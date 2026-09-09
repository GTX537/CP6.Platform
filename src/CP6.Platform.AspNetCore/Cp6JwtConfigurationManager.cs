using System.Net;
using System.Net.Http.Headers;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Platform.AspNetCore;

internal sealed class Cp6DeferredConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
{
    private Lazy<Cp6JwtConfigurationManager>? inner;

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
        GetManager().GetConfigurationAsync(cancel);

    public void RequestRefresh()
    {
        var current = Volatile.Read(ref inner);
        if (current?.IsValueCreated == true)
        {
            current.Value.RequestRefresh();
        }
    }

    public bool Configure(Func<Cp6JwtConfigurationManager> factory) =>
        Interlocked.CompareExchange(
            ref inner,
            new Lazy<Cp6JwtConfigurationManager>(factory, LazyThreadSafetyMode.ExecutionAndPublication),
            null) is null;

    private Cp6JwtConfigurationManager GetManager() =>
        (Volatile.Read(ref inner) ?? throw new Cp6JwtConfigurationException()).Value;
}

internal sealed class Cp6JwtBearerPostConfigure(
    string authenticationScheme,
    Cp6JwtBearerProfile profile,
    TimeProvider timeProvider) : IPostConfigureOptions<JwtBearerOptions>, IDisposable
{
    private readonly object gate = new();
    private readonly List<Cp6JwtConfigurationManager> managers = [];
    private bool disposed;

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (!string.Equals(name, authenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        Cp6DeferredConfigurationManager deferred;
        if (options.ConfigurationManager is Cp6DeferredConfigurationManager configured)
        {
            deferred = configured;
        }
        else if (options.ConfigurationManager is BaseConfigurationManager)
        {
            deferred = new Cp6DeferredConfigurationManager();
            options.ConfigurationManager = deferred;
        }
        else
        {
            return;
        }

        if (!deferred.Configure(() => CreateAndTrack(options)))
        {
            return;
        }
    }

    private Cp6JwtConfigurationManager CreateAndTrack(JwtBearerOptions options)
    {
        var (client, ownsClient) = CreateBackchannel(options);
        var manager = new Cp6JwtConfigurationManager(profile, client, ownsClient, timeProvider);
        lock (gate)
        {
            if (disposed)
            {
                manager.Dispose();
                throw new ObjectDisposedException(nameof(Cp6JwtBearerPostConfigure));
            }

            managers.Add(manager);
        }

        return manager;
    }

    public void Dispose()
    {
        List<Cp6JwtConfigurationManager> owned;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            owned = [.. managers];
            managers.Clear();
        }

        foreach (var manager in owned)
        {
            manager.Dispose();
        }
    }

    private static (HttpClient Client, bool OwnsClient) CreateBackchannel(JwtBearerOptions options)
    {
        if (options.Backchannel is not null)
        {
            RejectCredentialDefaults(options.Backchannel.DefaultRequestHeaders);
            return (options.Backchannel, false);
        }

        if (options.BackchannelHttpHandler is not null)
        {
            return (new HttpClient(options.BackchannelHttpHandler, disposeHandler: false)
            {
                Timeout = Timeout.InfiniteTimeSpan
            }, true);
        }

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseDefaultCredentials = false,
            Credentials = null,
            DefaultProxyCredentials = null
        };
        return (new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        }, true);
    }

    private static void RejectCredentialDefaults(HttpRequestHeaders headers)
    {
        if (headers.Authorization is not null ||
            headers.Contains("Cookie") ||
            headers.Contains("Proxy-Authorization"))
        {
            throw new Cp6JwtConfigurationProtocolException();
        }
    }
}

internal sealed class Cp6JwtConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>, IDisposable
{
    internal static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan DefaultFreshness = TimeSpan.FromSeconds(300);
    internal static readonly TimeSpan MaximumFreshness = TimeSpan.FromSeconds(300);
    internal static readonly TimeSpan MaximumTrustAge = TimeSpan.FromSeconds(900);
    internal static readonly TimeSpan UnknownKidRefreshWindow = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(30);
    internal const int MaximumBodyBytes = 256 * 1024;

    private readonly Cp6JwtBearerProfile profile;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly TimeProvider timeProvider;
    private readonly Uri metadataUri;
    private readonly Uri jwksUri;
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private CacheEntry? cache;
    private long? lastFailure;
    private long? lastUnknownKidAttempt;
    private int refreshRequested;
    private bool disposed;

    public Cp6JwtConfigurationManager(
        Cp6JwtBearerProfile profile,
        HttpClient client,
        bool ownsClient,
        TimeProvider timeProvider)
    {
        this.profile = profile;
        this.client = client;
        this.ownsClient = ownsClient;
        this.timeProvider = timeProvider;
        var authority = profile.Authority.TrimEnd('/');
        metadataUri = new Uri($"{authority}/.well-known/openid-configuration", UriKind.Absolute);
        jwksUri = new Uri($"{authority}/.well-known/jwks.json", UriKind.Absolute);
    }

    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancel.ThrowIfCancellationRequested();
        var snapshot = Volatile.Read(ref cache);
        var now = timeProvider.GetTimestamp();
        if (Volatile.Read(ref refreshRequested) == 0 && snapshot is not null && IsFresh(snapshot, now))
        {
            return snapshot.Configuration;
        }

        await refreshGate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            cancel.ThrowIfCancellationRequested();
            snapshot = Volatile.Read(ref cache);
            now = timeProvider.GetTimestamp();
            var unknownKidRefresh = Interlocked.Exchange(ref refreshRequested, 0) != 0;
            var hasFreshReusableCache = snapshot is not null && IsFresh(snapshot, now);

            if (!unknownKidRefresh && hasFreshReusableCache)
            {
                return snapshot!.Configuration;
            }

            var extraUnknownKidRefresh = unknownKidRefresh && hasFreshReusableCache;
            if (extraUnknownKidRefresh && lastUnknownKidAttempt is not null &&
                Elapsed(lastUnknownKidAttempt.Value, now) < UnknownKidRefreshWindow)
            {
                return snapshot!.Configuration;
            }

            if (lastFailure is not null && Elapsed(lastFailure.Value, now) < FailureBackoff)
            {
                return UseAfterUnavailable(snapshot, now);
            }

            if (extraUnknownKidRefresh)
            {
                lastUnknownKidAttempt = now;
            }

            try
            {
                var fetched = await FetchAsync(cancel).ConfigureAwait(false);
                var completedAt = timeProvider.GetTimestamp();
                var elapsedDuringFetch = Elapsed(fetched.RetrievedAt, completedAt);
                if (elapsedDuringFetch >= MaximumTrustAge ||
                    fetched.ResponseAge >= MaximumTrustAge - elapsedDuringFetch)
                {
                    throw new Cp6JwtConfigurationProtocolException();
                }

                lastFailure = null;
                if (fetched.NoStore)
                {
                    Volatile.Write(ref cache, null);
                    return fetched.Configuration;
                }

                var entry = new CacheEntry(
                    fetched.Configuration,
                    fetched.RetrievedAt,
                    fetched.ResponseAge,
                    fetched.Freshness,
                    fetched.NoCache,
                    fetched.MustRevalidate);
                Volatile.Write(ref cache, entry);
                return entry.Configuration;
            }
            catch (Cp6JwtConfigurationUnavailableException)
            {
                var completedAt = timeProvider.GetTimestamp();
                lastFailure = completedAt;
                return UseAfterUnavailable(snapshot, completedAt);
            }
            catch (Cp6JwtConfigurationProtocolException)
            {
                Volatile.Write(ref cache, null);
                lastFailure = timeProvider.GetTimestamp();
                throw;
            }
        }
        finally
        {
            refreshGate.Release();
        }
    }

    public void RequestRefresh() => Interlocked.Exchange(ref refreshRequested, 1);

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        refreshGate.Dispose();
        if (ownsClient)
        {
            client.Dispose();
        }
    }

    private OpenIdConnectConfiguration UseAfterUnavailable(CacheEntry? snapshot, long now)
    {
        if (snapshot is not null)
        {
            var age = Age(snapshot, now);
            if (age < MaximumTrustAge &&
                (age < snapshot.Freshness || (!snapshot.NoCache && !snapshot.MustRevalidate)))
            {
                return snapshot.Configuration;
            }

            if (age >= MaximumTrustAge)
            {
                Volatile.Write(ref cache, null);
            }
        }

        throw new Cp6JwtConfigurationUnavailableException();
    }

    private async Task<FetchResult> FetchAsync(CancellationToken callerCancellation)
    {
        var metadata = await FetchDocumentAsync(metadataUri, callerCancellation).ConfigureAwait(false);
        ValidateMetadata(metadata.Body);
        var jwks = await FetchDocumentAsync(jwksUri, callerCancellation).ConfigureAwait(false);
        var configuration = ParseJwks(jwks.Body);
        if (jwks.Age >= MaximumTrustAge)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }
        var cacheControl = jwks.CacheControl;
        var freshness = cacheControl?.MaxAge ?? DefaultFreshness;
        if (freshness < TimeSpan.Zero)
        {
            freshness = TimeSpan.Zero;
        }
        if (freshness > MaximumFreshness)
        {
            freshness = MaximumFreshness;
        }

        var noCache = cacheControl?.NoCache == true;
        if (noCache)
        {
            freshness = TimeSpan.Zero;
        }

        return new FetchResult(
            configuration,
            freshness,
            jwks.Age < TimeSpan.Zero ? TimeSpan.Zero : jwks.Age,
            jwks.ReceivedAt,
            cacheControl?.NoStore == true,
            noCache,
            cacheControl?.MustRevalidate == true);
    }

    private async Task<FetchDocument> FetchDocumentAsync(Uri uri, CancellationToken callerCancellation)
    {
        using var deadline = new CancellationTokenSource(FetchTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation, deadline.Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token).ConfigureAwait(false);
            if (response.RequestMessage?.RequestUri is null || response.RequestMessage.RequestUri != uri)
            {
                throw new Cp6JwtConfigurationProtocolException();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests ||
                (int)response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                throw new Cp6JwtConfigurationUnavailableException();
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Cp6JwtConfigurationProtocolException();
            }

            var receivedAt = timeProvider.GetTimestamp();
            var cacheControl = ParseCacheControl(response.Headers);
            var age = ParseAge(response.Headers);
            var body = await ReadBoundedBodyAsync(response.Content, linked.Token).ConfigureAwait(false);
            return new FetchDocument(body, cacheControl, age, receivedAt);
        }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new Cp6JwtConfigurationUnavailableException();
        }
        catch (HttpRequestException)
        {
            throw new Cp6JwtConfigurationUnavailableException();
        }
        catch (IOException)
        {
            throw new Cp6JwtConfigurationUnavailableException();
        }
    }

    private static CacheControlHeaderValue? ParseCacheControl(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Cache-Control", out var values))
        {
            return null;
        }

        var raw = string.Join(",", values);
        if (!CacheControlHeaderValue.TryParse(raw, out var parsed))
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        return parsed;
    }

    private static TimeSpan ParseAge(HttpResponseHeaders headers)
    {
        if (!headers.TryGetValues("Age", out var values))
        {
            return TimeSpan.Zero;
        }

        var raw = values.ToArray();
        if (raw.Length != 1 ||
            !long.TryParse(raw[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0 ||
            seconds > (long)TimeSpan.MaxValue.TotalSeconds)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaximumBodyBytes)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > MaximumBodyBytes)
            {
                throw new Cp6JwtConfigurationProtocolException();
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private void ValidateMetadata(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("issuer", out var issuer) ||
                issuer.ValueKind != JsonValueKind.String ||
                !string.Equals(issuer.GetString(), profile.Issuer, StringComparison.Ordinal))
            {
                throw new Cp6JwtConfigurationProtocolException();
            }
        }
        catch (JsonException)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }
    }

    private OpenIdConnectConfiguration ParseJwks(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("keys", out var keys) ||
                keys.ValueKind != JsonValueKind.Array ||
                keys.GetArrayLength() is < 1 or > 32)
            {
                throw new Cp6JwtConfigurationProtocolException();
            }

            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = profile.Issuer,
                JwksUri = jwksUri.AbsoluteUri
            };
            var kids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys.EnumerateArray())
            {
                configuration.SigningKeys.Add(ParseRsaKey(key, kids));
            }

            return configuration;
        }
        catch (JsonException)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }
        catch (FormatException)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }
        catch (CryptographicException)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }
    }

    private static SecurityKey ParseRsaKey(JsonElement key, HashSet<string> kids)
    {
        if (key.ValueKind != JsonValueKind.Object ||
            !ReadRequiredString(key, "kty", out var keyType) || keyType != "RSA" ||
            !ReadRequiredString(key, "kid", out var kid) || !kids.Add(kid) ||
            !ReadRequiredString(key, "n", out var modulus) ||
            !ReadRequiredString(key, "e", out var exponent) ||
            !IsCompatibleOptionalValue(key, "alg", "RS256") ||
            !IsCompatibleOptionalValue(key, "use", "sig") ||
            HasPrivateMaterial(key))
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        var parameters = new RSAParameters
        {
            Modulus = Base64UrlEncoder.DecodeBytes(modulus),
            Exponent = Base64UrlEncoder.DecodeBytes(exponent)
        };
        if (parameters.Modulus.Length == 0 || parameters.Exponent.Length == 0)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        var modulusValue = new BigInteger(parameters.Modulus, isUnsigned: true, isBigEndian: true);
        var exponentValue = new BigInteger(parameters.Exponent, isUnsigned: true, isBigEndian: true);
        if (modulusValue.GetBitLength() < 2048 ||
            exponentValue < 3 ||
            exponentValue.IsEven ||
            exponentValue >= modulusValue)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        using var validator = RSA.Create();
        validator.ImportParameters(parameters);
        if (validator.KeySize < 2048)
        {
            throw new Cp6JwtConfigurationProtocolException();
        }

        return new RsaSecurityKey(parameters) { KeyId = kid };
    }

    private static bool ReadRequiredString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static bool IsCompatibleOptionalValue(JsonElement element, string name, string expected) =>
        !element.TryGetProperty(name, out var property) ||
        (property.ValueKind == JsonValueKind.String && string.Equals(property.GetString(), expected, StringComparison.Ordinal));

    private static bool HasPrivateMaterial(JsonElement element)
    {
        foreach (var name in new[] { "d", "p", "q", "dp", "dq", "qi", "oth" })
        {
            if (element.TryGetProperty(name, out _))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsFresh(CacheEntry entry, long now) =>
        Age(entry, now) < entry.Freshness && Age(entry, now) < MaximumTrustAge;

    private TimeSpan Age(CacheEntry entry, long now)
    {
        var elapsed = Elapsed(entry.RetrievedAt, now);
        if (elapsed >= MaximumTrustAge || entry.ResponseAge >= MaximumTrustAge - elapsed)
        {
            return MaximumTrustAge;
        }

        return entry.ResponseAge + elapsed;
    }

    private TimeSpan Elapsed(long start, long end) => end <= start ? TimeSpan.Zero : timeProvider.GetElapsedTime(start, end);

    private sealed record CacheEntry(
        OpenIdConnectConfiguration Configuration,
        long RetrievedAt,
        TimeSpan ResponseAge,
        TimeSpan Freshness,
        bool NoCache,
        bool MustRevalidate);

    private sealed record FetchResult(
        OpenIdConnectConfiguration Configuration,
        TimeSpan Freshness,
        TimeSpan ResponseAge,
        long RetrievedAt,
        bool NoStore,
        bool NoCache,
        bool MustRevalidate);

    private sealed record FetchDocument(
        byte[] Body,
        CacheControlHeaderValue? CacheControl,
        TimeSpan Age,
        long ReceivedAt);
}

internal class Cp6JwtConfigurationException : InvalidOperationException
{
    public Cp6JwtConfigurationException() : base("Bearer signing-key configuration is unavailable.")
    {
    }
}

internal sealed class Cp6JwtConfigurationUnavailableException : Cp6JwtConfigurationException;

internal sealed class Cp6JwtConfigurationProtocolException : Cp6JwtConfigurationException;

using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;

internal static class Rfc3161Preflight
{
    private const string TimestampEkuOid = "1.3.6.1.5.5.7.3.8";

    public static async Task<Rfc3161PreflightResult> ProbeAsync(
        string serviceUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(serviceUrl, UriKind.Absolute, out var service) ||
            service.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Timestamp service URL must be absolute HTTP(S).", nameof(serviceUrl));
        }

        var payload = RandomNumberGenerator.GetBytes(32);
        var digest = SHA256.HashData(payload);
        var nonceBytes = RandomNumberGenerator.GetBytes(sizeof(long));
        var request = Rfc3161TimestampRequest.CreateFromHash(
            digest,
            HashAlgorithmName.SHA256,
            requestedPolicyId: null,
            nonceBytes,
            requestSignerCertificates: true,
            extensions: null);
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var content = new ByteArrayContent(request.Encode());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
            using var response = await client.PostAsync(service, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var token = request.ProcessResponse(responseBytes, out var bytesConsumed);
            if (bytesConsumed != responseBytes.Length ||
                !token.VerifySignatureForHash(
                    digest,
                    HashAlgorithmName.SHA256,
                    out var signer,
                    token.AsSignedCms().Certificates))
            {
                throw new CryptographicException("RFC3161 response is not a complete valid token for the request hash.");
            }

            using (signer)
            using (var chain = new X509Chain())
            {
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                chain.ChainPolicy.VerificationTime = token.TokenInfo.Timestamp.UtcDateTime;
                chain.ChainPolicy.DisableCertificateDownloads = false;
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(TimestampEkuOid));
                foreach (var certificate in token.AsSignedCms().Certificates)
                {
                    if (!certificate.RawData.AsSpan().SequenceEqual(signer.RawData))
                    {
                        chain.ChainPolicy.ExtraStore.Add(certificate);
                    }
                }

                if (!chain.Build(signer))
                {
                    throw new CryptographicException("RFC3161 signer does not build to a system root with online revocation.");
                }

                var chainHashes = chain.ChainElements.Cast<X509ChainElement>()
                    .Select(element => Convert.ToHexString(SHA256.HashData(element.Certificate.RawData)).ToLowerInvariant())
                    .ToArray();
                return new(
                    true,
                    token.TokenInfo.PolicyId.Value ?? throw new CryptographicException("RFC3161 policy OID is missing."),
                    token.TokenInfo.Timestamp,
                    chainHashes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            CryptographicOperations.ZeroMemory(digest);
            CryptographicOperations.ZeroMemory(nonceBytes);
        }
    }
}

internal sealed record Rfc3161PreflightResult(
    bool Success,
    string PolicyOid,
    DateTimeOffset TimestampUtc,
    IReadOnlyList<string> CertificateChainSha256);

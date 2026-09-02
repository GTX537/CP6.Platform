using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

internal static class NuGetPackageDownloader
{
    public static async Task DownloadAsync(
        string sourceUrl,
        string packageId,
        string version,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destinationPath))
        {
            throw new IOException("Package download destination already exists.");
        }

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var sourceUri) || sourceUri.Scheme != Uri.UriSchemeHttps ||
            !NuGetVersion.TryParse(version, out var parsedVersion) ||
            parsedVersion.IsPrerelease ||
            !string.IsNullOrEmpty(parsedVersion.Metadata) ||
            !string.Equals(version, parsedVersion.ToNormalizedString(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Package source and stable version must be canonical.");
        }

        var packageSource = new PackageSource(sourceUri.AbsoluteUri);
        var username = Environment.GetEnvironmentVariable("NUGET_FEED_USERNAME");
        var token = Environment.GetEnvironmentVariable("NUGET_FEED_TOKEN");
        if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(token))
        {
            packageSource.Credentials = new PackageSourceCredential(
                packageSource.Source,
                username,
                token,
                isPasswordClearText: true,
                validAuthenticationTypesText: "Basic");
        }

        var parent = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var repository = new SourceRepository(packageSource, Repository.Provider.GetCoreV3());
        var resource = await repository.GetResourceAsync<FindPackageByIdResource>(cancellationToken);
        using var cache = new SourceCacheContext { NoCache = true, DirectDownload = true };
        var destinationCreated = false;
        try
        {
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true);
            destinationCreated = true;
            var found = await resource.CopyNupkgToStreamAsync(
                packageId,
                parsedVersion,
                destination,
                cache,
                NullLogger.Instance,
                cancellationToken);
            if (!found)
            {
                throw new InvalidOperationException("Requested package identity was not found.");
            }
        }
        catch
        {
            if (destinationCreated && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw;
        }
    }
}

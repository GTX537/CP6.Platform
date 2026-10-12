using System.Security.Cryptography;
using System.Text.Json;

namespace CP6.Platform.Messaging.NativeContracts;

public sealed record Cp6NativeOpenApiIdentity(string OpenApi, string Title, string Version, string SchemaRoot, string SchemaSha256);
public sealed record Cp6NativeRegistryIdentity(string CodecNamespace, string SchemaFile, string SchemaSha256);

/// <summary>
/// Routes the three native identities assigned to S01-09. Profile selection is trusted caller
/// configuration, never an added envelope field. Resolution verifies identity/assets only;
/// native payload validation and business authorization remain the consumer's responsibility.
/// </summary>
public sealed class Cp6NativeContractCatalog
{
    public const string PackageVersion = "CP6-S01-CONTRACT-INPUTS/1.0.0";
    public const string ManifestSha256 = "2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e";

    private readonly Cp6NativeOpenApiIdentity inq;
    private readonly IReadOnlyDictionary<string, Cp6NativeRegistryIdentity> registries;

    private Cp6NativeContractCatalog(Cp6NativeOpenApiIdentity inq, Dictionary<string, Cp6NativeRegistryIdentity> registries)
    {
        this.inq = inq;
        this.registries = registries;
    }

    public static Cp6NativeContractCatalog LoadS01(ReadOnlyMemory<byte> manifest)
    {
        var owned = manifest.ToArray();
        VerifyHash(owned, ManifestSha256);
        using var document = Cp6NativeJson.Parse(owned);
        var root = document.RootElement;
        if (Text(root, "package_version") != PackageVersion) throw UnknownContract();
        var profiles = root.GetProperty("profiles").EnumerateArray().ToDictionary(item => Text(item, "id"), StringComparer.Ordinal);
        var sources = root.GetProperty("sources").EnumerateArray().ToDictionary(item => Text(item, "id"), StringComparer.Ordinal);
        var inq = profiles["crm-inq-r3-v15"].GetProperty("schema_or_type_identity");
        var info = inq.GetProperty("info");
        var openApi = new Cp6NativeOpenApiIdentity(Text(inq, "openapi"), Text(info, "title"), Text(info, "version"),
            Text(inq, "schema_root"), Text(sources[Text(inq, "source_id")], "composed_sha256"));
        var registries = new Dictionary<string, Cp6NativeRegistryIdentity>(StringComparer.Ordinal);
        foreach (var profile in new[] { "erp-credit-current-v1", "erp-credit-frozen-r8" })
        {
            var identity = profiles[profile].GetProperty("schema_or_type_identity");
            registries.Add(profile, new(Text(identity, "csharp_namespace"), "ERP-CREDIT-01_TYPE-REGISTRY.json", Text(identity, "type_registry_sha256")));
        }
        return new(openApi, registries);
    }

    public string ResolveOpenApi(string profileId, Cp6NativeOpenApiIdentity identity, ReadOnlyMemory<byte> schema)
    {
        if (profileId != "crm-inq-r3-v15" || identity != inq) throw UnknownContract();
        VerifyHash(schema.Span, inq.SchemaSha256);
        return profileId;
    }

    public string ResolveRegistry(string profileId, Cp6NativeRegistryIdentity identity, ReadOnlyMemory<byte> registry)
    {
        if (profileId is null || !registries.TryGetValue(profileId, out var expected) || identity != expected) throw UnknownContract();
        VerifyHash(registry.Span, expected.SchemaSha256);
        return profileId;
    }

    private static void VerifyHash(ReadOnlySpan<byte> bytes, string expected)
    {
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expected)
            throw new InvalidDataException("Native contract asset or manifest hash mismatch.");
    }

    private static InvalidDataException UnknownContract() => new("Native contract identity is not configured.");
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
}

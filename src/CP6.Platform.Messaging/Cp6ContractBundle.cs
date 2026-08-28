using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Json.Schema;

namespace CP6.Platform.Messaging;

/// <summary>
/// Loads a content-addressed CP6 event contract bundle from a directory.
/// </summary>
public sealed class Cp6ContractBundle
{
    public const string IndexFileName = "contract-bundle.v1.json";
    public const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

    private readonly IReadOnlyDictionary<string, JsonSchema> schemasById;
    private readonly IReadOnlyDictionary<string, Cp6ContractBundleEntry> entriesByType;

    private Cp6ContractBundle(
        string rootDirectory,
        BundleManifest manifest,
        IReadOnlyList<Cp6ContractBundleEntry> entries,
        IReadOnlyDictionary<string, JsonSchema> schemasById)
    {
        RootDirectory = rootDirectory;
        BundleVersion = manifest.BundleVersion;
        CloudEventsSpecVersion = manifest.CloudEventsSpecVersion;
        JsonSchemaDialect = manifest.JsonSchemaDialect;
        Entries = entries;
        this.schemasById = schemasById;
        entriesByType = entries.ToDictionary(entry => entry.EventType, StringComparer.Ordinal);
    }

    public string RootDirectory { get; }

    public string BundleVersion { get; }

    public string CloudEventsSpecVersion { get; }

    public string JsonSchemaDialect { get; }

    public IReadOnlyList<Cp6ContractBundleEntry> Entries { get; }

    public static Cp6ContractBundle Load(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Contract bundle directory does not exist: {root}");
        }

        var indexPath = Path.Combine(root, IndexFileName);
        if (!File.Exists(indexPath))
        {
            throw new InvalidDataException($"Contract bundle index is missing: {IndexFileName}");
        }

        var manifest = JsonSerializer.Deserialize<BundleManifest>(File.ReadAllText(indexPath), SerializerOptions)
            ?? throw new InvalidDataException("Contract bundle index is empty.");
        ValidateManifest(manifest);

        var entries = new List<Cp6ContractBundleEntry>(manifest.Entries.Count);
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        var buildOptions = new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new SchemaRegistry()
        };
        foreach (var sourceEntry in manifest.Entries)
        {
            if (!SemanticVersion.TryParse(sourceEntry.SchemaVersion, out _))
            {
                throw new InvalidDataException($"Schema version is not a three-part semantic version for '{sourceEntry.EventType}'.");
            }

            var identity = Cp6EventContractIdentity.Parse(sourceEntry.EventType);
            if (!Uri.TryCreate(sourceEntry.SchemaId, UriKind.Absolute, out var schemaId) || schemaId != identity.SchemaId)
            {
                throw new InvalidDataException($"Schema id is not canonical for event type '{sourceEntry.EventType}'.");
            }

            var schemaPath = ResolveAsset(root, sourceEntry.SchemaPath);
            VerifyHash(schemaPath, sourceEntry.SchemaSha256);
            var schemaText = File.ReadAllText(schemaPath);
            var schema = JsonSchema.FromText(schemaText, buildOptions);
            using (var schemaDocument = JsonDocument.Parse(schemaText))
            {
                var schemaRoot = schemaDocument.RootElement;
                if (schemaRoot.GetProperty("$schema").GetString() != Draft202012 ||
                    schemaRoot.GetProperty("$id").GetString() != sourceEntry.SchemaId)
                {
                    throw new InvalidDataException($"Schema metadata does not match bundle entry '{sourceEntry.EventType}'.");
                }
            }

            if (!schemas.TryAdd(sourceEntry.SchemaId, schema))
            {
                throw new InvalidDataException($"Duplicate schema id '{sourceEntry.SchemaId}'.");
            }

            ValidateExampleMatrix(sourceEntry);
            var examples = new List<Cp6ContractExample>(sourceEntry.Examples.Count);
            foreach (var sourceExample in sourceEntry.Examples)
            {
                var examplePath = ResolveAsset(root, sourceExample.Path);
                VerifyHash(examplePath, sourceExample.Sha256);
                examples.Add(new Cp6ContractExample(sourceExample.Name, sourceExample.Path, sourceExample.Valid, sourceExample.Sha256));
            }

            entries.Add(new Cp6ContractBundleEntry(
                sourceEntry.EventType,
                sourceEntry.SchemaVersion,
                sourceEntry.SchemaId,
                sourceEntry.SchemaPath,
                sourceEntry.SchemaSha256,
                examples));
        }

        if (entries.Select(entry => entry.EventType).Distinct(StringComparer.Ordinal).Count() != entries.Count)
        {
            throw new InvalidDataException("Contract bundle contains duplicate event types.");
        }

        return new Cp6ContractBundle(root, manifest, entries, schemas);
    }

    internal bool TryResolve(string eventType, string schemaId, string schemaVersion, out Cp6ContractBundleEntry entry, out JsonSchema schema)
    {
        if (entriesByType.TryGetValue(eventType, out var resolvedEntry) &&
            string.Equals(resolvedEntry.SchemaId, schemaId, StringComparison.Ordinal) &&
            string.Equals(resolvedEntry.SchemaVersion, schemaVersion, StringComparison.Ordinal) &&
            schemasById.TryGetValue(schemaId, out var resolvedSchema))
        {
            entry = resolvedEntry;
            schema = resolvedSchema;
            return true;
        }

        entry = null!;
        schema = null!;
        return false;
    }

    public string GetAssetPath(string relativePath) => ResolveAsset(RootDirectory, relativePath);

    private static void ValidateManifest(BundleManifest manifest)
    {
        if (!SemanticVersion.TryParse(manifest.BundleVersion, out _))
        {
            throw new InvalidDataException("bundleVersion must be a three-part semantic version.");
        }

        if (manifest.CloudEventsSpecVersion != "1.0")
        {
            throw new InvalidDataException("Only CloudEvents specification version 1.0 is supported.");
        }

        if (manifest.JsonSchemaDialect != Draft202012)
        {
            throw new InvalidDataException("Only JSON Schema Draft 2020-12 is supported.");
        }

        if (manifest.Entries is null || manifest.Entries.Count == 0)
        {
            throw new InvalidDataException("Contract bundle must contain at least one event contract.");
        }
    }

    private static void ValidateExampleMatrix(BundleEntryManifest entry)
    {
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["valid"] = true,
            ["missing-required"] = false,
            ["unknown-optional"] = true,
            ["wrong-type"] = false,
            ["pii-negative"] = false
        };
        if (entry.Examples is null ||
            entry.Examples.Count != expected.Count ||
            entry.Examples.Select(example => example.Name).Distinct(StringComparer.Ordinal).Count() != entry.Examples.Count)
        {
            throw new InvalidDataException($"Event contract '{entry.EventType}' does not contain the required five-example matrix.");
        }

        var actual = entry.Examples.ToDictionary(example => example.Name, example => example.Valid, StringComparer.Ordinal);
        if (actual.Count != expected.Count || expected.Any(pair => !actual.TryGetValue(pair.Key, out var value) || value != pair.Value))
        {
            throw new InvalidDataException($"Event contract '{entry.EventType}' does not contain the required five-example matrix.");
        }
    }

    private static string ResolveAsset(string root, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Contract bundle asset paths must be relative.");
        }

        var resolved = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), root);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
        {
            throw new InvalidDataException($"Contract bundle asset is missing or escapes the bundle root: {relativePath}");
        }

        return resolved;
    }

    private static void VerifyHash(string path, string expectedHash)
    {
        if (expectedHash.Length != 64 || expectedHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException($"Asset hash is not a SHA-256 value: {Path.GetFileName(path)}");
        }

        using var stream = File.OpenRead(path);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Contract bundle asset hash mismatch: {Path.GetFileName(path)}");
        }
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private sealed record BundleManifest(
        [property: JsonPropertyName("bundleVersion")] string BundleVersion,
        [property: JsonPropertyName("cloudEventsSpecVersion")] string CloudEventsSpecVersion,
        [property: JsonPropertyName("jsonSchemaDialect")] string JsonSchemaDialect,
        [property: JsonPropertyName("entries")] IReadOnlyList<BundleEntryManifest> Entries);

    private sealed record BundleEntryManifest(
        [property: JsonPropertyName("eventType")] string EventType,
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("schemaId")] string SchemaId,
        [property: JsonPropertyName("schemaPath")] string SchemaPath,
        [property: JsonPropertyName("schemaSha256")] string SchemaSha256,
        [property: JsonPropertyName("examples")] IReadOnlyList<ExampleManifest> Examples);

    private sealed record ExampleManifest(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("valid")] bool Valid,
        [property: JsonPropertyName("sha256")] string Sha256);

    private readonly record struct SemanticVersion(int Major, int Minor, int Patch)
    {
        public static bool TryParse(string value, out SemanticVersion result)
        {
            result = default;
            var components = value.Split('.');
            if (components.Length != 3 ||
                !int.TryParse(components[0], out var major) || major < 0 ||
                !int.TryParse(components[1], out var minor) || minor < 0 ||
                !int.TryParse(components[2], out var patch) || patch < 0 ||
                value != $"{major}.{minor}.{patch}")
            {
                return false;
            }

            result = new SemanticVersion(major, minor, patch);
            return true;
        }
    }
}

public sealed record Cp6ContractBundleEntry(
    string EventType,
    string SchemaVersion,
    string SchemaId,
    string SchemaPath,
    string SchemaSha256,
    IReadOnlyList<Cp6ContractExample> Examples);

public sealed record Cp6ContractExample(string Name, string Path, bool Valid, string Sha256);

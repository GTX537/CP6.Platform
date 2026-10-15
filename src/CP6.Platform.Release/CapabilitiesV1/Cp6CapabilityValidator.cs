using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace CP6.Platform.Release.CapabilitiesV1;

public static class Cp6CapabilityValidator
{
    public const string SchemaId = "https://schemas.cp6.dev/release/capabilities/v1/capability-matrix";
    public const string Repository = "https://github.com/GTX537/CP6.Platform";
    private static readonly string[] PackageIds = ["CP6.Platform.Abstractions", "CP6.Platform.AspNetCore",
        "CP6.Platform.Contracts", "CP6.Platform.Deployment", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging", "CP6.Platform.Release"];
    private static readonly string[] RowFields = ["route", "consumerApp", "host", "consumerSourceGitSha", "producerPackageId",
        "sdk", "runtimeFramework", "os", "architecture", "sourceSchemaSha256", "packagedSchemaSha256", "packagedSchemaPath",
        "semanticValidatorIdentity", "messageType", "schemaVersionsAccepted", "databaseProvider", "databaseDriver", "migrationSet",
        "requiredCapabilityId", "requiredCapabilityVersion", "profile", "apiSignatureRef", "registrationPointRef", "configurationSha256",
        "compatibilityDecision", "evidenceSha256", "disableReason"];

    public static byte[] ValidateShape(ReadOnlySpan<byte> manifest)
    {
        var bytes = RequireCanonical(manifest);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        Cp6ReleaseJsonRules.RequireExactObject(root, "$schemaId", "repository", "sourceGitSha", "lane", "version",
            "packages", "evidenceSha256", "capabilities", "compatibility");
        Exact(root, "$schemaId", SchemaId); Exact(root, "repository", Repository);
        var source = Text(root, "sourceGitSha"); GitSha(source, "source-identity");
        var lane = Text(root, "lane"); var version = Text(root, "version");
        if (lane == "TaskLocal")
        {
            if (!Regex.IsMatch(version, @"\A0\.10\.3-wp02\.27\.g" + source + @"\.[1-9][0-9]*\z", RegexOptions.CultureInvariant))
                throw Error("package-version", "Task-local packages require an exact unique WP02-27 prerelease identity.");
        }
        else if (lane != "FormalPublished" || version is not ("0.10.0" or "0.10.1" or "0.10.2" or "0.10.3"))
            throw Error("package-version", "Unknown publication lane or unapproved formal version.");
        var packages = Array(root, "packages");
        if (packages.GetArrayLength() != PackageIds.Length) throw Error("package-set", "The exact seven packages are required.");
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < PackageIds.Length; i++)
        {
            var p = packages[i];
            Cp6ReleaseJsonRules.RequireExactObject(p, "packageId", "version", "sourceGitSha", "sha256", "feedIdentity");
            Exact(p, "packageId", PackageIds[i]); Exact(p, "version", version); Exact(p, "sourceGitSha", source);
            var hash = Text(p, "sha256"); Sha256(hash, "package-hash");
            if (!hashes.Add(hash)) throw Error("package-hash", "Package hashes must be distinct.");
            var prefix = lane == "TaskLocal" ? "task-local:wp02-27#" : "https://nuget.pkg.github.com/GTX537/index.json#";
            Exact(p, "feedIdentity", $"{prefix}{PackageIds[i]}/{version}");
        }
        var evidence = StringSet(root, "evidenceSha256", true);
        foreach (var hash in evidence) Sha256(hash, "evidence-identity");
        var capabilities = Array(root, "capabilities");
        var ids = new List<string>();
        foreach (var c in capabilities.EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(c, "id", "version", "profiles", "scope");
            var id = Text(c, "id"); ids.Add(id); Exact(c, "version", "1.0.0");
            var (profiles, scope) = id switch
            {
                "S01-09" => (new[] { "crm-inq-r3-v15", "erp-credit-current-v1", "erp-credit-frozen-r8" }, new[] { "native-contract-routing", "original-json-bytes" }),
                "ctx-binding-v1" => (new[] { "ctx-v1" }, new[] { "Background", "Http", "Message" }),
                _ => throw Error("unknown-capability", "Capability is not supported by this versioned catalog.")
            };
            if (!StringSet(c, "profiles", true).SequenceEqual(profiles) || !StringSet(c, "scope", true).SequenceEqual(scope))
                throw Error("unknown-profile", "Capability profiles and scope must match the complete supported set.");
        }
        Cp6ReleaseJsonRules.RequireOrdinalSet(ids, "capability-set");
        if (lane == "FormalPublished" &&
            (version != "0.10.3" ? ids.Count != 0 : ids.Any(id => id != "ctx-binding-v1")))
            throw Error("capability-unavailable", "Formal 0.10.0-0.10.2 capabilities are empty; 0.10.3 permits only ctx-binding-v1.");
        var rowHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in Array(root, "compatibility").EnumerateArray())
        {
            Cp6ReleaseJsonRules.RequireExactObject(row, RowFields);
            foreach (var field in RowFields.Except(["schemaVersionsAccepted", "evidenceSha256", "disableReason"])) _ = Text(row, field);
            GitSha(Text(row, "consumerSourceGitSha"), "consumer-source");
            foreach (var field in new[] { "sourceSchemaSha256", "packagedSchemaSha256", "configurationSha256" })
                Sha256(Text(row, field), "row-hash");
            Exact(row, "packagedSchemaSha256", Text(row, "sourceSchemaSha256"));
            if (!PackageIds.Contains(Text(row, "producerPackageId"), StringComparer.Ordinal)) throw Error("package-set", "Unknown producer.");
            Exact(row, "databaseProvider", "PostgreSQL"); Exact(row, "requiredCapabilityVersion", "1.0.0");
            var id = Text(row, "requiredCapabilityId");
            if (!ids.Contains(id, StringComparer.Ordinal)) throw Error("unknown-capability", "Row requires an absent capability.");
            var capability = capabilities.EnumerateArray().Single(c => Text(c, "id") == id);
            if (!StringSet(capability, "profiles", true).Contains(Text(row, "profile"), StringComparer.Ordinal))
                throw Error("unknown-profile", "Row profile is not supported by its required capability.");
            _ = StringSet(row, "schemaVersionsAccepted", true);
            var refs = StringSet(row, "evidenceSha256", true);
            if (refs.Any(hash => !evidence.Contains(hash, StringComparer.Ordinal)) ||
                !refs.Contains(Text(row, "sourceSchemaSha256"), StringComparer.Ordinal))
                throw Error("evidence-identity", "Row evidence and source schema must be bound by the matrix.");
            var path = Text(row, "packagedSchemaPath");
            if (path.StartsWith('/') || path.Contains('\\') || path.Split('/').Any(segment => segment is "" or "." or ".."))
                throw Error("schema-path", "Packaged schema must use a relative canonical archive path.");
            var decision = Text(row, "compatibilityDecision");
            var reason = Cp6ReleaseJsonRules.RequireProperty(row, "disableReason", JsonValueKind.String).GetString()!;
            if (decision is not ("Allowed" or "Blocked" or "Unknown") || (decision == "Allowed" ? reason.Length != 0 : string.IsNullOrWhiteSpace(reason)))
                throw Error("compatibility-decision", "Decision and disable reason must agree.");
            var identity = JsonNode.Parse(row.GetRawText())!.AsObject();
            foreach (var outcome in new[] { "compatibilityDecision", "disableReason", "evidenceSha256" }) identity.Remove(outcome);
            var identityBytes = Cp6DeterministicJson.Canonicalize(System.Text.Encoding.UTF8.GetBytes(identity.ToJsonString()));
            if (!rowHashes.Add(Cp6DeterministicJson.Sha256Hex(identityBytes)))
                throw Error("duplicate-row", "One exact consumer identity cannot have duplicate or conflicting measured decisions.");
        }
        return bytes;
    }
    public static Cp6CapabilityEvaluation Evaluate(ReadOnlySpan<byte> manifest, Cp6CapabilityApproval approval,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> packages,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> evidence,
        ReadOnlySpan<byte> requestedRow, DateTimeOffset evaluationUtc,
        ReadOnlyMemory<byte> publication = default, Cp6PinnedNuGetTrustPolicy? trustPolicy = null,
        ICp6CapabilitySignatureVerifier? signatureVerifier = null)
    {
        ArgumentNullException.ThrowIfNull(approval); ArgumentNullException.ThrowIfNull(packages); ArgumentNullException.ThrowIfNull(evidence);
        var bytes = ValidateShape(manifest); var hash = Cp6DeterministicJson.Sha256Hex(bytes);
        using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
        if (hash != approval.ManifestSha256 || Text(root, "sourceGitSha") != approval.SourceGitSha)
            throw Error("approval-identity", "Matrix/source do not match independently approved identity.");
        var snapshots = Snapshot(packages);
        if (!snapshots.Keys.Order(StringComparer.Ordinal).SequenceEqual(PackageIds)) throw Error("package-set", "Archive set must be exact.");
        foreach (var p in Array(root, "packages").EnumerateArray())
        {
            var id = Text(p, "packageId"); var archive = snapshots[id];
            if (Cp6DeterministicJson.Sha256Hex(archive.Span) != Text(p, "sha256")) throw Error("package-hash", "Actual package bytes differ.");
            ValidateArchive(archive, p);
        }
        var evidenceSnapshots = Snapshot(evidence);
        var evidenceRefs = StringSet(root, "evidenceSha256", true);
        if (!evidenceSnapshots.Keys.Order(StringComparer.Ordinal).SequenceEqual(evidenceRefs)) throw Error("evidence-set", "Exact evidence bytes are required.");
        foreach (var item in evidenceSnapshots)
            if (Cp6DeterministicJson.Sha256Hex(item.Value.Span) != item.Key) throw Error("evidence-hash", "Evidence bytes differ.");
        foreach (var row in Array(root, "compatibility").EnumerateArray())
        {
            using var stream = new MemoryStream(snapshots[Text(row, "producerPackageId")].ToArray(), false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(Text(row, "packagedSchemaPath")) ?? throw Error("packaged-schema", "Required packaged schema is absent.");
            if (entry.Length > Cp6DeterministicJson.MaximumBytes) throw Error("packaged-schema", "Packaged schema exceeds the contract size limit.");
            using var data = new MemoryStream(); using (var input = entry.Open()) input.CopyTo(data);
            if (Cp6DeterministicJson.Sha256Hex(data.ToArray()) != Text(row, "packagedSchemaSha256"))
                throw Error("packaged-schema", "Packaged schema bytes do not match.");
        }
        var requestBytes = RequireCanonical(requestedRow);
        var requestedHash = Cp6DeterministicJson.Sha256Hex(requestBytes);
        var decision = Cp6CompatibilityDecision.Unknown;
        foreach (var row in Array(root, "compatibility").EnumerateArray())
            if (System.Text.Encoding.UTF8.GetBytes(row.GetRawText()).AsSpan().SequenceEqual(requestBytes))
                decision = Enum.Parse<Cp6CompatibilityDecision>(Text(row, "compatibilityDecision"));
        var formal = Text(root, "lane") == "FormalPublished";
        var trusted = false;
        string? publicationHash = null;
        if (formal)
        {
            if (trustPolicy is null || publication.IsEmpty || approval.CurrentTrustPolicySha256 != trustPolicy.ValidatedDocument.Sha256)
                throw Error("trust-policy", "Formal evaluation requires an independently approved current policy and original publication bytes.");
            var publicationSnapshot = publication.ToArray();
            var validated = Cp6FormalPackagePublicationValidator.ValidateFormalPackagePublication(publicationSnapshot, trustPolicy, evaluationUtc);
            publicationHash = validated.Sha256;
            if (!evidenceRefs.Contains(publicationHash, StringComparer.Ordinal) ||
                !evidenceSnapshots.TryGetValue(publicationHash, out var approvedPublication) ||
                !publicationSnapshot.AsSpan().SequenceEqual(approvedPublication.Span))
                throw Error("publication-evidence", "Original publication bytes must be bound by the independently approved matrix evidence.");
            using var pub = JsonDocument.Parse(publicationSnapshot);
            if (pub.RootElement.GetProperty("createdAtUtc").GetDateTimeOffset() > evaluationUtc)
                throw Error("future-publication", "A future publication record cannot establish current acceptance.");
            Exact(pub.RootElement, "sourceGitSha", approval.SourceGitSha); Exact(pub.RootElement, "version", Text(root, "version"));
            var published = pub.RootElement.GetProperty("packages");
            for (var i = 0; i < PackageIds.Length; i++)
            {
                Exact(published[i], "publishedPackageSha256", Text(Array(root, "packages")[i], "sha256"));
                Exact(published[i], "feedIdentity", Text(Array(root, "packages")[i], "feedIdentity"));
            }
            trusted = signatureVerifier?.VerifyCurrent(publicationSnapshot, snapshots, trustPolicy, evaluationUtc) == true;
        }
        var consumerLock = new JsonObject {
            ["$schemaId"] = "https://schemas.cp6.dev/release/capabilities/v1/consumer-lock",
            ["matrixSha256"] = hash, ["sourceGitSha"] = approval.SourceGitSha, ["repository"] = Repository,
            ["requestedRowSha256"] = requestedHash, ["compatibilityDecision"] = decision.ToString(),
            ["packages"] = JsonNode.Parse(root.GetProperty("packages").GetRawText()),
            ["evidenceSha256"] = JsonNode.Parse(root.GetProperty("evidenceSha256").GetRawText()),
            ["publicationSha256"] = publicationHash, ["trustPolicySha256"] = formal ? approval.CurrentTrustPolicySha256 : null
        };
        return new(Cp6CapabilityState.Verified, Cp6CapabilityState.Verified, Cp6CapabilityState.Verified,
            trusted ? Cp6CapabilityState.Verified : Cp6CapabilityState.Unknown, decision,
            formal ? "FORMAL_PUBLICATION_RECORD_VALIDATED" : "NOT_PUBLISHED", trusted ? "CURRENTLY_ACCEPTED" : "NOT_FORMALLY_TRUSTED",
            Cp6DeterministicJson.Canonicalize(System.Text.Encoding.UTF8.GetBytes(consumerLock.ToJsonString())));
    }

    private static void ValidateArchive(ReadOnlyMemory<byte> archive, JsonElement expected)
    {
        try
        {
            using var stream = new MemoryStream(archive.ToArray(), false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != zip.Entries.Count)
                throw Error("package-archive", "Duplicate archive entries are forbidden.");
            var nuspecs = zip.Entries.Where(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (nuspecs.Length != 1 || nuspecs[0].Length > Cp6DeterministicJson.MaximumBytes)
                throw Error("package-archive", "Exactly one bounded nuspec is required.");
            using var input = nuspecs[0].Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Cp6DeterministicJson.MaximumBytes });
            var xml = XDocument.Load(reader);
            var metadata = xml.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "metadata");
            string Value(string name) => metadata?.Elements().SingleOrDefault(e => e.Name.LocalName == name)?.Value ?? "";
            var repository = metadata?.Elements().SingleOrDefault(e => e.Name.LocalName == "repository");
            if (Value("id") != Text(expected, "packageId") || Value("version") != Text(expected, "version") ||
                (string?)repository?.Attribute("url") != Repository || (string?)repository?.Attribute("commit") != Text(expected, "sourceGitSha") ||
                (string?)repository?.Attribute("type") != "git") throw Error("package-identity", "Nuspec does not match approved immutable identity.");
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or InvalidOperationException)
        { throw new Cp6ReleaseContractException("package-archive", "Invalid package archive or nuspec.", ex); }
    }

    private static ReadOnlyDictionary<string, ReadOnlyMemory<byte>> Snapshot(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> source) =>
        new(source.ToDictionary(p => p.Key, p => (ReadOnlyMemory<byte>)p.Value.ToArray(), StringComparer.Ordinal));
    private static byte[] RequireCanonical(ReadOnlySpan<byte> bytes)
    {
        var canonical = Cp6DeterministicJson.Canonicalize(bytes);
        if (!bytes.SequenceEqual(canonical)) throw Error("non-canonical-json", "Input bytes must already be deterministic JSON; they are never rewritten.");
        return canonical;
    }
    private static string Text(JsonElement root, string name) => Cp6ReleaseJsonRules.RequireString(root, name, "property-value");
    private static JsonElement Array(JsonElement root, string name) => Cp6ReleaseJsonRules.RequireProperty(root, name, JsonValueKind.Array);
    private static void Exact(JsonElement root, string name, string expected)
    { if (Text(root, name) != expected) throw Error("identity-mismatch", $"Property '{name}' differs from the required identity."); }
    private static string[] StringSet(JsonElement root, string name, bool nonEmpty)
    {
        var values = Array(root, name).EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
            ? e.GetString()! : throw Error("property-kind", "Set elements must be non-empty strings.")).ToArray();
        if (nonEmpty && values.Length == 0) throw Error("empty-set", "A nonempty set is required.");
        Cp6ReleaseJsonRules.RequireOrdinalSet(values, "ordinal-set"); return values;
    }
    private static Cp6ReleaseContractException Error(string code, string message) => new(code, message);
    private static void GitSha(string value, string code)
    { if (value.Length != 40) throw Error(code, "A full lowercase 40-character Git SHA is required."); Cp6ReleaseJsonRules.RequireGitSha(value, code); }
    private static void Sha256(string value, string code)
    { if (value.Length != 64) throw Error(code, "A lowercase 64-character SHA-256 is required."); Cp6ReleaseJsonRules.RequireSha256(value, code); }
}

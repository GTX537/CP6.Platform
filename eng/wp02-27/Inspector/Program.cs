using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CP6.Platform.Release;
using CP6.Platform.Release.CapabilitiesV1;

return Inspector.Run(args);

internal static class Inspector
{
    internal static readonly string[] Ids = ["CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts",
        "CP6.Platform.Deployment", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging", "CP6.Platform.Release"];
    internal static int Run(string[] args)
    {
        try
        {
            switch (args)
            {
                case ["formal", var repo, var inventory, var inventoryHash, var output, var dotnet, var tool, var utc]:
                    return Formal(Path.GetFullPath(repo), inventory, inventoryHash, output, dotnet, tool, ParseUtc(utc));
                case ["local", var repo, var packages, var source, var version, var output]:
                    return Local(Path.GetFullPath(repo), packages, source, version, output);
                case ["fixtures", var repo, var output, var utc]:
                    Fixtures(Path.GetFullPath(repo), output, ParseUtc(utc), null); return 0;
                case ["fixtures", var repo, var output, var utc, var package]:
                    Fixtures(Path.GetFullPath(repo), output, ParseUtc(utc), package); return 0;
                default:
                    Console.Error.WriteLine("formal REPO INVENTORY EXPECTED_INVENTORY_SHA256 OUTPUT DOTNET RELEASE_TOOL_DLL UTC | local REPO PACKAGE_DIR SOURCE VERSION OUTPUT | fixtures REPO OUTPUT UTC [ORIGINAL_NUPKG]");
                    return 64;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex is Cp6ReleaseContractException c ? $"{c.Code}: {c.Message}" : ex.ToString());
            return 2;
        }
    }

    private static int Formal(string repo, string inventoryPath, string inventoryHash, string output,
        string dotnet, string tool, DateTimeOffset utc)
    {
        var inventoryBytes = File.ReadAllBytes(inventoryPath);
        Require(Hash(inventoryBytes) == inventoryHash, "Externally pinned inventory hash differs.");
        var inventory = JsonNode.Parse(inventoryBytes)!.AsObject();
        var publication = File.ReadAllBytes(Path.Combine(repo, Text(inventory, "formal_publication_path")));
        Require(Hash(publication) == Text(inventory, "formal_publication_sha256"), "Original publication differs from approved inventory.");
        var source = Text(inventory, "formal_source_git_sha");
        var policyPath = Path.Combine(repo, "eng/p10/trust/p10-formal-nuget-trust-store.v1.json");
        var policyBytes = File.ReadAllBytes(policyPath);
        var anchor = inventory["source_anchors"]!.AsArray().Single(n => Text(n!, "path") == "eng/p10/trust/p10-formal-nuget-trust-store.v1.json")!;
        Require(Hash(policyBytes) == Text(anchor, "committed_bytes_sha256"), "Pinned policy differs from the approved inventory.");
        var certificates = ReadCertificates(Path.Combine(repo, "eng/p10/trust/certificates"));
        var policy = Cp6PinnedNuGetTrustPolicy.Parse(policyBytes, certificates);
        var packages = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var entries = new JsonArray();
        foreach (var p in inventory["cached_packages"]!.AsArray())
        {
            var id = Text(p!, "package_id"); var bytes = File.ReadAllBytes(Text(p!, "path"));
            Require(Hash(bytes) == Text(p!, "sha256"), $"Approved {id} package hash differs.");
            Require(Text(p!, "source_git_sha") == source && Text(p!["nuspec"]!, "version") == "0.10.2", "Inventory mixes source/version.");
            packages.Add(id, bytes);
            entries.Add(Package(id, "0.10.2", source, Hash(bytes), Text(p!, "feed_identity")));
        }
        NewOutput(output);
        var evidence = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal)
        { [inventoryHash] = inventoryBytes, [Hash(publication)] = publication };
        foreach (var item in evidence) Save(output, "evidence/" + item.Key + ".bin", item.Value.ToArray());
        var matrix = Matrix(source, "0.10.2", "FormalPublished", entries, evidence.Keys, new JsonArray());
        var approval = new Cp6CapabilityApproval(Hash(Canonical(matrix)), source, policy.ValidatedDocument.Sha256);
        Write(output, "approval-provenance.json", new JsonObject {
            ["authority"] = "Caller-supplied expected inventory SHA256; original publication/policy/package hashes verified against it",
            ["expectedInventorySha256"] = inventoryHash, ["manifestSha256"] = approval.ManifestSha256,
            ["sourceGitSha"] = source, ["currentTrustPolicySha256"] = approval.CurrentTrustPolicySha256,
            ["evaluationUtc"] = Utc(utc), ["toolDllSha256"] = Hash(File.ReadAllBytes(tool)) });
        Save(output, "matrix.json", Canonical(matrix));
        var verifier = new CliVerifier(output, dotnet, tool, certificates);
        try
        {
            var result = Cp6CapabilityValidator.Evaluate(Canonical(matrix), approval, packages, evidence,
                Encoding.UTF8.GetBytes("{}"), utc, publication, policy, verifier);
            SaveResult(output, result);
            return result.TrustCurrentlyAccepted == Cp6CapabilityState.Verified ? 0 : 3;
        }
        catch (Exception ex)
        {
            Write(output, "failure.json", new JsonObject { ["trustCurrentlyAccepted"] = "Unknown",
                ["errorCode"] = ex is Cp6ReleaseContractException c ? c.Code : ex.GetType().Name,
                ["error"] = ex.Message, ["evaluationUtc"] = Utc(utc), ["matrixSha256"] = approval.ManifestSha256,
                ["scope"] = "No accepted consumer lock was issued; preserved matrix, approval provenance and any completed CLI calls remain diagnostic evidence" });
            throw;
        }
    }

    private static int Local(string repo, string packageDirectory, string source, string version, string output)
    {
        var files = Directory.GetFiles(packageDirectory, "*.nupkg", SearchOption.TopDirectoryOnly);
        Require(files.Length == 7, "Local package directory must contain exactly seven nupkg files.");
        var packages = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes, false); using var zip = new ZipArchive(stream);
            var nuspec = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var data = nuspec.Open(); var xml = XDocument.Load(data);
            var metadata = xml.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
            var id = metadata.Elements().Single(e => e.Name.LocalName == "id").Value;
            packages.Add(id, bytes); paths.Add(id, Path.GetFullPath(path));
        }
        Require(packages.Keys.Order(StringComparer.Ordinal).SequenceEqual(Ids), "Local set has missing/unknown packages.");
        NewOutput(output);
        var assets = new JsonArray(); var evidence = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var name in new[] { "capability-matrix.v1.schema.json", "consumer-lock.v1.schema.json" })
        {
            var path = "contracts/release/capabilities/v1/" + name;
            var sourceBytes = File.ReadAllBytes(Path.Combine(repo, path));
            var packaged = Entry(packages["CP6.Platform.Release"], path);
            Require(sourceBytes.AsSpan().SequenceEqual(packaged), $"Packaged {name} differs from source bytes.");
            evidence[Hash(sourceBytes)] = sourceBytes;
            assets.Add(new JsonObject { ["packageId"] = "CP6.Platform.Release", ["entry"] = path,
                ["sourceSha256"] = Hash(sourceBytes), ["packagedSha256"] = Hash(packaged), ["byteEqual"] = true });
        }
        var api = new JsonArray();
        InspectApi(packages, api, "CP6.Platform.Messaging", "CP6.Platform.Messaging.NativeContracts.Cp6NativeContractCatalog", ["LoadS01", "ResolveOpenApi", "ResolveRegistry"]);
        InspectApi(packages, api, "CP6.Platform.Messaging", "CP6.Platform.Messaging.NativeContracts.Cp6OriginalJson", ["Capture", "GetBytes", "Matches"]);
        InspectApi(packages, api, "CP6.Platform.Contracts", "CP6.Platform.Contracts.Context.ContextResolutionEvidenceV1", ["get_ContextContractVersion", "get_CapabilityVersion"]);
        InspectApi(packages, api, "CP6.Platform.Abstractions", "CP6.Platform.Abstractions.Context.TrustedContextExecutorV1", ["ExecuteAsync"]);
        var inspection = new JsonObject { ["sourceGitSha"] = source, ["version"] = version, ["packageAssets"] = assets, ["actualArchiveApis"] = api,
            ["scope"] = "Actual archive metadata and schema-byte inspection only; no DLL execution, source assembly resolution or ProjectReference fallback",
            ["nativeProfileSchemaPackaging"] = "UNKNOWN: native S01-09 profile schemas are not packaged by the current Contracts project; new Release schemas are not substitutes",
            ["consumerQualification"] = "UNKNOWN", ["databaseHostConfiguration"] = "NOT_RUN" };
        var inspectionBytes = Canonical(inspection); evidence[Hash(inspectionBytes)] = inspectionBytes;
        foreach (var item in evidence) Save(output, "evidence/" + item.Key + ".bin", item.Value.ToArray());
        Save(output, "archive-inspection.json", inspectionBytes);
        var entries = new JsonArray();
        foreach (var id in Ids) entries.Add(Package(id, version, source, Hash(packages[id].Span), $"task-local:wp02-27#{id}/{version}"));
        var capabilities = JsonNode.Parse("""
            [{"id":"S01-09","version":"1.0.0","profiles":["crm-inq-r3-v15","erp-credit-current-v1","erp-credit-frozen-r8"],"scope":["native-contract-routing","original-json-bytes"]},{"id":"ctx-binding-v1","version":"1.0.0","profiles":["ctx-v1"],"scope":["Background","Http","Message"]}]
            """)!.AsArray();
        var matrix = Matrix(source, version, "TaskLocal", entries, evidence.Keys, capabilities);
        var bytesMatrix = Canonical(matrix);
        var approval = new Cp6CapabilityApproval(Hash(bytesMatrix), source);
        Write(output, "approval-provenance.json", new JsonObject { ["authority"] = "Explicit caller source/version identity and actual archive inspection; no measured consumer rows approved",
            ["manifestSha256"] = approval.ManifestSha256, ["sourceGitSha"] = source, ["version"] = version });
        Save(output, "matrix.json", bytesMatrix);
        SaveResult(output, Cp6CapabilityValidator.Evaluate(bytesMatrix, approval, packages, evidence, Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow));
        var originals = new JsonObject(); foreach (var p in paths) originals[p.Key] = p.Value;
        Write(output, "archive-paths.json", originals);
        return 0;
    }

    private static void InspectApi(Dictionary<string, ReadOnlyMemory<byte>> packages, JsonArray report,
        string packageId, string fullType, string[] methods)
    {
        var bytes = Entry(packages[packageId], $"lib/net8.0/{packageId}.dll");
        using var stream = new MemoryStream(bytes, false); using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var match = reader.TypeDefinitions.Select(h => reader.GetTypeDefinition(h)).Single(t =>
            reader.GetString(t.Namespace) + "." + reader.GetString(t.Name) == fullType);
        Require((match.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public, $"{fullType} is not public.");
        var publicMethods = match.GetMethods().Select(h => reader.GetMethodDefinition(h))
            .Where(m => (m.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public)
            .Select(m => reader.GetString(m.Name)).ToHashSet(StringComparer.Ordinal);
        Require(methods.All(publicMethods.Contains), $"{fullType} lacks required public method names.");
        report.Add(new JsonObject { ["packageId"] = packageId, ["assemblyEntry"] = $"lib/net8.0/{packageId}.dll",
            ["assemblySha256"] = Hash(bytes), ["publicType"] = fullType, ["publicMethods"] = new JsonArray(methods.Select(m => JsonValue.Create(m)).ToArray()),
            ["scope"] = "Metadata public type/method presence; signatures and runtime behavior not qualified" });
    }

    private static void Fixtures(string repo, string output, DateTimeOffset utc, string? package)
    {
        NewOutput(output);
        var original = File.ReadAllBytes(Path.Combine(repo, "eng/p10/trust/p10-formal-nuget-trust-store.v1.json"));
        var originalRoot = JsonNode.Parse(original)!.AsObject();
        var oldSigner = originalRoot["signers"]![0]!.DeepClone();
        var certs = ReadCertificates(Path.Combine(repo, "eng/p10/trust/certificates"));
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=CP6 Platform Release Signing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.3") }, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var certificate = request.CreateSelfSigned(utc.AddDays(-1), utc.AddYears(1));
        var der = certificate.Export(X509ContentType.Cert); var identity = Cp6NuGetCertificateProfile.Validate(der);
        var newSigner = new JsonObject { ["certificatePath"] = $"certificates/{identity.CertificateSha256}.cer",
            ["certificateSha256"] = identity.CertificateSha256, ["spkiKeyId"] = identity.SpkiKeyId,
            ["subject"] = identity.Subject, ["issuer"] = identity.Issuer,
            ["validFromUtc"] = Utc(identity.ValidFromUtc), ["validUntilUtc"] = Utc(identity.ValidUntilUtc),
            ["activatedAtUtc"] = Utc(identity.ValidFromUtc.AddMinutes(5)), ["status"] = "Current", ["revokedAtUtc"] = null, ["revocationReason"] = null };
        var manifest = new JsonArray();
        foreach (var state in new[] { "Current", "Historical", "Revoked", "UnknownSigner" })
        {
            var dir = Path.Combine(output, state); Directory.CreateDirectory(Path.Combine(dir, "certificates"));
            var policy = originalRoot.DeepClone().AsObject(); var selected = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            if (state == "Current") foreach (var c in certs) selected.Add(c.Key, c.Value);
            else
            {
                policy["policyVersion"] = originalRoot["policyVersion"]!.GetValue<int>() + 1;
                var signers = new JsonArray(newSigner.DeepClone()); selected.Add(Text(newSigner, "certificatePath"), der);
                if (state != "UnknownSigner")
                {
                    var old = oldSigner.DeepClone(); old["status"] = state;
                    if (state == "Revoked") { old["revokedAtUtc"] = Utc(utc.AddHours(-1)); old["revocationReason"] = "WP02-27 synthetic negative fixture; original policy unchanged"; }
                    signers.Add(old); foreach (var c in certs) selected.Add(c.Key, c.Value);
                }
                policy["signers"] = signers;
            }
            var bytes = Canonical(policy); _ = Cp6PinnedNuGetTrustPolicy.Parse(bytes, selected);
            Save(dir, "policy.json", bytes);
            foreach (var c in selected) Save(dir, c.Key, c.Value.ToArray());
            manifest.Add(new JsonObject { ["case"] = state, ["policyPath"] = Path.GetFullPath(Path.Combine(dir, "policy.json")),
                ["certificateDirectory"] = Path.GetFullPath(Path.Combine(dir, "certificates")), ["policySha256"] = Hash(bytes),
                ["scope"] = "Synthetic policy fixture; public DER only; ephemeral private key disposed without export" });
        }
        if (package is not null)
        {
            var originalPackage = File.ReadAllBytes(package);
            foreach (var mutation in new[] { "missing-timestamp", "bad-timestamp" })
            {
                using var stream = new MemoryStream(); stream.Write(originalPackage);
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
                {
                    var signature = zip.GetEntry(".signature.p7s") ?? throw new InvalidDataException("Missing signature entry.");
                    using var input = new MemoryStream(); using (var data = signature.Open()) data.CopyTo(input);
                    var cms = new SignedCms(); cms.Decode(input.ToArray()); var signer = cms.SignerInfos[0];
                    var values = signer.UnsignedAttributes.Cast<CryptographicAttributeObject>()
                        .Where(a => a.Oid.Value == "1.2.840.113549.1.9.16.2.14").SelectMany(a => a.Values.Cast<AsnEncodedData>()).ToArray();
                    Require(values.Length > 0, "Original package must contain timestamp unsigned attribute.");
                    foreach (var value in values) signer.RemoveUnsignedAttribute(value);
                    if (mutation == "bad-timestamp") signer.AddUnsignedAttribute(new AsnEncodedData(new Oid("1.2.840.113549.1.9.16.2.14"), new byte[] { 5, 0 }));
                    signature.Delete(); using var target = zip.CreateEntry(".signature.p7s", CompressionLevel.NoCompression).Open(); target.Write(cms.Encode());
                }
                Save(output, mutation + ".nupkg", stream.ToArray());
            }
        }
        Write(output, "fixtures.json", new JsonObject { ["originalPolicySha256"] = Hash(original), ["evaluationUtc"] = Utc(utc), ["policies"] = manifest });
    }

    internal static Dictionary<string, ReadOnlyMemory<byte>> ReadCertificates(string directory) => Directory.GetFiles(directory, "*.cer")
        .ToDictionary(p => "certificates/" + Path.GetFileName(p), p => (ReadOnlyMemory<byte>)File.ReadAllBytes(p), StringComparer.Ordinal);
    internal static string Hash(ReadOnlySpan<byte> bytes) => Cp6DeterministicJson.Sha256Hex(bytes);
    internal static byte[] Canonical(JsonNode node) => Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(node.ToJsonString()));
    internal static string Text(JsonNode node, string name) => node[name]!.GetValue<string>();
    internal static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseUtc(string value) => DateTimeOffset.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    internal static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void NewOutput(string path) { Require(!Directory.Exists(path), "Evidence output directory already exists; choose a unique attempt."); Directory.CreateDirectory(path); }
    internal static void Save(string root, string name, byte[] bytes)
    { var path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var file = new FileStream(path, FileMode.CreateNew); file.Write(bytes); }
    internal static void Write(string root, string name, JsonNode node) => Save(root, name, Canonical(node));
    private static JsonObject Package(string id, string version, string source, string hash, string feed) => new()
    { ["packageId"] = id, ["version"] = version, ["sourceGitSha"] = source, ["sha256"] = hash, ["feedIdentity"] = feed };
    private static JsonObject Matrix(string source, string version, string lane, JsonArray packages, IEnumerable<string> hashes, JsonArray capabilities) => new()
    { ["$schemaId"] = Cp6CapabilityValidator.SchemaId, ["repository"] = Cp6CapabilityValidator.Repository, ["sourceGitSha"] = source,
        ["lane"] = lane, ["version"] = version, ["packages"] = packages, ["evidenceSha256"] = new JsonArray(hashes.Order(StringComparer.Ordinal).Select(h => JsonValue.Create(h)).ToArray()),
        ["capabilities"] = capabilities, ["compatibility"] = new JsonArray() };
    private static byte[] Entry(ReadOnlyMemory<byte> archive, string path)
    { using var stream = new MemoryStream(archive.ToArray(), false); using var zip = new ZipArchive(stream); using var input = zip.GetEntry(path)?.Open() ?? throw new InvalidDataException($"Missing {path}"); using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray(); }
    private static void SaveResult(string output, Cp6CapabilityEvaluation result)
    {
        Save(output, "consumer-lock.json", result.ConsumerLock);
        Write(output, "state.json", new JsonObject { ["sourceFrozen"] = result.SourceFrozen.ToString(), ["contractShapeValid"] = result.ContractShapeValid.ToString(),
            ["artifactBytesVerified"] = result.ArtifactBytesVerified.ToString(), ["trustCurrentlyAccepted"] = result.TrustCurrentlyAccepted.ToString(),
            ["compatibility"] = result.Compatibility.ToString(), ["publicationStatus"] = result.PublicationStatus, ["trustStatus"] = result.TrustStatus,
            ["consumerQualified"] = result.ConsumerQualified.ToString(), ["hostRegistered"] = result.HostRegistered.ToString(), ["configEnabled"] = result.ConfigEnabled.ToString(),
            ["runtimeObserved"] = result.RuntimeObserved.ToString(), ["businessApplied"] = result.BusinessApplied.ToString(), ["independentlyAccepted"] = result.IndependentlyAccepted.ToString() });
    }
}

internal sealed class CliVerifier(string output, string dotnet, string tool,
    Dictionary<string, ReadOnlyMemory<byte>> certificates) : ICp6CapabilitySignatureVerifier
{
    public bool VerifyCurrent(ReadOnlyMemory<byte> publication, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> packages,
        Cp6PinnedNuGetTrustPolicy policy, DateTimeOffset evaluationUtc)
    {
        var root = Path.Combine(output, "current-verification"); Directory.CreateDirectory(root);
        Inspector.Save(root, "publication.json", publication.ToArray());
        Inspector.Save(root, "policy.json", policy.ValidatedDocument.CanonicalUtf8);
        foreach (var c in certificates) Inspector.Save(root, c.Key, c.Value.ToArray());
        using var pub = JsonDocument.Parse(publication);
        var source = pub.RootElement.GetProperty("sourceGitSha").GetString()!;
        var version = pub.RootElement.GetProperty("version").GetString()!;
        var accepted = true;
        foreach (var id in Inspector.Ids)
        {
            Inspector.Save(root, id + ".nupkg", packages[id].ToArray());
            var arguments = new[] { Path.GetFullPath(tool), "verify-formal-package", Path.GetFullPath(Path.Combine(root, id + ".nupkg")),
                Path.GetFullPath(Path.Combine(root, "policy.json")), Path.GetFullPath(Path.Combine(root, "certificates")), id, version, source, Inspector.Utc(evaluationUtc), "Current" };
            var start = new ProcessStartInfo(dotnet) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            var began = DateTimeOffset.UtcNow;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start existing ReleaseTool.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(); var stderrTask = process.StandardError.ReadToEndAsync();
            var exited = process.WaitForExit(180000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            var stdout = stdoutTask.GetAwaiter().GetResult(); var stderr = stderrTask.GetAwaiter().GetResult();
            Inspector.Save(root, id + ".stdout.txt", Encoding.UTF8.GetBytes(stdout)); Inspector.Save(root, id + ".stderr.txt", Encoding.UTF8.GetBytes(stderr));
            var valid = exited && process.ExitCode == 0;
            try
            {
                using var result = JsonDocument.Parse(stdout); var r = result.RootElement;
                valid &= r.GetProperty("packageId").GetString() == id && r.GetProperty("version").GetString() == version &&
                    r.GetProperty("sourceGitSha").GetString() == source && r.GetProperty("packageSha256").GetString() == Inspector.Hash(packages[id].Span) &&
                    r.GetProperty("signerFingerprint").GetString() == policy.CurrentSigner.CertificateSha256 && r.GetProperty("spkiKeyId").GetString() == policy.CurrentSigner.SpkiKeyId;
                var publishedPackage = pub.RootElement.GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("packageId").GetString() == id);
                valid &= r.GetProperty("timestampPolicyOid").GetString() == publishedPackage.GetProperty("timestampPolicyOid").GetString() &&
                    r.GetProperty("timestampCertificateChainSha256").EnumerateArray().Select(v => v.GetString())
                        .SequenceEqual(publishedPackage.GetProperty("timestampCertificateChainSha256").EnumerateArray().Select(v => v.GetString()));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { valid = false; }
            accepted &= valid;
            Inspector.Write(root, id + ".call.json", new JsonObject { ["arguments"] = new JsonArray(arguments.Select(a => JsonValue.Create(a)).ToArray()),
                ["startedUtc"] = Inspector.Utc(began), ["finishedUtc"] = Inspector.Utc(DateTimeOffset.UtcNow), ["evaluationUtc"] = Inspector.Utc(evaluationUtc),
                ["exitCode"] = process.ExitCode, ["timedOut"] = !exited, ["identityCheckedSuccess"] = valid, ["policySha256"] = policy.ValidatedDocument.Sha256,
                ["archiveSha256"] = Inspector.Hash(packages[id].Span), ["originalPublicationSha256"] = Inspector.Hash(publication.Span) });
        }
        return accepted;
    }
}

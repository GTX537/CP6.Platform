namespace CP6.Platform.Release;

public sealed record Cp6ValidatedReleaseDocument(
    string SchemaId,
    string? CandidateKind,
    string? SubjectKind,
    bool? Deployable,
    string Sha256,
    IReadOnlyList<string> RepositoryNames,
    IReadOnlyList<string> PackageIds,
    IReadOnlyList<string> SubjectHashes,
    byte[] CanonicalUtf8);

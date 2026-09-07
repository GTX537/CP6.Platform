namespace CP6.Platform.ReleaseTests;

// Repository-local pack writes shared bin/obj files; isolate it from all other test collections.
[CollectionDefinition(nameof(PackageBuildCollection), DisableParallelization = true)]
public sealed class PackageBuildCollection;

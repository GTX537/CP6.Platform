namespace CP6.Platform.ReleaseTests;

public sealed class PackageBuildIsolationTests
{
    [Theory]
    [InlineData(typeof(P10PackageTests))]
    [InlineData(typeof(FormalPackageVerifierTests))]
    public void Shared_repository_pack_tests_use_one_nonparallel_collection(Type testClass)
    {
        const string collectionName = "PackageBuildCollection";
        var collection = Assert.Single(
            testClass.GetCustomAttributesData(),
            attribute => attribute.AttributeType == typeof(CollectionAttribute));
        Assert.Equal(collectionName, collection.ConstructorArguments[0].Value);

        var definition = Assert.Single(
            testClass.Assembly.GetTypes().SelectMany(type => type.GetCustomAttributesData()),
            attribute => attribute.AttributeType == typeof(CollectionDefinitionAttribute)
                && Equals(attribute.ConstructorArguments[0].Value, collectionName));
        var parallelization = Assert.Single(
            definition.NamedArguments,
            argument => argument.MemberName == nameof(CollectionDefinitionAttribute.DisableParallelization));
        Assert.Equal(true, parallelization.TypedValue.Value);
    }
}

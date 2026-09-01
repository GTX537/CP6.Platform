namespace CP6.Platform.ReleaseTests;

internal static class ReleaseTestData
{
    private static readonly Lazy<string> Root = new(FindRepositoryRoot);

    public static byte[] Fixture(string group, string name) =>
        File.ReadAllBytes(Path.Combine(Root.Value, "contracts", "release", "v1", "fixtures", group, name));

    public static string RepositoryRoot => Root.Value;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CP6.Platform.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate CP6.Platform.sln from the test output directory.");
    }
}

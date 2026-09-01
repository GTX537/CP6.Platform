using System.Text.Json;
using CP6.Platform.Deployment;

if (args is ["--profile", var profilePath])
{
    try
    {
        var profile = Cp6P09RuntimeProfile.Parse(await File.ReadAllBytesAsync(profilePath));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "Valid",
            profileId = profile.ProfileId,
            profileSha256 = profile.Sha256
        }));
        return 0;
    }
    catch (Cp6P09ContractException exception)
    {
        WriteInvalid(exception);
        return 65;
    }
}

if (args is ["--evidence", var evidencePath])
{
    try
    {
        var evidence = Cp6P09RehearsalEvidence.Parse(await File.ReadAllBytesAsync(evidencePath));
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "Valid",
            evidenceSha256 = evidence.Sha256
        }));
        return 0;
    }
    catch (Cp6P09ContractException exception)
    {
        WriteInvalid(exception);
        return 65;
    }
}

if (args is ["--kubernetes", var kubernetesProfilePath, _, ..])
{
    try
    {
        var profile = Cp6P09RuntimeProfile.Parse(await File.ReadAllBytesAsync(kubernetesProfilePath));
        var resources = new List<string>();
        foreach (var resourcePath in args.Skip(2))
        {
            resources.Add(await File.ReadAllTextAsync(resourcePath));
        }

        var validation = Cp6P09KubernetesValidator.Validate(profile, resources);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "Valid",
            profileId = profile.ProfileId,
            profileSha256 = profile.Sha256,
            kubernetesManifestSha256 = validation.Sha256
        }));
        return 0;
    }
    catch (Cp6P09ContractException exception)
    {
        WriteInvalid(exception);
        return 65;
    }
}

if (args is ["--strict-json", _, ..])
{
    try
    {
        foreach (var jsonPath in args.Skip(1))
        {
            _ = Cp6P09Json.Canonicalize(await File.ReadAllBytesAsync(jsonPath));
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "Valid",
            jsonFileCount = args.Length - 1
        }));
        return 0;
    }
    catch (Cp6P09ContractException exception)
    {
        WriteInvalid(exception);
        return 65;
    }
}

return 64;

static void WriteInvalid(Cp6P09ContractException exception) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        status = "Invalid",
        checkId = exception.CheckId
    }));

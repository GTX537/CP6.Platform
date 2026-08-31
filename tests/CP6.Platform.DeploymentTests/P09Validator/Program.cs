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

return 64;

static void WriteInvalid(Cp6P09ContractException exception) =>
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        status = "Invalid",
        checkId = exception.CheckId
    }));

using System.Globalization;
using CP6.Platform.Release;

return Run(args);

static int Run(string[] arguments)
{
    try
    {
        switch (arguments)
        {
            case ["canonicalize", var input, var output]:
                File.WriteAllBytes(output, Cp6DeterministicJson.Canonicalize(File.ReadAllBytes(input)));
                return 0;
            case ["validate-build-provenance", var input]:
                Cp6SupportingContractValidator.ValidateBuildInvocationProvenance(File.ReadAllBytes(input));
                return 0;
            case ["validate-evidence", var input]:
                Cp6SupportingContractValidator.ValidateEvidenceRecord(File.ReadAllBytes(input));
                return 0;
            case ["validate-transport", var input, var evaluationText]
                when TryParseUtcRoundTrip(evaluationText, out var evaluationUtc):
                Cp6SupportingContractValidator.ValidateTestPackageTransport(File.ReadAllBytes(input), evaluationUtc);
                return 0;
            case ["validate-transport", _, _]:
                return 64;
            default:
                return 64;
        }
    }
    catch (Cp6ReleaseContractException exception)
    {
        Console.Error.WriteLine($"{exception.Code}: {exception.Message}");
        return 2;
    }
    catch
    {
        Console.Error.WriteLine("release-tool-internal-error");
        return 1;
    }
}

static bool TryParseUtcRoundTrip(string value, out DateTimeOffset result)
{
    result = default;
    return value.EndsWith('Z') &&
        DateTimeOffset.TryParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result) &&
        result.Offset == TimeSpan.Zero;
}

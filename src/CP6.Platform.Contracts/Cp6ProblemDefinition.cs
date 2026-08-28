using System.Text.RegularExpressions;

namespace CP6.Platform.Contracts;

/// <summary>
/// Defines the stable, non-sensitive machine contract for a CP6 RFC 9457 response.
/// </summary>
public sealed partial record Cp6ProblemDefinition
{
    public Cp6ProblemDefinition(string type, string title, int status, string code, string messageKey)
    {
        if (!Uri.TryCreate(type, UriKind.Absolute, out var typeUri) || typeUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Problem type must be an absolute HTTPS URI.", nameof(type));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (title.Length > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(title), "Problem title cannot exceed 200 characters.");
        }

        if (status is < 400 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Problem status must be an HTTP error status.");
        }

        if (!CodePattern().IsMatch(code))
        {
            throw new ArgumentException("Problem code does not match the CP6 machine-code profile.", nameof(code));
        }

        if (!MessageKeyPattern().IsMatch(messageKey))
        {
            throw new ArgumentException("Problem message key does not match the CP6 localization profile.", nameof(messageKey));
        }

        Type = typeUri.AbsoluteUri;
        Title = title;
        Status = status;
        Code = code;
        MessageKey = messageKey;
    }

    public string Type { get; }

    public string Title { get; }

    public int Status { get; }

    public string Code { get; }

    public string MessageKey { get; }

    [GeneratedRegex("^[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z][a-z0-9]*\\.error\\.[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageKeyPattern();
}

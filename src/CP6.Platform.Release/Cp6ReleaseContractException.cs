namespace CP6.Platform.Release;

public sealed class Cp6ReleaseContractException : Exception
{
    public Cp6ReleaseContractException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}

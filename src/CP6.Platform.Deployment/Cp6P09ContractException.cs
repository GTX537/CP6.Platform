namespace CP6.Platform.Deployment;

public sealed class Cp6P09ContractException : Exception
{
    public Cp6P09ContractException(string checkId, string message)
        : base(message)
    {
        CheckId = checkId;
    }

    internal Cp6P09ContractException(string checkId, string message, Exception innerException)
        : base(message, innerException)
    {
        CheckId = checkId;
    }

    public string CheckId { get; }
}

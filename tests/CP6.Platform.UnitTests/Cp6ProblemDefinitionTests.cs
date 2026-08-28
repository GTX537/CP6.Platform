using CP6.Platform.Contracts;

namespace CP6.Platform.UnitTests;

public sealed class Cp6ProblemDefinitionTests
{
    [Fact]
    public void ValidDefinition_PreservesStableMachineFields()
    {
        var definition = new Cp6ProblemDefinition(
            "https://errors.cp6.uk/crm/concurrency-conflict",
            "The record changed",
            412,
            "CRM_CONCURRENCY_CONFLICT",
            "cp6.error.concurrencyConflict");

        Assert.Equal(412, definition.Status);
        Assert.Equal("CRM_CONCURRENCY_CONFLICT", definition.Code);
        Assert.Equal("cp6.error.concurrencyConflict", definition.MessageKey);
    }

    [Theory]
    [InlineData("http://errors.cp6.uk/problem", "TITLE", 400, "CP6_CODE", "cp6.error.code")]
    [InlineData("https://errors.cp6.uk/problem", "TITLE", 200, "CP6_CODE", "cp6.error.code")]
    [InlineData("https://errors.cp6.uk/problem", "TITLE", 400, "lower_code", "cp6.error.code")]
    [InlineData("https://errors.cp6.uk/problem", "TITLE", 400, "CP6_CODE", "formatted message")]
    public void InvalidDefinition_IsRejected(
        string type,
        string title,
        int status,
        string code,
        string messageKey)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Cp6ProblemDefinition(type, title, status, code, messageKey));
    }
}

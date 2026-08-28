using CP6.Platform.Abstractions;
using CP6.Platform.Contracts;

namespace CP6.Platform.UnitTests;

public sealed class RequestContextTests
{
    [Fact]
    public void Interface_IsExactlyTheApprovedReadOnlyContract()
    {
        var properties = typeof(IRequestContext).GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Audience", "CorrelationId", "IsPublic", "Subject", "TenantId", "TokenId", "UserId"],
            properties.Select(property => property.Name));
        Assert.All(properties, property => Assert.False(property.CanWrite));
        Assert.DoesNotContain(typeof(IRequestContext).GetMethods(), method => !method.IsSpecialName);
    }

    [Fact]
    public void Constructor_PreservesTrustedValuesWithoutGeneratingDefaults()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var snapshot = new RequestContextSnapshot(
            tenantId,
            userId,
            "opaque-subject",
            "cp6-crm",
            "correlation-original",
            "token-17",
            true);

        var context = new RequestContext(snapshot);

        Assert.Equal(tenantId, context.TenantId);
        Assert.Equal(userId, context.UserId);
        Assert.Equal("opaque-subject", context.Subject);
        Assert.Equal("cp6-crm", context.Audience);
        Assert.Equal("correlation-original", context.CorrelationId);
        Assert.Equal("token-17", context.TokenId);
        Assert.True(context.IsPublic);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("audience")]
    [InlineData("correlation")]
    public void Constructor_RejectsMissingRequiredIdentity(string missingField)
    {
        var snapshot = ValidSnapshot() with
        {
            Subject = missingField == "subject" ? " " : "subject",
            Audience = missingField == "audience" ? " " : "audience",
            CorrelationId = missingField == "correlation" ? " " : "correlation"
        };

        Assert.Throws<ArgumentException>(() => new RequestContext(snapshot));
    }

    [Fact]
    public void Constructor_RejectsEmptyTenantInsteadOfFallingBack()
    {
        var snapshot = ValidSnapshot() with { TenantId = Guid.Empty };

        Assert.Throws<ArgumentException>(() => new RequestContext(snapshot));
    }

    [Fact]
    public void Constructor_AllowsAbsentUserAndToken()
    {
        var context = new RequestContext(ValidSnapshot() with { UserId = null, TokenId = " " });

        Assert.Null(context.UserId);
        Assert.Null(context.TokenId);
    }

    private static RequestContextSnapshot ValidSnapshot() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "subject", "audience", "correlation", "token", false);
}

using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tests.Integration;

[Collection("Sitefinity")]
[Trait("Category", "Integration")]
public sealed class StatusServiceTests
{
    private readonly SitefinityFixture _fixture;

    public StatusServiceTests(SitefinityFixture fixture)
    {
        this._fixture = fixture;
    }

    [SkippableFact]
    public async Task CheckStatus_ReturnsReady()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var status = await this._fixture.StatusService.CheckStatusAsync();

        Assert.True(status.IsReady, $"Expected IsReady but got: {status.Summary}");
        Assert.False(status.IsBootstrapping);
        Assert.False(status.IsUnreachable);
    }

    [SkippableFact]
    public async Task ValidateApiKey_ReturnsValid()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var result = await this._fixture.ApiKeyValidationService.ValidateAsync();

        Assert.Equal(ApiKeyValidationResult.Valid, result);
    }
}

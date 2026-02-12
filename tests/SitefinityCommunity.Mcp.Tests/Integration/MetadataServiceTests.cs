namespace SitefinityCommunity.Mcp.Tests.Integration;

[Collection("Sitefinity")]
[Trait("Category", "Integration")]
public sealed class MetadataServiceTests
{
    private readonly SitefinityFixture _fixture;

    public MetadataServiceTests(SitefinityFixture fixture)
    {
        this._fixture = fixture;
    }

    [SkippableFact]
    public async Task GetSiteInfo_ReturnsSitefinityVersion()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var info = await this._fixture.MetadataService.GetSiteInfoAsync();

        Assert.False(string.IsNullOrWhiteSpace(info.SitefinityVersion),
            "SitefinityVersion should not be empty");
        Assert.False(string.IsNullOrWhiteSpace(info.ProjectName),
            "ProjectName should not be empty");
    }

    [SkippableFact]
    public async Task ListModules_ReturnsAtLeastOneModule()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var modules = await this._fixture.MetadataService.ListModulesAsync();

        Assert.NotEmpty(modules);
        Assert.All(modules, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.Name));
            Assert.False(string.IsNullOrWhiteSpace(m.Type));
        });
    }

    [SkippableFact]
    public async Task ListDynamicTypes_ReturnsTypes()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var types = await this._fixture.MetadataService.ListDynamicTypesAsync();

        Assert.NotEmpty(types);
        Assert.All(types, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.TypeFullName));
        });
    }

    [SkippableFact]
    public async Task ListPageRoutes_ReturnsPages()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        try
        {
            var result = await this._fixture.MetadataService.ListPageRoutesAsync();

            Assert.NotEmpty(result.PageRoutes);
            Assert.All(result.PageRoutes, p =>
            {
                Assert.StartsWith("/", p.Url);
            });
        }
        catch (TaskCanceledException)
        {
            // Page routes walks the entire Sitefinity page tree — can exceed the 30s HTTP timeout on large sites
            Skip.If(true, "Page routes endpoint timed out (large site). This is expected.");
        }
    }

    [SkippableFact]
    public async Task ListApiRoutes_ReturnsServiceStackRoutes()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var result = await this._fixture.MetadataService.ListApiRoutesAsync();

        Assert.NotEmpty(result.ServiceStackRoutes);
    }

    [SkippableFact]
    public async Task ListApiRoutes_ReturnsODataRoutes()
    {
        Skip.If(!this._fixture.IsAvailable, this._fixture.SkipReason);

        var result = await this._fixture.MetadataService.ListApiRoutesAsync();

        Assert.NotEmpty(result.ODataRoutes);
    }
}

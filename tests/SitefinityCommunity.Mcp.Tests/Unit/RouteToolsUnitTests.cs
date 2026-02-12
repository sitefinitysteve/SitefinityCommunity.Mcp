using NSubstitute;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class RouteToolsUnitTests
{
    private static (RouteTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new RouteTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task ListPageRoutes_FormatsTableCorrectly()
    {
        var (tools, mock) = CreateTools();
        mock.ListPageRoutesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageRoutesResponse
            {
                PageRoutes =
                [
                    new PageRoute
                    {
                        Title = "Home",
                        Url = "/",
                        NodeType = "Standard",
                        IsPublished = true
                    },
                    new PageRoute
                    {
                        Title = "Contact",
                        Url = "/contact",
                        NodeType = "Standard",
                        IsPublished = false
                    }
                ],
                Warnings = []
            });

        var result = await tools.ListPageRoutes();

        Assert.Contains("Page Routes (2)", result);
        Assert.Contains("/", result);
        Assert.Contains("Home", result);
        Assert.Contains("Published", result);
        Assert.Contains("Draft", result);
        Assert.Contains("No issues detected.", result);
    }

    [Fact]
    public async Task ListApiRoutes_FormatsODataSection()
    {
        var (tools, mock) = CreateTools();
        mock.ListApiRoutesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ApiRoutesResponse
            {
                ServiceStackRoutes =
                [
                    new ApiRoute { Path = "/mcp/ping", Verbs = "GET", RequestType = "McpPingRequest" }
                ],
                ODataRoutes =
                [
                    new ODataRoute { EntitySetName = "newsitems", EntitySetUrl = "/api/default/newsitems" }
                ],
                Warnings = []
            });

        var result = await tools.ListApiRoutes();

        Assert.Contains("ServiceStack Routes (1)", result);
        Assert.Contains("/mcp/ping", result);
        Assert.Contains("OData Entity Sets (1)", result);
        Assert.Contains("newsitems", result);
        Assert.Contains("No issues detected.", result);
    }

    [Fact]
    public async Task ListPageRoutes_ShowsWarningsForUrlEvaluation()
    {
        var (tools, mock) = CreateTools();
        mock.ListPageRoutesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageRoutesResponse
            {
                PageRoutes =
                [
                    new PageRoute
                    {
                        Title = "Dynamic Page",
                        Url = "/dynamic",
                        NodeType = "Standard",
                        IsPublished = true,
                        HasUrlEvaluation = true,
                        UrlEvaluationMode = "QueryString"
                    }
                ],
                Warnings = ["Page '/dynamic' uses QueryString URL evaluation"]
            });

        var result = await tools.ListPageRoutes();

        // Verify URL evaluation indicator appears in the page line
        Assert.Contains("URL eval: QueryString", result);

        // Verify warning section
        Assert.Contains("Warnings (1)", result);
        Assert.Contains("QueryString URL evaluation", result);
    }
}

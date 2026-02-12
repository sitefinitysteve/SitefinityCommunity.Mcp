using System.Text.Json;
using NSubstitute;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tests.Helpers;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class MetadataServiceUnitTests
{
    private static SitefinityMetadataService CreateService(string jsonResponse)
    {
        var config = new SitefinityMcpConfig
        {
            DefaultEnvironment = "dev",
            Environments = new Dictionary<string, EnvironmentConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["dev"] = new EnvironmentConfig
                {
                    Url = "https://test.example.com",
                    SitefinityApiKey = "test-key"
                }
            }
        };

        var resolver = new EnvironmentResolver(config);
        var handler = new MockHttpMessageHandler(jsonResponse);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        return new SitefinityMetadataService(resolver, factory);
    }

    [Fact]
    public async Task GetSiteInfo_DeserializesCorrectly()
    {
        var json = JsonSerializer.Serialize(new SiteInfoResponse
        {
            SitefinityVersion = "15.1.8325.0",
            DotNetVersion = "4.8.1",
            ProjectName = "TestProject",
            ModuleCount = 42,
            Languages = ["en", "fr"],
            Sites =
            [
                new SiteEntry { Name = "Default", LiveUrl = "https://test.example.com", IsDefault = true }
            ]
        });

        var service = CreateService(json);
        var result = await service.GetSiteInfoAsync();

        Assert.Equal("15.1.8325.0", result.SitefinityVersion);
        Assert.Equal("4.8.1", result.DotNetVersion);
        Assert.Equal("TestProject", result.ProjectName);
        Assert.Equal(42, result.ModuleCount);
        Assert.Equal(2, result.Languages.Count);
        Assert.Single(result.Sites);
        Assert.True(result.Sites[0].IsDefault);
    }

    [Fact]
    public async Task ListPageRoutes_DeserializesCorrectly()
    {
        var json = JsonSerializer.Serialize(new PageRoutesResponse
        {
            PageRoutes =
            [
                new PageRoute
                {
                    Title = "Home",
                    Url = "/",
                    NodeType = "Standard",
                    IsPublished = true,
                    Depth = 0
                },
                new PageRoute
                {
                    Title = "About",
                    Url = "/about",
                    NodeType = "Standard",
                    IsPublished = true,
                    Depth = 1,
                    HasUrlEvaluation = true,
                    UrlEvaluationMode = "Dynamic"
                }
            ],
            Warnings = ["Page '/about' uses dynamic URL evaluation"]
        });

        var service = CreateService(json);
        var result = await service.ListPageRoutesAsync();

        Assert.Equal(2, result.PageRoutes.Count);
        Assert.StartsWith("/", result.PageRoutes[0].Url);
        Assert.True(result.PageRoutes[1].HasUrlEvaluation);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task ListApiRoutes_DeserializesODataRoutes()
    {
        var json = JsonSerializer.Serialize(new ApiRoutesResponse
        {
            ServiceStackRoutes =
            [
                new ApiRoute { Path = "/mcp/ping", Verbs = "GET", RequestType = "McpPingRequest" }
            ],
            ODataRoutes =
            [
                new ODataRoute { EntitySetName = "newsitems", EntitySetUrl = "/api/default/newsitems" },
                new ODataRoute { EntitySetName = "blogposts", EntitySetUrl = "/api/default/blogposts" }
            ],
            Warnings = []
        });

        var service = CreateService(json);
        var result = await service.ListApiRoutesAsync();

        Assert.Single(result.ServiceStackRoutes);
        Assert.Equal(2, result.ODataRoutes.Count);
        Assert.Equal("newsitems", result.ODataRoutes[0].EntitySetName);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task GetPageDetails_DeserializesCorrectly()
    {
        var json = JsonSerializer.Serialize(new PageDetailsResponse
        {
            Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
            PageDataId = "11111111-2222-3333-4444-555555555555",
            Title = "Test Page",
            Url = "/test-page",
            UrlName = "test-page",
            NodeType = "Standard",
            IsPublished = true,
            TemplateName = "Default Template",
            Description = "A test page",
            Depth = 1,
            Widgets =
            [
                new PageWidgetInfo
                {
                    ObjectType = "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy",
                    WidgetName = "MvcControllerProxy",
                    FriendlyName = "TestController",
                    PlaceHolder = "Content",
                    Caption = "Test Widget",
                    IsLayoutControl = false,
                    Properties = new Dictionary<string, string>
                    {
                        ["ControllerName"] = "TestController",
                        ["TemplateName"] = "Default"
                    }
                }
            ],
            Warnings = ["Partial title match"]
        });

        var service = CreateService(json);
        var result = await service.GetPageDetailsAsync("/test-page");

        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", result.Id);
        Assert.Equal("Test Page", result.Title);
        Assert.Equal("/test-page", result.Url);
        Assert.Equal("Standard", result.NodeType);
        Assert.True(result.IsPublished);
        Assert.Equal("Default Template", result.TemplateName);
        Assert.Single(result.Widgets);
        Assert.Equal("TestController", result.Widgets[0].FriendlyName);
        Assert.Equal(2, result.Widgets[0].Properties.Count);
        Assert.Equal("TestController", result.Widgets[0].Properties["ControllerName"]);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public async Task ListApiRoutes_HandlesEmptyODataGracefully()
    {
        var json = JsonSerializer.Serialize(new ApiRoutesResponse
        {
            ServiceStackRoutes =
            [
                new ApiRoute { Path = "/mcp/ping", Verbs = "GET", RequestType = "McpPingRequest" }
            ],
            ODataRoutes = [],
            Warnings = ["OData metadata endpoint returned empty response"]
        });

        var service = CreateService(json);
        var result = await service.ListApiRoutesAsync();

        Assert.Empty(result.ODataRoutes);
        Assert.Single(result.Warnings);
        Assert.Contains("OData", result.Warnings[0]);
    }
}

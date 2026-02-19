using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PageToolsUnitTests
{
    private static (PageTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new PageTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task GetPageDetails_ReturnsJsonWithWidgets()
    {
        var (tools, mock) = CreateTools();
        mock.GetPageDetailsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageDetailsResponse
            {
                Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                Title = "MacDot Dashboard",
                Url = "/ug/macdot",
                UrlName = "macdot",
                NodeType = "Standard",
                IsPublished = true,
                TemplateName = "UG Template",
                Widgets =
                [
                    new PageWidgetInfo
                    {
                        ObjectType = "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy",
                        WidgetName = "MvcControllerProxy",
                        FriendlyName = "MacDotDashboard",
                        PlaceHolder = "Content",
                        Properties = new Dictionary<string, string>
                        {
                            ["ControllerName"] = "MacDotDashboard",
                            ["TemplateName"] = "Default"
                        }
                    }
                ]
            });

        var result = await tools.GetPageDetails("/ug/macdot");

        Assert.Contains("MacDot Dashboard", result);
        Assert.Contains("/ug/macdot", result);
        Assert.Contains("MvcControllerProxy", result);
        Assert.Contains("MacDotDashboard", result);
        Assert.Contains("ControllerName", result);
        Assert.Contains("UG Template", result);
    }

    [Fact]
    public async Task GetPageDetails_HandlesNotFound()
    {
        var (tools, mock) = CreateTools();
        mock.GetPageDetailsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));

        var result = await tools.GetPageDetails("/nonexistent-page");

        Assert.Contains("Error:", result);
        Assert.Contains("Ensure the Sitefinity plugin is installed", result);
    }

    [Fact]
    public async Task GetPageDetails_IncludesWidgetIdAndSettingsProperties()
    {
        var (tools, mock) = CreateTools();
        mock.GetPageDetailsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageDetailsResponse
            {
                Id = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                Title = "Test Page",
                Url = "/test",
                Widgets =
                [
                    new PageWidgetInfo
                    {
                        Id = "11111111-2222-3333-4444-555555555555",
                        ObjectType = "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy",
                        WidgetName = "MvcControllerProxy",
                        FriendlyName = "ContentBlock",
                        PlaceHolder = "Content",
                        Properties = new Dictionary<string, string>
                        {
                            ["ControllerName"] = "ContentBlock",
                            ["Settings"] = ""
                        },
                        SettingsProperties = new Dictionary<string, string>
                        {
                            ["SharedContentID"] = "aabbccdd-1122-3344-5566-778899001122",
                            ["ProviderName"] = "OpenAccessProvider"
                        }
                    }
                ]
            });

        var result = await tools.GetPageDetails("/test");

        Assert.Contains("11111111-2222-3333-4444-555555555555", result);
        Assert.Contains("SharedContentID", result);
        Assert.Contains("ProviderName", result);
        Assert.Contains("SettingsProperties", result);
    }

    [Fact]
    public async Task GetWidgetProperties_ReturnsJsonWithBothPropertyLevels()
    {
        var (tools, mock) = CreateTools();
        mock.GetWidgetPropertiesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new WidgetPropertiesResponse
            {
                WidgetId = "11111111-2222-3333-4444-555555555555",
                ObjectType = "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy",
                FriendlyName = "ContentBlock",
                PlaceHolder = "Content",
                Properties = new Dictionary<string, string>
                {
                    ["ControllerName"] = "ContentBlock",
                    ["ID"] = "11111111-2222-3333-4444-555555555555",
                    ["Settings"] = ""
                },
                SettingsProperties = new Dictionary<string, string>
                {
                    ["SharedContentID"] = "aabbccdd-1122-3344-5566-778899001122",
                    ["ProviderName"] = "OpenAccessProvider",
                    ["Model"] = "{\"Chunks\":[{\"Value\":\"<p>Hello</p>\"}]}"
                }
            });

        var result = await tools.GetWidgetProperties("11111111-2222-3333-4444-555555555555");

        Assert.Contains("ContentBlock", result);
        Assert.Contains("SharedContentID", result);
        Assert.Contains("ProviderName", result);
        Assert.Contains("Model", result);
        Assert.Contains("SettingsProperties", result);
        Assert.Contains("11111111-2222-3333-4444-555555555555", result);
    }

    [Fact]
    public async Task GetWidgetProperties_HandlesNotFound()
    {
        var (tools, mock) = CreateTools();
        mock.GetWidgetPropertiesAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));

        var result = await tools.GetWidgetProperties("00000000-0000-0000-0000-000000000000");

        Assert.Contains("Error:", result);
        Assert.Contains("Ensure the Sitefinity plugin is installed", result);
    }

}

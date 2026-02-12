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

}

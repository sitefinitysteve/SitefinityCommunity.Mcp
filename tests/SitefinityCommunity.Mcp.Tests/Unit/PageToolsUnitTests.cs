using ModelContextProtocol;
using System.Text.Json;
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

        var result = JsonSerializer.Serialize(await tools.GetPageDetails("/ug/macdot"));

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

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetPageDetails("/nonexistent-page"));

        Assert.Contains("Ensure the Sitefinity plugin is installed", ex.Message);
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

        var result = JsonSerializer.Serialize(await tools.GetPageDetails("/test"));

        Assert.Contains("11111111-2222-3333-4444-555555555555", result);
        Assert.Contains("SharedContentID", result);
        Assert.Contains("ProviderName", result);
        Assert.Contains("SettingsProperties", result);
    }

    [Fact]
    public async Task GetWidgetProperties_ReturnsJsonWithBothPropertyLevels()
    {
        var (tools, mock) = CreateTools();
        mock.GetWidgetPropertiesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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

        var result = JsonSerializer.Serialize(await tools.GetWidgetProperties("11111111-2222-3333-4444-555555555555", "/test-page"));

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
        mock.GetWidgetPropertiesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetWidgetProperties("00000000-0000-0000-0000-000000000000", "/test-page"));

        Assert.Contains("Ensure the Sitefinity plugin is installed", ex.Message);
    }

    [Fact]
    public async Task GetPageWidgetTree_ReturnsTreeWithPlaceholders()
    {
        var (tools, mock) = CreateTools();
        mock.GetPageWidgetTreeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageWidgetTreeResponse
            {
                PageId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                PageTitle = "Home",
                PageUrl = "/home",
                TemplateId = "tmpl-1",
                Placeholders =
                [
                    new PlaceholderNode
                    {
                        Name = "Content",
                        Widgets =
                        [
                            new WidgetNode
                            {
                                Id = "w-layout",
                                ObjectType = "Telerik.Sitefinity.Web.UI.ContentUI.LayoutControl",
                                FriendlyName = "Layout",
                                Caption = "grid-8+4",
                                PlaceHolder = "Content",
                                IsLayoutControl = true,
                                RenderOrder = 0,
                                Children =
                                [
                                    new PlaceholderNode
                                    {
                                        Name = "w-layout_Col00",
                                        Widgets =
                                        [
                                            new WidgetNode
                                            {
                                                Id = "w-cb1",
                                                FriendlyName = "ContentBlock",
                                                PlaceHolder = "w-layout_Col00",
                                                RenderOrder = 0,
                                                Properties = new Dictionary<string, string> { ["Content"] = "<p>Left</p>" },
                                            },
                                        ],
                                    },
                                    new PlaceholderNode
                                    {
                                        Name = "w-layout_Col01",
                                        Widgets =
                                        [
                                            new WidgetNode
                                            {
                                                Id = "w-cb2",
                                                FriendlyName = "ContentBlock",
                                                PlaceHolder = "w-layout_Col01",
                                                RenderOrder = 0,
                                                Properties = new Dictionary<string, string> { ["Content"] = "<p>Right</p>" },
                                            },
                                        ],
                                    },
                                ],
                            },
                        ],
                    },
                ],
            });

        var result = JsonSerializer.Serialize(await tools.GetPageWidgetTree("/home"));

        Assert.Contains("Home", result);
        Assert.Contains("/home", result);
        Assert.Contains("w-layout", result);
        Assert.Contains("w-layout_Col00", result);
        Assert.Contains("w-layout_Col01", result);
        Assert.Contains("Left", result);
        Assert.Contains("Right", result);
        Assert.Contains("\"IsLayoutControl\":true", result);
    }

    [Fact]
    public async Task GetPageWidgetTree_MergedPropertiesLevel2Wins()
    {
        // The plugin does the merge; here we verify the tool passes it through faithfully.
        var (tools, mock) = CreateTools();
        mock.GetPageWidgetTreeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageWidgetTreeResponse
            {
                PageId = "p",
                Placeholders =
                [
                    new PlaceholderNode
                    {
                        Name = "Content",
                        Widgets =
                        [
                            new WidgetNode
                            {
                                Id = "w",
                                FriendlyName = "ContentBlock",
                                // Level 2 already overrode Level 1 on the plugin side
                                Properties = new Dictionary<string, string> { ["TemplateName"] = "B" },
                            },
                        ],
                    },
                ],
            });

        var result = JsonSerializer.Serialize(await tools.GetPageWidgetTree("/page"));

        Assert.Contains("\"TemplateName\":\"B\"", result);
        Assert.DoesNotContain("\"TemplateName\": \"A\"", result);
    }

    [Fact]
    public async Task GetPageWidgetTree_ExcludeLayoutControls()
    {
        var (tools, mock) = CreateTools();
        // When includeLayoutControls=false, the plugin flattens layout nodes.
        mock.GetPageWidgetTreeAsync(Arg.Any<string>(), false, Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageWidgetTreeResponse
            {
                Placeholders =
                [
                    new PlaceholderNode
                    {
                        Name = "Content",
                        Widgets =
                        [
                            new WidgetNode
                            {
                                Id = "w-cb1",
                                FriendlyName = "ContentBlock",
                                IsLayoutControl = false,
                                Properties = new Dictionary<string, string> { ["Content"] = "<p>Hi</p>" },
                            },
                        ],
                    },
                ],
            });

        var result = JsonSerializer.Serialize(await tools.GetPageWidgetTree("/home", includeLayoutControls: false));

        Assert.Contains("ContentBlock", result);
        // No layout control emitted
        Assert.DoesNotContain("\"IsLayoutControl\": true", result);
    }

    [Fact]
    public async Task GetPageWidgetTree_HandlesBrokenSiblingChainWarning()
    {
        var (tools, mock) = CreateTools();
        mock.GetPageWidgetTreeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PageWidgetTreeResponse
            {
                Warnings = ["Broken sibling chain detected in placeholder 'Content' — appended 2 unreached widget(s) in ORM order."],
            });

        var result = JsonSerializer.Serialize(await tools.GetPageWidgetTree("/home"));

        Assert.Contains("Broken sibling chain", result);
    }

    [Fact]
    public async Task GetPageWidgetTree_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.GetPageWidgetTreeAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetPageWidgetTree("/missing"));

        Assert.Contains("Ensure the Sitefinity plugin is installed", ex.Message);
    }
}

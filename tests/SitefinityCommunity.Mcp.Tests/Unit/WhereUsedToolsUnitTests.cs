using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class WhereUsedToolsUnitTests
{
    private static (WhereUsedTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new WhereUsedTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task WhereUsed_ReturnsUsagesAcrossPages()
    {
        var (tools, mock) = CreateTools();
        mock.WhereUsedAsync("ContentBlock", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new WhereUsedResponse
            {
                Query = "ContentBlock",
                ResolvedKind = "widget",
                ResolvedTitle = "ContentBlock",
                TotalUsages = 2,
                Usages =
                [
                    new WhereUsedItem
                    {
                        HostKind = "page",
                        HostId = "11111111-1111-1111-1111-111111111111",
                        HostTitle = "Home",
                        HostUrl = "/home",
                        WidgetId = "aaaa1111-2222-3333-4444-555555555555",
                        WidgetName = "ContentBlock",
                        MatchReason = "ControllerName matches 'ContentBlock'"
                    },
                    new WhereUsedItem
                    {
                        HostKind = "page",
                        HostTitle = "About",
                        HostUrl = "/about",
                        MatchReason = "Widget type contains 'ContentBlock'"
                    }
                ]
            });

        var result = await tools.WhereUsed("ContentBlock");

        Assert.Contains("ContentBlock", result);
        Assert.Contains("/home", result);
        Assert.Contains("/about", result);
        Assert.Contains("ControllerName matches", result);
        Assert.Contains("\"TotalUsages\": 2", result);
    }

    [Fact]
    public async Task WhereUsed_TemplateUsageIncludesInheritingTemplates()
    {
        var (tools, mock) = CreateTools();
        mock.WhereUsedAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new WhereUsedResponse
            {
                ResolvedKind = "template",
                ResolvedTitle = "Base Template",
                Usages =
                [
                    new WhereUsedItem { HostKind = "page", HostTitle = "Landing", HostUrl = "/landing", MatchReason = "Page uses this template" },
                    new WhereUsedItem { HostKind = "template", HostTitle = "Child Template", MatchReason = "Template inherits from this template" }
                ]
            });

        var result = await tools.WhereUsed("11111111-1111-1111-1111-111111111111", "template");

        Assert.Contains("Base Template", result);
        Assert.Contains("Child Template", result);
        Assert.Contains("inherits from this template", result);
    }

    [Fact]
    public async Task WhereUsed_RequiresQuery()
    {
        var (tools, _) = CreateTools();

        var result = await tools.WhereUsed("");

        Assert.Contains("Error:", result);
        Assert.Contains("query is required", result);
    }

    [Fact]
    public async Task WhereUsed_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.WhereUsedAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await tools.WhereUsed("ContentBlock");

        Assert.Contains("Error:", result);
        Assert.Contains("Ensure the Sitefinity plugin is installed", result);
    }
}

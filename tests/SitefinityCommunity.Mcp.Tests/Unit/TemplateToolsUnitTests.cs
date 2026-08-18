using ModelContextProtocol;
using System.Text.Json;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class TemplateToolsUnitTests
{
    private static (TemplateTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new TemplateTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task ListTemplates_ReturnsJsonWithTemplates()
    {
        var (tools, mock) = CreateTools();
        mock.ListTemplatesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatesResponse
            {
                Templates =
                [
                    new PageTemplateInfo { Id = "g1", Name = "Default", Title = "Default", Framework = "Mvc", Culture = "en" },
                    new PageTemplateInfo { Id = "g2", Name = "LegacyWF", Title = "Legacy WF", Framework = "WebForms", Culture = "en" },
                ],
            });

        var result = JsonSerializer.Serialize(await tools.ListTemplates());

        Assert.Contains("Default", result);
        Assert.Contains("LegacyWF", result);
        Assert.Contains("Mvc", result);
        Assert.Contains("WebForms", result);
    }

    [Fact]
    public async Task ListTemplates_HandlesEmptyList()
    {
        var (tools, mock) = CreateTools();
        mock.ListTemplatesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TemplatesResponse());

        var result = JsonSerializer.Serialize(await tools.ListTemplates());

        Assert.Contains("\"Templates\":[]", result);
    }

    [Fact]
    public async Task ListTemplates_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.ListTemplatesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Unreachable"));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.ListTemplates());

        Assert.StartsWith("Error:", ex.Message);
        Assert.Contains("Unreachable", ex.Message);
    }
}

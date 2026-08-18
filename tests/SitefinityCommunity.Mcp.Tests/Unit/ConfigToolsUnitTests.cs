using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class ConfigToolsUnitTests
{
    private static (ConfigTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new ConfigTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task ListConfigSections_ReturnsJsonWithSectionNames()
    {
        var (tools, mock) = CreateTools();
        mock.ListConfigSectionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConfigSectionsResponse
            {
                Sections = ["SystemConfig", "SecurityConfig", "MultisiteConfig"]
            });

        var result = await tools.ListConfigSections();

        Assert.Contains("SystemConfig", result);
        Assert.Contains("SecurityConfig", result);
        Assert.Contains("MultisiteConfig", result);
    }

    [Fact]
    public async Task GetConfigSection_ReturnsFlattenedEntries()
    {
        var (tools, mock) = CreateTools();
        mock.GetConfigSectionAsync("SystemConfig", Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConfigSectionResponse
            {
                SectionName = "SystemConfig",
                SectionType = "Telerik.Sitefinity.Configuration.SystemConfig",
                Found = true,
                Entries =
                [
                    new ConfigEntry { Path = "DisableDbConfigsLoad", Value = "False" },
                    new ConfigEntry { Path = "SmtpSettings.Password", Value = "[REDACTED]" }
                ]
            });

        var result = await tools.GetConfigSection("SystemConfig");

        Assert.Contains("SystemConfig", result);
        Assert.Contains("DisableDbConfigsLoad", result);
        Assert.Contains("[REDACTED]", result);
    }

    [Fact]
    public async Task GetConfigSection_RequiresSectionName()
    {
        var (tools, _) = CreateTools();

        var result = await tools.GetConfigSection("   ");

        Assert.Contains("Error:", result);
        Assert.Contains("sectionName is required", result);
    }

    [Fact]
    public async Task GetConfigSection_DefaultsToOverridesOnlyAndForwardsBounds()
    {
        var (tools, mock) = CreateTools();
        mock.GetConfigSectionAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConfigSectionResponse { SectionName = "ContentViewConfig", Found = true });

        await tools.GetConfigSection("ContentViewConfig", pathFilter: "NewsBackend", maxEntries: 50);

        // The bounds must reach the plugin — this is what keeps a defaults-merged section from
        // returning ~375,000 entries and killing the stdio transport.
        await mock.Received(1).GetConfigSectionAsync(
            "ContentViewConfig", "NewsBackend", 50, false, Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetConfigSection_ReportsTruncationAndTrueTotal()
    {
        var (tools, mock) = CreateTools();
        mock.GetConfigSectionAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ConfigSectionResponse
            {
                SectionName = "ContentViewConfig",
                Found = true,
                Entries = [new ConfigEntry { Path = "contentViewControls[NewsBackend].x", Value = "1" }],
                TotalCount = 4210,
                ReturnedCount = 1,
                Truncated = true,
                MaxEntries = 500,
                DefaultsSkipped = 371_334
            });

        var result = await tools.GetConfigSection("ContentViewConfig");

        Assert.Contains("4210", result);
        Assert.Contains("true", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("371334", result.Replace(",", string.Empty));
    }

    [Fact]
    public async Task GetConfigSection_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.GetConfigSectionAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(),
                Arg.Any<bool>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));

        var result = await tools.GetConfigSection("NopeConfig");

        Assert.Contains("Error:", result);
        Assert.Contains("Ensure the Sitefinity plugin is installed", result);
    }
}

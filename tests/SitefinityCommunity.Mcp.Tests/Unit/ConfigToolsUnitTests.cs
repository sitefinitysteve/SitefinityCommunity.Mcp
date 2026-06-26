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
        mock.GetConfigSectionAsync("SystemConfig", Arg.Any<string?>(), Arg.Any<CancellationToken>())
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
    public async Task GetConfigSection_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.GetConfigSectionAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Response status code does not indicate success: 404 (Not Found)."));

        var result = await tools.GetConfigSection("NopeConfig");

        Assert.Contains("Error:", result);
        Assert.Contains("Ensure the Sitefinity plugin is installed", result);
    }
}

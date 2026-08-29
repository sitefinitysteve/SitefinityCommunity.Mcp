using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Extensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tests.Helpers;

namespace SitefinityCommunity.Mcp.Tests.Unit;

/// <summary>
/// Per-capability admin toggles: ping roster parsing, backwards compatibility with plugins that
/// report no roster, the tool filter's pre-block, and the 403 body mapping.
/// </summary>
[Trait("Category", "Unit")]
public sealed class CapabilityGateUnitTests
{
    private static SitefinityMcpConfig BuildConfig() => new()
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

    private static ApiKeyValidationService CreateValidator(
        string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var resolver = new EnvironmentResolver(BuildConfig());
        var handler = new MockHttpMessageHandler(json, status);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        return new ApiKeyValidationService(resolver, factory, NullLogger<ApiKeyValidationService>.Instance);
    }

    // ── Ping roster parsing ───────────────────────────────────────

    [Fact]
    public async Task Ping_WithFeatures_ParsesRoster()
    {
        const string json = """
            {
              "Status": "ok",
              "Features": {
                "Logs": true,
                "Metadata": false,
                "Content": true,
                "Forms": false,
                "ConfigReader": false,
                "WhereUsed": true,
                "Permissions": true,
                "Maintenance": false,
                "Incident": {
                  "Enabled": true,
                  "AllowIisLogs": false,
                  "AllowEventLogs": true,
                  "AllowHttpErr": false
                }
              }
            }
            """;

        var validator = CreateValidator(json);

        Assert.Equal(ApiKeyValidationResult.Valid, await validator.ValidateAsync());

        var features = await validator.GetFeaturesAsync();

        Assert.NotNull(features);
        Assert.True(features.Logs);
        Assert.False(features.Metadata);
        Assert.False(features.Forms);
        Assert.False(features.ConfigReader);
        Assert.False(features.Maintenance);
        Assert.True(features.Incident.Enabled);
        Assert.False(features.Incident.AllowIisLogs);
        Assert.True(features.Incident.AllowEventLogs);
        Assert.False(features.Incident.AllowHttpErr);
    }

    [Fact]
    public async Task Ping_WithoutFeatures_ReportsNoRosterSoEverythingStaysEnabled()
    {
        // An older plugin build answers ping with just the status — every capability is enabled there.
        var validator = CreateValidator("""{"Status":"ok"}""");

        Assert.Equal(ApiKeyValidationResult.Valid, await validator.ValidateAsync());
        Assert.Null(await validator.GetFeaturesAsync());

        foreach (var tool in new[]
                 {
                     "sitefinity_list_forms",
                     "sitefinity_get_site_info",
                     "sitefinity_investigate_incident",
                     "sitefinity_clear_cache"
                 })
        {
            Assert.Null(CapabilityGate.CheckTool(tool, await validator.GetFeaturesAsync()));
        }
    }

    [Fact]
    public void Roster_DefaultsToEverythingEnabled()
    {
        var roster = FeatureRoster.AllEnabled;

        Assert.True(roster.Logs);
        Assert.True(roster.Metadata);
        Assert.True(roster.Content);
        Assert.True(roster.Forms);
        Assert.True(roster.ConfigReader);
        Assert.True(roster.WhereUsed);
        Assert.True(roster.Permissions);
        Assert.True(roster.Maintenance);
        Assert.True(roster.Incident.Enabled);
        Assert.True(roster.Incident.AllowIisLogs);
        Assert.True(roster.Incident.AllowEventLogs);
        Assert.True(roster.Incident.AllowHttpErr);
    }

    [Fact]
    public void Roster_PartialPayload_LeavesUnmentionedCapabilitiesEnabled()
    {
        var roster = JsonSerializer.Deserialize<FeatureRoster>(
            """{"Forms":false}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(roster);
        Assert.False(roster.Forms);
        Assert.True(roster.Metadata);
        Assert.True(roster.Incident.Enabled);
    }

    // ── Tool → capability map ─────────────────────────────────────

    [Theory]
    [InlineData("sitefinity_get_site_info", "Metadata")]
    [InlineData("sitefinity_get_page_widget_tree", "Metadata")]
    [InlineData("sitefinity_list_content", "Content")]
    [InlineData("sitefinity_list_form_responses", "Forms")]
    [InlineData("sitefinity_get_config_section", "ConfigReader")]
    [InlineData("sitefinity_search_settings", "ConfigReader")]
    [InlineData("sitefinity_where_used", "WhereUsed")]
    [InlineData("sitefinity_get_permissions", "Permissions")]
    [InlineData("sitefinity_investigate_incident", "Incident")]
    [InlineData("sitefinity_recycle_app", "Maintenance")]
    public void GetCapability_MapsRemoteTools(string toolName, string expected)
    {
        Assert.Equal(expected, CapabilityGate.GetCapability(toolName));
    }

    [Theory]
    [InlineData("sitefinity_list_environments")]
    [InlineData("sitefinity_set_default_environment")]
    [InlineData("sitefinity_check_status")]
    [InlineData("sitefinity_read_log_file")]
    [InlineData("sitefinity_search_logs")]
    [InlineData("sitefinity_list_log_files")]
    public void GetCapability_LeavesLocalCapableToolsUngated(string toolName)
    {
        // Log tools read the filesystem in local mode, so pre-blocking them on the remote Logs flag
        // would break a working local setup — the plugin's 403 covers them in remote mode.
        Assert.Null(CapabilityGate.GetCapability(toolName));
    }

    // ── Tool filter pre-block ─────────────────────────────────────

    [Fact]
    public void CheckTool_DisabledCapability_ReturnsFriendlyMessage()
    {
        var roster = FeatureRoster.AllEnabled;
        roster.Forms = false;

        var message = CapabilityGate.CheckTool("sitefinity_list_forms", roster);

        Assert.Equal(
            "This tool is disabled by the Sitefinity administrator (Admin > Advanced > McpSettings > Forms).",
            message);
    }

    [Fact]
    public void CheckTool_DisabledIncident_ReturnsFriendlyMessage()
    {
        var roster = FeatureRoster.AllEnabled;
        roster.Incident.Enabled = false;

        var message = CapabilityGate.CheckTool("sitefinity_investigate_incident", roster);

        Assert.NotNull(message);
        Assert.Contains("McpSettings > Incident", message);
    }

    [Fact]
    public void CheckTool_DisabledMaintenance_PointsAtTheWriteOperationsSwitch()
    {
        var roster = FeatureRoster.AllEnabled;
        roster.Maintenance = false;

        var message = CapabilityGate.CheckTool("sitefinity_clear_cache", roster);

        Assert.NotNull(message);
        Assert.Contains("Allow Write Operations", message);
    }

    [Fact]
    public void CheckTool_EnabledCapability_Proceeds()
    {
        Assert.Null(CapabilityGate.CheckTool("sitefinity_list_forms", FeatureRoster.AllEnabled));
    }

    [Fact]
    public void CheckTool_OneDisabledCapabilityDoesNotAffectOthers()
    {
        var roster = FeatureRoster.AllEnabled;
        roster.ConfigReader = false;

        Assert.NotNull(CapabilityGate.CheckTool("sitefinity_list_config_sections", roster));
        Assert.Null(CapabilityGate.CheckTool("sitefinity_get_site_info", roster));
    }

    // ── 403 body mapping ──────────────────────────────────────────

    [Fact]
    public async Task Forbidden_WithDisabledBody_MapsToFriendlyMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"Disabled":"Forms","Reason":"Disabled by the administrator in Sitefinity Admin > Advanced > McpSettings."}""",
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var ex = await Assert.ThrowsAsync<SitefinityCapabilityDisabledException>(
            () => response.EnsureCapabilityEnabledAsync());

        Assert.Equal("Forms", ex.Capability);
        Assert.Contains("McpSettings > Forms", ex.Message);
    }

    [Fact]
    public async Task Forbidden_WithoutDisabledBody_IsLeftForNormalStatusHandling()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"ResponseStatus":{"Message":"Invalid or missing MCP API key."}}""",
                System.Text.Encoding.UTF8,
                "application/json")
        };

        await response.EnsureCapabilityEnabledAsync();

        Assert.Throws<HttpRequestException>(() => response.EnsureSuccessStatusCode());
    }

    [Theory]
    [InlineData("FormsResponses", "Forms > Allow Responses")]
    [InlineData("ConfigSection", "Config Reader > Excluded Sections")]
    public void SubCapability403_MapsToItsAdminSetting(string capability, string expectedPath)
    {
        // Sub-capabilities are never pre-blocked (they gate one endpoint / one named section, so they
        // are not roster entries) — they only ever arrive as a 403 body, and must still read sensibly.
        Assert.Null(FeatureRoster.AllEnabled.GetType().GetProperty(capability));

        var message = CapabilityGate.BuildDisabledMessage(capability);

        Assert.Contains(expectedPath, message);
    }

    [Fact]
    public async Task Forbidden_WithFormsResponsesBody_MapsToTheAllowResponsesSwitch()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"Disabled":"FormsResponses","Reason":"Disabled by the administrator in Sitefinity Admin > Advanced > McpSettings."}""",
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var ex = await Assert.ThrowsAsync<SitefinityCapabilityDisabledException>(
            () => response.EnsureCapabilityEnabledAsync());

        Assert.Equal("FormsResponses", ex.Capability);
        Assert.Contains("Forms > Allow Responses", ex.Message);
    }

    [Fact]
    public async Task Forbidden_WithExcludedSectionBody_MapsToTheExcludedSectionsSetting()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"Disabled":"ConfigSection","Reason":"Configuration section 'AuthenticationConfig' is excluded by the administrator."}""",
                System.Text.Encoding.UTF8,
                "application/json")
        };

        var ex = await Assert.ThrowsAsync<SitefinityCapabilityDisabledException>(
            () => response.EnsureCapabilityEnabledAsync());

        Assert.Equal("ConfigSection", ex.Capability);
        Assert.Contains("Config Reader > Excluded Sections", ex.Message);
    }

    [Fact]
    public void Roster_DoesNotCarrySubCapabilities()
    {
        // Forms responses and hidden config sections are enforced plugin-side only, so a disabled
        // one must NOT cause the tool to be pre-blocked — the tool still runs and gets the 403.
        var roster = FeatureRoster.AllEnabled;

        Assert.Null(CapabilityGate.CheckTool("sitefinity_list_form_responses", roster));
        Assert.Null(CapabilityGate.CheckTool("sitefinity_get_config_section", roster));
    }

    [Fact]
    public async Task SuccessResponse_IsNeverTreatedAsDisabled()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"Disabled":"Forms"}""", System.Text.Encoding.UTF8, "application/json")
        };

        await response.EnsureCapabilityEnabledAsync();
    }

    [Fact]
    public async Task MetadataService_DisabledCapability_ThrowsFriendlyException()
    {
        var resolver = new EnvironmentResolver(BuildConfig());
        var handler = new MockHttpMessageHandler(
            """{"Disabled":"Metadata","Reason":"Disabled by the administrator in Sitefinity Admin > Advanced > McpSettings."}""",
            HttpStatusCode.Forbidden);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        var service = new SitefinityMetadataService(resolver, factory);

        var ex = await Assert.ThrowsAsync<SitefinityCapabilityDisabledException>(
            () => service.GetSiteInfoAsync());

        Assert.Equal("Metadata", ex.Capability);
    }
}

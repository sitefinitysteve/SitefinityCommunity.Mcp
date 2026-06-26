using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for reading Sitefinity configuration sections. Config lives across the database and
/// .config files and is otherwise only visible through the admin UI; these tools surface it for
/// debugging. Values that look like credentials are redacted on the plugin side before transit.
/// </summary>
[McpServerToolType]
public sealed class ConfigTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public ConfigTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_config_sections", ReadOnly = true)]
    [Description("List the names of all registered Sitefinity configuration sections (e.g. systemConfig, " +
                 "securityConfig, multisiteConfig, projectConfig). Use this to discover a valid section name " +
                 "before calling sitefinity_get_config_section.")]
    public async Task<string> ListConfigSections(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var response = await this._metadataService.ListConfigSectionsAsync(environment, ct);
            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_get_config_section", ReadOnly = true)]
    [Description("Dump a single Sitefinity configuration section as a flattened list of name/value entries. " +
                 "Nested elements and dictionaries are expressed as dotted/indexed paths. Credential-like values " +
                 "(keys, passwords, connection strings, tokens, encrypted/[SecretData] properties) are ALWAYS " +
                 "redacted and never returned — in every environment, with no flag to reveal them. " +
                 "Call sitefinity_list_config_sections first to discover valid section names. " +
                 "Useful for debugging without round-tripping through the admin UI.")]
    public async Task<string> GetConfigSection(
        [Description("Config section name, e.g. \"systemConfig\", \"securityConfig\", \"multisiteConfig\".")]
        string sectionName,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            return "Error: sectionName is required. Call sitefinity_list_config_sections to discover valid names.";
        }

        try
        {
            var response = await this._metadataService.GetConfigSectionAsync(sectionName, environment, ct);
            return JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (HttpRequestException ex)
        {
            return $"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
}

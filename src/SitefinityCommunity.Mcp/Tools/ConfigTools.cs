using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
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

    [McpServerTool(Name = "sitefinity_list_config_sections", Title = "List Config Sections", ReadOnly = true, UseStructuredContent = true)]
    [Description("List the names of all registered Sitefinity configuration sections (e.g. systemConfig, " +
                 "securityConfig, multisiteConfig, projectConfig). Use this to discover a valid section name " +
                 "before calling sitefinity_get_config_section.")]
    public async Task<ConfigSectionsResponse> ListConfigSections(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var response = await this._metadataService.ListConfigSectionsAsync(environment, ct);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "sitefinity_search_settings", Title = "Search Advanced Settings", ReadOnly = true, UseStructuredContent = true)]
    [Description("Full-text search across ALL Sitefinity Advanced Settings using the backend " +
                 "'advanced-settings-search' Lucene index (Sitefinity 14.1+). Answers \"which section is " +
                 "setting X in?\" when you don't know where a setting lives — each hit includes the setting's " +
                 "caption, breadcrumb path, and owning section, which you can then dump with " +
                 "sitefinity_get_config_section + pathFilter. If the index is disabled or not yet built, the " +
                 "response says so and explains how to enable it. Values are secret-redacted.")]
    public async Task<SettingsSearchResponse> SearchSettings(
        [Description("Full-text query, e.g. \"output cache\", \"smtp host\", \"session timeout\".")]
        string query,
        [Description("Maximum results (default: 25, max: 100).")]
        int take = 0,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("Error: query is required.");
        }

        try
        {
            var response = await this._metadataService.SearchSettingsAsync(query, take, environment, ct);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}. Ensure the Sitefinity plugin (v2.0.0+) is installed and the site is running.");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "sitefinity_get_config_section", Title = "Get Config Section", ReadOnly = true, UseStructuredContent = true)]
    [Description("Dump a single Sitefinity configuration section as a flattened list of name/value entries. " +
                 "Nested elements and dictionaries are expressed as dotted/indexed paths. " +
                 "By default this returns OVERRIDES ONLY — values someone actually changed — because Sitefinity " +
                 "materializes a fully defaults-merged object graph (ContentViewConfig alone expands to ~375,000 " +
                 "leaves / ~79 MB if defaults are included). Results are capped; use pathFilter to narrow to the " +
                 "subtree you care about, e.g. \"contentViewControls[NewsBackend]\". " +
                 "Credential-like values (keys, passwords, connection strings, tokens, encrypted/[SecretData] " +
                 "properties) are ALWAYS redacted and never returned — in every environment, with no flag to " +
                 "reveal them. Call sitefinity_list_config_sections first to discover valid section names.")]
    public async Task<ConfigSectionResponse> GetConfigSection(
        [Description("Config section name, e.g. \"systemConfig\", \"securityConfig\", \"multisiteConfig\".")]
        string sectionName,
        [Description("Case-insensitive substring; only entries whose path contains it are returned. " +
                     "Strongly recommended on large sections, e.g. \"NewsBackend\" or \"smtp\".")]
        string? pathFilter = null,
        [Description("Maximum entries to return (default: 500, max: 5000). The response reports the true " +
                     "total match count even when truncated.")]
        int maxEntries = 0,
        [Description("Include values still sitting at their compiled-in defaults. Off by default. " +
                     "Turning this on without a pathFilter will hit the entry cap on most sections.")]
        bool includeDefaults = false,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            throw new McpException("Error: sectionName is required. Call sitefinity_list_config_sections to discover valid names.");
        }

        try
        {
            var response = await this._metadataService.GetConfigSectionAsync(
                sectionName, pathFilter, maxEntries, includeDefaults, environment, ct);

            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }
}

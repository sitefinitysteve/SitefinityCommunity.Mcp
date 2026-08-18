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
    /// <summary>Indented for small results, where a human may read the raw JSON.</summary>
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>Compact for large results — indentation roughly doubles the payload for no gain.</summary>
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

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
            return JsonSerializer.Serialize(response, IndentedJson);
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
                 "Nested elements and dictionaries are expressed as dotted/indexed paths. " +
                 "By default this returns OVERRIDES ONLY — values someone actually changed — because Sitefinity " +
                 "materializes a fully defaults-merged object graph (ContentViewConfig alone expands to ~375,000 " +
                 "leaves / ~79 MB if defaults are included). Results are capped; use pathFilter to narrow to the " +
                 "subtree you care about, e.g. \"contentViewControls[NewsBackend]\". " +
                 "Credential-like values (keys, passwords, connection strings, tokens, encrypted/[SecretData] " +
                 "properties) are ALWAYS redacted and never returned — in every environment, with no flag to " +
                 "reveal them. Call sitefinity_list_config_sections first to discover valid section names.")]
    public async Task<string> GetConfigSection(
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
            return "Error: sectionName is required. Call sitefinity_list_config_sections to discover valid names.";
        }

        try
        {
            var response = await this._metadataService.GetConfigSectionAsync(
                sectionName, pathFilter, maxEntries, includeDefaults, environment, ct);

            return JsonSerializer.Serialize(
                response, response.Entries.Count <= 200 ? IndentedJson : CompactJson);
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

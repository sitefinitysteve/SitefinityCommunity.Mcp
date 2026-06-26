using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tool for reverse lookups across the site: find every page (and template) that references a
/// given widget type, content item, or page template. Sitefinity has no built-in "where used" view,
/// so this is the safety check before deleting or refactoring shared resources.
/// </summary>
[McpServerToolType]
public sealed class WhereUsedTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public WhereUsedTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_where_used", ReadOnly = true)]
    [Description("Find everywhere a widget type, content item, or page template is referenced across the " +
                 "site's pages and templates. Pass a Guid (content item or template id) or a widget/controller " +
                 "type name (e.g. \"ContentBlock\", \"MvcControllerProxy\"). The kind is auto-detected; override " +
                 "it with the kind parameter when needed. Returns each host page/template, the widget carrying " +
                 "the reference, and why it matched. Use before deleting or refactoring a shared resource.")]
    public async Task<string> WhereUsed(
        [Description("What to look for: a Guid (content item / template id) or a widget/controller type name.")]
        string query,
        [Description("Optional interpretation override: \"widget\", \"content\", or \"template\". Auto-detected when omitted.")]
        string? kind = null,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Error: query is required (a Guid or a widget/controller type name).";
        }

        try
        {
            var response = await this._metadataService.WhereUsedAsync(query, kind, environment, ct);
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

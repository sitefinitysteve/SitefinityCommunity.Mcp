using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for inspecting Sitefinity pages: retrieving a page's template, widget tree,
/// and individual widget properties (including both Level 1 and Level 2 designer Settings).
/// </summary>
[McpServerToolType]
public sealed class PageTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public PageTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_get_page_details", Title = "Get Page Details", ReadOnly = true, UseStructuredContent = true)]
    [Description("Get detailed information about a specific CMS page as JSON. Returns page metadata, " +
                 "template name, and all widgets with their properties. " +
                 "Accepts a page ID (Guid), URL path (e.g. '/ug/macdot'), URL slug, or page title.")]
    public async Task<PageDetailsResponse> GetPageDetails(
        [Description("Page identifier: Guid, URL path, URL slug, or page title")]
        string pageIdentifier,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var page = await this._metadataService.GetPageDetailsAsync(pageIdentifier, environment, ct);
            return page;
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

    [McpServerTool(Name = "sitefinity_get_page_widget_tree", Title = "Get Page Widget Tree", ReadOnly = true, UseStructuredContent = true)]
    [Description("Return every widget on a page as a placeholder tree in render order. " +
                 "Layout controls contain nested child placeholders named '{ControlId}_Col00', '_Col01', etc. " +
                 "The Properties dict merges Level 1 (ORM) and Level 2 (Settings children) with Level 2 winning on conflict. " +
                 "Use this for full page composition; use sitefinity_get_widget_properties for a single widget.")]
    public async Task<PageWidgetTreeResponse> GetPageWidgetTree(
        [Description("Page identifier: Guid, URL path, URL slug, or page title")]
        string pageIdentifier,
        [Description("Include layout controls as explicit widget nodes. When false they still scaffold the tree " +
                     "but are not emitted as widgets. Default: true.")]
        bool includeLayoutControls = true,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var tree = await this._metadataService.GetPageWidgetTreeAsync(pageIdentifier, includeLayoutControls, environment, ct);
            return tree;
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

    [McpServerTool(Name = "sitefinity_get_widget_properties", Title = "Get Widget Properties", ReadOnly = true, UseStructuredContent = true)]
    [Description("Get full property details for a specific widget by its GUID. Returns both Level 1 properties " +
                 "(ControllerName, ID) and Level 2 Settings children (the actual designer field values, content, etc.). " +
                 "Use sitefinity_get_page_details first to find widget IDs on a page.")]
    public async Task<WidgetPropertiesResponse> GetWidgetProperties(
        [Description("The widget GUID (from sitefinity_get_page_details results)")]
        string widgetId,
        [Description("Page identifier (Guid, URL path, slug, or title) — the page the widget is on")]
        string pageIdentifier,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var widget = await this._metadataService.GetWidgetPropertiesAsync(widgetId, pageIdentifier, environment, ct);
            return widget;
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

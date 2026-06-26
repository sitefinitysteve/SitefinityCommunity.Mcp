using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tool for inspecting effective permissions on a securable Sitefinity object (page or content
/// item). Answers "why can't this role see/edit this?" by resolving per-role granted and denied
/// actions across each permission set, including whether the object inherits from its parent.
/// </summary>
[McpServerToolType]
public sealed class PermissionTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public PermissionTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_get_permissions", ReadOnly = true)]
    [Description("Inspect the effective permissions on a page or content item: which roles are granted or " +
                 "denied which actions (View, Modify, Delete, Create, ChangePermissions, …) across each " +
                 "permission set, and whether the object inherits from its parent. Pass a page identifier " +
                 "(Guid, URL, or title) for a page. For a content item, pass its Guid and the content type's " +
                 "full name via typeFullName. Use to debug why a role can't see or edit something.")]
    public async Task<string> GetPermissions(
        [Description("Page identifier (Guid, URL, or title) or content item Guid.")]
        string identifier,
        [Description("For a content item, the full CLR type name (e.g. \"Telerik.Sitefinity.News.Model.NewsItem\"). " +
                     "Omit to inspect a page node.")]
        string? typeFullName = null,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return "Error: identifier is required (a page identifier or a content item Guid).";
        }

        try
        {
            var response = await this._metadataService.GetPermissionsAsync(identifier, typeFullName, environment, ct);
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

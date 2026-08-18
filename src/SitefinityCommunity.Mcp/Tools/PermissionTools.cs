using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
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

    [McpServerTool(Name = "sitefinity_get_permissions", Title = "Get Permissions", ReadOnly = true, UseStructuredContent = true)]
    [Description("Inspect the effective permissions on a page or content item. Decodes each principal's " +
                 "granted/denied actions (View, Modify, Delete, Create, ChangePermissions, …) into EFFECTIVE " +
                 "access (deny wins over grant) across each permission set, flags whether the object is public " +
                 "(the Everyone role can View), whether any authenticated user can view it, and whether it " +
                 "inherits permissions (and from which parent). Pass a page identifier (Guid, URL, or title) for " +
                 "a page; for a content item pass its Guid and the content type's full name via typeFullName. " +
                 "Use to answer \"is this page public?\" or \"why can't this role see/edit this?\".")]
    public async Task<PermissionsResponse> GetPermissions(
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
            throw new McpException("Error: identifier is required (a page identifier or a content item Guid).");
        }

        try
        {
            var response = await this._metadataService.GetPermissionsAsync(identifier, typeFullName, environment, ct);
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

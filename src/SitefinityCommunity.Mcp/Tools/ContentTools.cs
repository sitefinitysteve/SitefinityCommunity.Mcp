using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for enumerating live content items of a Sitefinity content type.
/// Delegates to <see cref="ISitefinityMetadataService"/> which calls the remote <c>/RestApi/mcp/content</c> plugin endpoint.
/// </summary>
[McpServerToolType]
public sealed class ContentTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public ContentTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_content", ReadOnly = true)]
    [Description("List live content items of a given Sitefinity type as JSON. " +
                 "Returns Id, Title, UrlName, Status, DateCreated, and LastModified for each item so widgets " +
                 "and content-driven code can reference real IDs. " +
                 "Use sitefinity_list_dynamic_types or sitefinity_list_modules to discover available type full names " +
                 "(e.g., 'Telerik.Sitefinity.News.Model.NewsItem').")]
    public async Task<string> ListContent(
        [Description("Full CLR type name of the content type (e.g., 'Telerik.Sitefinity.News.Model.NewsItem')")]
        string typeFullName,
        [Description("Max items to return. Default 50.")]
        int take = 50,
        [Description("Items to skip for paging. Default 0.")]
        int skip = 0,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
        {
            return "Error: typeFullName is required.";
        }

        if (take <= 0)
        {
            take = 50;
        }

        if (take > 500)
        {
            take = 500;
        }

        if (skip < 0)
        {
            skip = 0;
        }

        try
        {
            var response = await this._metadataService.ListContentAsync(typeFullName, take, skip, environment, ct);
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

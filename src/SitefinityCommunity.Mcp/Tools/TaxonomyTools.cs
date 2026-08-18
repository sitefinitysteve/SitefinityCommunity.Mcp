using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tool for listing Sitefinity taxonomies (classifications) along with a capped sample of
/// their top-level taxa — useful for discovering tag/category IDs referenced by content.
/// </summary>
[McpServerToolType]
public sealed class TaxonomyTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public TaxonomyTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_taxonomies", Title = "List Taxonomies", ReadOnly = true, UseStructuredContent = true)]
    [Description("List all classifications (taxonomies) — Categories, Tags, and custom ones — plus a sample of their " +
                 "top-level taxa. Returns Taxonomies[] and Taxa{} (keyed by taxonomy id) so widgets configured with " +
                 "classification filters can reference real Ids.")]
    public async Task<TaxonomiesResponse> ListTaxonomies(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var response = await this._metadataService.ListTaxonomiesAsync(environment, ct);
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

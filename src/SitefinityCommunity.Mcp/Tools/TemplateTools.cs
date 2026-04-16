using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tool for listing Sitefinity page templates (MVC, Web Forms and Hybrid) with their framework,
/// parent chain, resource package, and culture.
/// </summary>
[McpServerToolType]
public sealed class TemplateTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public TemplateTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_templates", ReadOnly = true)]
    [Description("List all available page templates (Id, Name, Title, Framework MVC/WebForms, ParentTemplateId, Culture). " +
                 "Use this to discover template IDs when generating widgets or pages against real templates.")]
    public async Task<string> ListTemplates(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var response = await this._metadataService.ListTemplatesAsync(environment, ct);
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

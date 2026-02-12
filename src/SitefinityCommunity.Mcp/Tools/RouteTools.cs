using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

[McpServerToolType]
public sealed class RouteTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public RouteTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_page_routes", ReadOnly = true)]
    [Description("List all CMS page routes with URL evaluation warnings. Use this to audit site structure and detect pages with dynamic URL routing.")]
    public async Task<string> ListPageRoutes(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await this._metadataService.ListPageRoutesAsync(environment, ct);

            var sb = new StringBuilder();

            // ── Page Routes ──
            sb.AppendLine($"── Page Routes ({result.PageRoutes.Count}) ──");
            if (result.PageRoutes.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var page in result.PageRoutes)
                {
                    var status = page.IsPublished ? "Published" : "Draft";
                    var evalWarning = page.HasUrlEvaluation
                        ? $" \u26a0 URL eval: {page.UrlEvaluationMode}"
                        : "";

                    sb.AppendLine($"  {page.Url,-40} {page.Title} ({page.NodeType}, {status}){evalWarning}");
                }
            }

            sb.AppendLine();

            // ── Warnings ──
            sb.AppendLine($"── Warnings ({result.Warnings.Count}) ──");
            if (result.Warnings.Count == 0)
            {
                sb.AppendLine("  No issues detected.");
            }
            else
            {
                foreach (var warning in result.Warnings)
                {
                    sb.AppendLine($"  \u26a0 {warning}");
                }
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error listing page routes: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error listing page routes: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_list_api_routes", ReadOnly = true)]
    [Description("List all API routes: ServiceStack REST API routes and OData entity sets. Use this to discover available API endpoints for integration.")]
    public async Task<string> ListApiRoutes(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await this._metadataService.ListApiRoutesAsync(environment, ct);

            var sb = new StringBuilder();

            // ── ServiceStack Routes ──
            sb.AppendLine($"── ServiceStack Routes ({result.ServiceStackRoutes.Count}) ──");
            if (result.ServiceStackRoutes.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var api in result.ServiceStackRoutes)
                {
                    sb.AppendLine($"  {api.Verbs,-8} {api.Path,-40} {api.RequestType}");
                }
            }

            sb.AppendLine();

            // ── OData Entity Sets ──
            sb.AppendLine($"── OData Entity Sets ({result.ODataRoutes.Count}) ──");
            if (result.ODataRoutes.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var odata in result.ODataRoutes)
                {
                    sb.AppendLine($"  {odata.EntitySetUrl,-50} {odata.EntitySetName}");
                }
            }

            sb.AppendLine();

            // ── Warnings ──
            sb.AppendLine($"── Warnings ({result.Warnings.Count}) ──");
            if (result.Warnings.Count == 0)
            {
                sb.AppendLine("  No issues detected.");
            }
            else
            {
                foreach (var warning in result.Warnings)
                {
                    sb.AppendLine($"  \u26a0 {warning}");
                }
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error listing API routes: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error listing API routes: {ex.Message}";
        }
    }
}

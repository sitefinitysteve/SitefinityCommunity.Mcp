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

    [McpServerTool(Name = "sitefinity_list_routes", ReadOnly = true)]
    [Description("List all routes: CMS page routes, ServiceStack API routes, and URL evaluation warnings. Use this to audit site structure and detect pages with dynamic URL routing.")]
    public async Task<string> ListRoutes(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var routes = await this._metadataService.ListRoutesAsync(environment, ct);

            var sb = new StringBuilder();

            // ── Page Routes ──
            sb.AppendLine($"── Page Routes ({routes.PageRoutes.Count}) ──");
            if (routes.PageRoutes.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var page in routes.PageRoutes)
                {
                    var status = page.IsPublished ? "Published" : "Draft";
                    var evalWarning = page.HasUrlEvaluation
                        ? $" \u26a0 URL eval: {page.UrlEvaluationMode}"
                        : "";

                    sb.AppendLine($"  {page.Url,-40} {page.Title} ({page.NodeType}, {status}){evalWarning}");
                }
            }

            sb.AppendLine();

            // ── API Routes ──
            sb.AppendLine($"── API Routes ({routes.ApiRoutes.Count}) ──");
            if (routes.ApiRoutes.Count == 0)
            {
                sb.AppendLine("  (none)");
            }
            else
            {
                foreach (var api in routes.ApiRoutes)
                {
                    sb.AppendLine($"  {api.Verbs,-8} {api.Path,-40} {api.RequestType}");
                }
            }

            sb.AppendLine();

            // ── Warnings ──
            sb.AppendLine($"── Warnings ({routes.Warnings.Count}) ──");
            if (routes.Warnings.Count == 0)
            {
                sb.AppendLine("  No issues detected.");
            }
            else
            {
                foreach (var warning in routes.Warnings)
                {
                    sb.AppendLine($"  \u26a0 {warning}");
                }
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error listing routes: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error listing routes: {ex.Message}";
        }
    }
}

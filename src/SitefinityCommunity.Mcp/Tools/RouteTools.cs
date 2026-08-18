using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for listing Sitefinity routes: frontend CMS page URLs (including 301-redirect aliases)
/// and backend ServiceStack / OData API routes.
/// </summary>
[McpServerToolType]
public sealed class RouteTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public RouteTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_page_routes", Title = "List Page Routes", ReadOnly = true)]
    [Description("List all CMS frontend page routes. Returns a compact markdown list of each published page with its URL, title, and any legacy URLs that redirect to it.")]
    public async Task<string> ListPageRoutes(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var result = await this._metadataService.ListPageRoutesAsync(environment, ct);

            var sb = new StringBuilder();

            sb.AppendLine($"# Page Routes ({result.PageRoutes.Count})");
            sb.AppendLine();

            if (result.PageRoutes.Count == 0)
            {
                sb.AppendLine("_No pages found._");
            }
            else
            {
                foreach (var page in result.PageRoutes)
                {
                    var url = StripHost(page.Url);
                    var title = string.IsNullOrEmpty(page.Title) ? "(untitled)" : page.Title;

                    var line = $"- `{url}` — {title} [{(page.IsPublished ? "Published" : "Draft")}]";

                    if (page.HasUrlEvaluation)
                    {
                        // URL evaluation means sub-paths route into this page — the classic source of
                        // "why does this URL resolve?" confusion, so it earns a per-line flag.
                        line += $" [URL eval: {page.UrlEvaluationMode}]";
                    }

                    if (page.AdditionalUrls.Count > 0)
                    {
                        // Inline alternates — cheaper than a line each on pages with many redirects
                        var alts = string.Join(", ", page.AdditionalUrls.Select(StripHost));
                        line += $" _(redirects: {alts})_";
                    }

                    sb.AppendLine(line);
                }
            }

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"## Warnings ({result.Warnings.Count})");
                foreach (var warning in result.Warnings)
                {
                    sb.AppendLine($"- \u26a0 {warning}");
                }
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("No issues detected.");
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

    /// <summary>
    /// Defensively strip absolute URL prefixes (scheme + host) so only the path+query remains.
    /// Sitefinity mostly returns relative paths, but some multisite / canonical-url configs
    /// can surface fully-qualified URLs that bloat the output.
    /// </summary>
    private static string StripHost(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return url;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(url);
                return uri.PathAndQuery;
            }
            catch
            {
                // fall through
            }
        }

        return url;
    }

    [McpServerTool(Name = "sitefinity_list_api_routes", Title = "List API Routes", ReadOnly = true)]
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

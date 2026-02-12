using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

[McpServerToolType]
public sealed class SitefinityInfoTools
{
    private readonly ISitefinityMetadataService _metadataService;
    private readonly IEnvironmentResolver _resolver;

    public SitefinityInfoTools(ISitefinityMetadataService metadataService, IEnvironmentResolver resolver)
    {
        this._metadataService = metadataService;
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_get_site_info", ReadOnly = true)]
    [Description("Get Sitefinity instance metadata — version, .NET version, project name, configured languages, module count, and multisite info.")]
    public async Task<string> GetSiteInfo(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var (envName, config) = this._resolver.Resolve(environment);
            var info = await this._metadataService.GetSiteInfoAsync(environment, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Environment:       {envName} ({config.Url})");
            sb.AppendLine($"Sitefinity:        {info.SitefinityVersion}");
            sb.AppendLine($".NET:              {info.DotNetVersion}");
            sb.AppendLine($"Project:           {info.ProjectName}");
            sb.AppendLine($"Modules:           {info.ModuleCount}");

            if (info.Languages.Count > 0)
            {
                sb.AppendLine($"Languages:         {string.Join(", ", info.Languages)}");
            }

            if (info.Sites.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(info.Sites.Count == 1 ? "Single site:" : $"Multisite ({info.Sites.Count} sites):");
                foreach (var site in info.Sites)
                {
                    var defaultMarker = site.IsDefault ? " (default)" : "";
                    var url = string.IsNullOrEmpty(site.LiveUrl) ? "" : $" — {site.LiveUrl}";
                    sb.AppendLine($"  - {site.Name}{defaultMarker}{url}");
                }
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error fetching site info: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error fetching site info: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_list_modules", ReadOnly = true)]
    [Description("List all installed Sitefinity modules with their type (System/Dynamic/Custom), status (Active/Inactive), and startup type.")]
    public async Task<string> ListModules(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var modules = await this._metadataService.ListModulesAsync(environment, ct);

            if (modules.Count == 0)
                return "No modules found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {modules.Count} module(s):");
            sb.AppendLine();

            var grouped = modules.GroupBy(m => m.Type).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                sb.AppendLine($"── {group.Key} ({group.Count()}) ──");
                foreach (var mod in group.OrderBy(m => m.Name))
                {
                    sb.AppendLine($"  {mod.Name} [{mod.Status}] (startup: {mod.StartupType})");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error listing modules: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error listing modules: {ex.Message}";
        }
    }
}

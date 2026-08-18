using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for state-changing Sitefinity maintenance: clearing caches and recycling the app pool —
/// the inner loop of widget/template development. These are the only write tools in the server, so
/// they are gated twice: this side refuses unless the target environment sets
/// <c>allowWriteOperations: true</c> (never honored for prod-like names), and the Sitefinity plugin
/// enforces a matching admin switch. Both must opt in.
/// </summary>
[McpServerToolType]
public sealed class MaintenanceTools
{
    private readonly ISitefinityMetadataService _metadataService;
    private readonly IEnvironmentResolver _resolver;

    public MaintenanceTools(ISitefinityMetadataService metadataService, IEnvironmentResolver resolver)
    {
        this._metadataService = metadataService;
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_clear_cache", Title = "Clear Cache", ReadOnly = false, Destructive = true, Idempotent = true, UseStructuredContent = true)]
    [Description("Clear Sitefinity caches — the fast way to see widget/template changes without a full recycle. " +
                 "Scope: \"output\" (HTML output cache, default), \"whole\" (entire Sitefinity cache), or \"page\" " +
                 "(output cache for a single page; requires pageIdentifier). WRITE OPERATION: refused unless the " +
                 "target environment sets allowWriteOperations:true in sitefinity-mcp.json AND the Sitefinity admin " +
                 "switch is on. Never permitted for prod-like environments.")]
    public async Task<MaintenanceResponse> ClearCache(
        [Description("Cache scope: \"output\" (default), \"whole\", or \"page\".")]
        string scope = "output",
        [Description("When scope is \"page\", the page identifier (Guid, URL, or title) whose output cache to invalidate.")]
        string? pageIdentifier = null,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        var gate = this.CheckWriteAllowed(environment, out var envName);
        if (gate != null)
        {
            throw new McpException(gate);
        }

        try
        {
            var response = await this._metadataService.ClearCacheAsync(scope, pageIdentifier, environment, ct);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}. Ensure the Sitefinity plugin is installed, the site is running, " +
                   $"and 'Allow Write Operations' is enabled in Admin > Advanced > McpSettings.");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "sitefinity_recycle_app", Title = "Recycle Application", ReadOnly = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("Recycle the Sitefinity application (restart the app domain / app pool) so code, config, and " +
                 "binding changes take effect. Causes a brief outage and a cold-start delay on the next request. " +
                 "WRITE OPERATION: refused unless the target environment sets allowWriteOperations:true in " +
                 "sitefinity-mcp.json AND the Sitefinity admin switch is on. Never permitted for prod-like environments.")]
    public async Task<MaintenanceResponse> RecycleApp(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        var gate = this.CheckWriteAllowed(environment, out var envName);
        if (gate != null)
        {
            throw new McpException(gate);
        }

        try
        {
            var response = await this._metadataService.RecycleApplicationAsync(environment, ct);
            return response;
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}. Ensure the Sitefinity plugin is installed, the site is running, " +
                   $"and 'Allow Write Operations' is enabled in Admin > Advanced > McpSettings.");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns null when write operations are permitted for the target environment, otherwise a
    /// ready-to-return refusal message. Resolves the environment so an unknown name is reported clearly.
    /// </summary>
    private string? CheckWriteAllowed(string? environment, out string envName)
    {
        envName = string.Empty;

        try
        {
            var (name, config) = this._resolver.Resolve(environment);
            envName = name;

            if (!config.EffectiveAllowWriteOperations(name))
            {
                if (name.StartsWith("prod", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Refused: write operations are never permitted for prod-like environment '{name}'.";
                }

                return $"Refused: write operations are disabled for environment '{name}'. " +
                       $"Set \"allowWriteOperations\": true for this environment in sitefinity-mcp.json " +
                       $"and enable 'Allow Write Operations' in Sitefinity Admin > Advanced > McpSettings.";
            }

            return null;
        }
        catch (ArgumentException ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }
}

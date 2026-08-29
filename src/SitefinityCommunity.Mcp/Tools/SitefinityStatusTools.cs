using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tool for checking whether a Sitefinity instance is bootstrapped and reachable. Delegates
/// to <see cref="ISitefinityStatusService"/>, optionally polling until ready.
/// </summary>
[McpServerToolType]
public sealed class SitefinityStatusTools
{
    private readonly ISitefinityStatusService _statusService;
    private readonly IEnvironmentResolver _resolver;
    private readonly IApiKeyValidationService _apiKeyValidation;

    public SitefinityStatusTools(
        ISitefinityStatusService statusService,
        IEnvironmentResolver resolver,
        IApiKeyValidationService apiKeyValidation)
    {
        this._statusService = statusService;
        this._resolver = resolver;
        this._apiKeyValidation = apiKeyValidation;
    }

    [McpServerTool(Name = "sitefinity_check_status", Title = "Check Status", ReadOnly = true)]
    [Description("Check if a Sitefinity instance is bootstrapped and ready to serve requests. Polls the /RestApi/systemstatus endpoint. If the site is bootstrapping or unreachable, automatically retries every 5 seconds until ready or the timeout is reached. Also reports the MCP server version alongside the version of the plugin sources installed in the Sitefinity project, with the exact steps to fix a mismatch — check this first whenever a tool fails with a 404.")]
    public async Task<string> CheckStatus(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        [Description("Whether to wait and retry if the site is not ready (default: true)")] bool waitForReady = true,
        [Description("Maximum seconds to wait for the site to become ready (default: 120)")] int maxWaitSeconds = 120,
        CancellationToken ct = default)
    {
        try
        {
            var (envName, config) = this._resolver.Resolve(environment);

            var status = waitForReady && maxWaitSeconds > 0
                ? await this._statusService.WaitForReadyAsync(environment, maxWaitSeconds, ct: ct)
                : await this._statusService.CheckStatusAsync(environment, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Environment: {envName} ({config.Url})");
            sb.AppendLine($"Status:      {(status.IsReady ? "Ready" : status.IsBootstrapping ? "Bootstrapping" : "Unreachable")}");
            sb.AppendLine($"Details:     {status.Summary}");

            await this.AppendVersionsAsync(sb, environment, ct);

            return sb.ToString();
        }
        catch (TaskCanceledException) when (ct.IsCancellationRequested)
        {
            return "Status check was cancelled.";
        }
        catch (Exception ex)
        {
            return $"Error checking status: {ex.Message}";
        }
    }

    /// <summary>
    /// Appends the two-sided version handshake: this server's version, the version of the plugin
    /// sources installed in the Sitefinity project, and a verdict that spells out the fix when they
    /// disagree. The version comes from the same cached ping the tool filter already makes, so this
    /// costs no extra round trip.
    /// </summary>
    /// <param name="sb">Output being built.</param>
    /// <param name="environment">Environment name, or null for the default.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task AppendVersionsAsync(StringBuilder sb, string? environment, CancellationToken ct)
    {
        try
        {
            var pluginVersion = await this._apiKeyValidation.GetPluginVersionAsync(environment, ct);

            sb.AppendLine($"MCP server version: {PluginVersionAdvisor.ServerVersion}");
            sb.AppendLine($"Site plugin version: " +
                (string.IsNullOrWhiteSpace(pluginVersion)
                    ? PluginVersionAdvisor.UnknownPluginVersion
                    : pluginVersion));
            sb.AppendLine(PluginVersionAdvisor.BuildVerdict(pluginVersion));
        }
        catch (Exception ex)
        {
            // The version handshake is advisory — a failure here must not mask a good status answer.
            sb.AppendLine($"MCP server version: {PluginVersionAdvisor.ServerVersion}");
            sb.AppendLine($"Site plugin version: could not be read ({ex.Message}).");
        }
    }
}

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

    public SitefinityStatusTools(ISitefinityStatusService statusService, IEnvironmentResolver resolver)
    {
        this._statusService = statusService;
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_check_status", Title = "Check Status", ReadOnly = true)]
    [Description("Check if a Sitefinity instance is bootstrapped and ready to serve requests. Polls the /RestApi/systemstatus endpoint. If the site is bootstrapping or unreachable, automatically retries every 5 seconds until ready or the timeout is reached.")]
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
}

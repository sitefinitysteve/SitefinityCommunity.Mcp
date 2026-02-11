using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

[McpServerToolType]
public sealed class SitefinityStatusTools
{
    private const int DefaultPollIntervalSeconds = 5;
    private const int DefaultMaxWaitSeconds = 120;

    private readonly ISitefinityStatusService _statusService;
    private readonly IEnvironmentResolver _resolver;

    public SitefinityStatusTools(ISitefinityStatusService statusService, IEnvironmentResolver resolver)
    {
        this._statusService = statusService;
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_check_status", ReadOnly = true)]
    [Description("Check if a Sitefinity instance is bootstrapped and ready to serve requests. Polls the /RestApi/systemstatus endpoint. If the site is bootstrapping or unreachable, automatically retries every 5 seconds until ready or the timeout is reached.")]
    public async Task<string> CheckStatus(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        [Description("Whether to wait and retry if the site is not ready (default: true)")] bool waitForReady = true,
        [Description("Maximum seconds to wait for the site to become ready (default: 120)")] int maxWaitSeconds = DefaultMaxWaitSeconds,
        CancellationToken ct = default)
    {
        try
        {
            var (envName, config) = this._resolver.Resolve(environment);
            var status = await this._statusService.CheckStatusAsync(environment, ct);
            var totalWaited = 0;

            if (waitForReady && !status.IsReady && maxWaitSeconds > 0)
            {
                while (!status.IsReady && totalWaited < maxWaitSeconds && !ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(DefaultPollIntervalSeconds), ct);
                    totalWaited += DefaultPollIntervalSeconds;
                    status = await this._statusService.CheckStatusAsync(environment, ct);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Environment: {envName} ({config.Url})");
            sb.AppendLine($"Status:      {(status.IsReady ? "Ready" : status.IsBootstrapping ? "Bootstrapping" : "Unreachable")}");
            sb.AppendLine($"Details:     {status.Summary}");

            if (totalWaited > 0)
            {
                sb.AppendLine(status.IsReady
                    ? $"Note:        Site became ready after waiting ~{totalWaited} seconds."
                    : $"Note:        Site did NOT become ready after waiting {totalWaited} seconds (max: {maxWaitSeconds}s).");
            }

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

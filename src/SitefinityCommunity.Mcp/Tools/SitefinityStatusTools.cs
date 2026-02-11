using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

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

    [McpServerTool(Name = "sitefinity_check_status", ReadOnly = true)]
    [Description("Check if a Sitefinity instance is bootstrapped and ready to serve requests. Polls the /RestApi/systemstatus endpoint.")]
    public async Task<string> CheckStatus(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var (envName, config) = this._resolver.Resolve(environment);
            var status = await this._statusService.CheckStatusAsync(environment, ct);

            var sb = new StringBuilder();
            sb.AppendLine($"Environment: {envName} ({config.Url})");
            sb.AppendLine($"Status:      {(status.IsReady ? "Ready" : status.IsBootstrapping ? "Bootstrapping" : "Unreachable")}");
            sb.AppendLine($"Details:     {status.Summary}");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error checking status: {ex.Message}";
        }
    }
}

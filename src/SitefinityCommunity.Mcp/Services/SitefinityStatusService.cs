using System.Text.Json;
using Microsoft.Extensions.Logging;
using SitefinityCommunity.Mcp.Extensions;
using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Polls /RestApi/systemstatus to determine Sitefinity's bootstrap state.
/// Handles: 200 (bootstrapped), 503 (restarting), timeout, and unreachable states.
/// After systemstatus reports "Bootstrapped", verifies the MCP plugin is loaded
/// by pinging /RestApi/mcp/ping — this prevents false "Ready" when ServiceStack
/// plugins haven't initialized yet.
/// </summary>
public sealed class SitefinityStatusService : ISitefinityStatusService
{
    private readonly IEnvironmentResolver _resolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SitefinityStatusService> _logger;

    public SitefinityStatusService(
        IEnvironmentResolver resolver,
        IHttpClientFactory httpClientFactory,
        ILogger<SitefinityStatusService> logger)
    {
        this._resolver = resolver;
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
    }

    public async Task<SitefinityHealthResponse> CheckStatusAsync(
        string? environmentName = null, CancellationToken ct = default)
    {
        var (name, config) = this._resolver.Resolve(environmentName);

        try
        {
            var client = this._httpClientFactory.CreateClient("SitefinityStatus");
            client.BaseAddress = new Uri(config.Url.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = await client.GetAsync("/RestApi/systemstatus", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable)
            {
                return SitefinityHealthResponse.Bootstrapping();
            }

            if (response.IsSuccessStatusCode)
            {
                // Sitefinity redirects ALL requests to /sitefinity/status while bootstrapping.
                // HttpClient auto-follows the redirect, so we see a 200 OK with the HTML loading page.
                if (response.IsSitefinityBootstrapping())
                {
                    return SitefinityHealthResponse.Bootstrapping();
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                var systemStatus = ParseStatusResponse(content);

                // systemstatus says "Bootstrapped" but ServiceStack plugins may not be loaded yet.
                // Verify by pinging the MCP plugin endpoint directly.
                if (systemStatus.IsReady)
                {
                    var mcpReady = await VerifyMcpPluginAsync(config, ct);
                    if (!mcpReady)
                    {
                        return new SitefinityHealthResponse
                        {
                            IsBootstrapping = true,
                            Summary = "Sitefinity core is bootstrapped but MCP plugin endpoints are not yet available. Please wait."
                        };
                    }
                }

                return systemStatus;
            }

            // 404 can mean the endpoint doesn't exist but Sitefinity is running
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return SitefinityHealthResponse.Ready();
            }

            return SitefinityHealthResponse.Unreachable(
                $"Unexpected status code: {(int)response.StatusCode} {response.StatusCode}");
        }
        catch (TaskCanceledException)
        {
            return SitefinityHealthResponse.Unreachable("Request timed out after 10 seconds.");
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogWarning(ex, "Failed to reach Sitefinity at {Url}", config.Url);
            return SitefinityHealthResponse.Unreachable(ex.Message);
        }
    }

    public async Task<SitefinityHealthResponse> WaitForReadyAsync(
        string? environmentName = null,
        int maxWaitSeconds = 90,
        int pollIntervalSeconds = 5,
        CancellationToken ct = default)
    {
        var (envName, config) = this._resolver.Resolve(environmentName);
        var status = await this.CheckStatusAsync(environmentName, ct);

        if (status.IsReady || maxWaitSeconds <= 0)
        {
            return status;
        }

        this._logger.LogInformation(
            "Sitefinity ({Environment}, {Url}) is not ready — waiting up to {MaxWait}s...",
            envName, config.Url, maxWaitSeconds);

        var totalWaited = 0;
        while (!status.IsReady && totalWaited < maxWaitSeconds && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), ct);
            totalWaited += pollIntervalSeconds;
            status = await this.CheckStatusAsync(environmentName, ct);

            this._logger.LogInformation(
                "Waiting for Sitefinity ({Environment}): {Status} ({Waited}s / {MaxWait}s)",
                envName,
                status.IsReady ? "Ready" : status.IsBootstrapping ? "Bootstrapping" : "Unreachable",
                totalWaited,
                maxWaitSeconds);
        }

        return status;
    }

    /// <summary>
    /// Verifies that the MCP plugin is loaded and serving requests.
    /// After Sitefinity's systemstatus reports "Bootstrapped", ServiceStack plugins
    /// may still be initializing. A quick GET to /RestApi/mcp/ping confirms readiness.
    /// </summary>
    private async Task<bool> VerifyMcpPluginAsync(Configuration.EnvironmentConfig config, CancellationToken ct)
    {
        try
        {
            var client = this._httpClientFactory.CreateClient("McpPing");
            client.BaseAddress = new Uri(config.Url.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(5);

            if (!string.IsNullOrEmpty(config.SitefinityApiKey))
            {
                client.DefaultRequestHeaders.Add("X-MCP-API-Key", config.SitefinityApiKey);
            }

            var response = await client.GetAsync("/RestApi/mcp/ping?format=json", ct);

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogDebug(
                    "MCP ping returned {StatusCode} — plugin not yet loaded",
                    (int)response.StatusCode);
                return false;
            }

            if (response.IsSitefinityBootstrapping())
            {
                this._logger.LogDebug("MCP ping returned HTML — site still bootstrapping");
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException)
        {
            this._logger.LogDebug(ex, "MCP ping failed — plugin not yet available");
            return false;
        }
    }

    private static SitefinityHealthResponse ParseStatusResponse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Look for Sitefinity's bootstrap status indicator
            if (root.TryGetProperty("Operation", out var operation))
            {
                var opValue = operation.GetString() ?? string.Empty;
                if (opValue.Contains("Bootstrapped", StringComparison.OrdinalIgnoreCase))
                {
                    return SitefinityHealthResponse.Ready();
                }

                return new SitefinityHealthResponse
                {
                    IsBootstrapping = true,
                    Summary = $"Sitefinity status: {opValue}"
                };
            }

            return SitefinityHealthResponse.Ready();
        }
        catch (JsonException)
        {
            // Non-JSON response from /RestApi/systemstatus means the endpoint isn't serving
            // its expected JSON payload — most likely the site is still bootstrapping.
            return SitefinityHealthResponse.Bootstrapping();
        }
    }
}

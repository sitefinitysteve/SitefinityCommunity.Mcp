using System.Text.Json;
using Microsoft.Extensions.Logging;
using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Polls /RestApi/systemstatus to determine Sitefinity's bootstrap state.
/// Handles: 200 (bootstrapped), 503 (restarting), timeout, and unreachable states.
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
                var finalUrl = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
                if (finalUrl.Contains("/sitefinity/status", StringComparison.OrdinalIgnoreCase))
                {
                    return SitefinityHealthResponse.Bootstrapping();
                }

                // If the response is HTML (not JSON), the site is likely still bootstrapping
                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    return SitefinityHealthResponse.Bootstrapping();
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                return ParseStatusResponse(content);
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

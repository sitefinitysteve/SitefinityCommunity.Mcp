using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using SitefinityCommunity.Mcp.Configuration;

namespace SitefinityCommunity.Mcp.Services;

public enum ApiKeyValidationResult
{
    Valid,
    InvalidKey,
    Unreachable
}

public interface IApiKeyValidationService
{
    Task<ApiKeyValidationResult> ValidateAsync(string? environmentName = null, CancellationToken ct = default);
}

/// <summary>
/// Validates that the MCP server's API key matches Sitefinity's configured key
/// by calling the /RestApi/mcp/ping endpoint. Caches Valid/InvalidKey for 5 minutes
/// and Unreachable for 15 seconds per environment.
/// </summary>
public sealed class ApiKeyValidationService : IApiKeyValidationService
{
    private readonly IEnvironmentResolver _resolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiKeyValidationService> _logger;
    private readonly ConcurrentDictionary<string, (ApiKeyValidationResult Result, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan UnreachableCacheDuration = TimeSpan.FromSeconds(15);

    public ApiKeyValidationService(
        IEnvironmentResolver resolver,
        IHttpClientFactory httpClientFactory,
        ILogger<ApiKeyValidationService> logger)
    {
        this._resolver = resolver;
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
    }

    public async Task<ApiKeyValidationResult> ValidateAsync(string? environmentName = null, CancellationToken ct = default)
    {
        var (name, config) = this._resolver.Resolve(environmentName);

        // Check cache first
        if (this._cache.TryGetValue(name, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Result;
        }

        var result = await PingAsync(config, ct);

        var ttl = result == ApiKeyValidationResult.Unreachable ? UnreachableCacheDuration : CacheDuration;
        this._cache[name] = (result, DateTime.UtcNow.Add(ttl));

        return result;
    }

    private async Task<ApiKeyValidationResult> PingAsync(EnvironmentConfig config, CancellationToken ct)
    {
        try
        {
            var client = this._httpClientFactory.CreateClient("McpPing");
            client.BaseAddress = new Uri(config.Url.TrimEnd('/'));
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("X-MCP-API-Key", config.SitefinityApiKey);

            var response = await client.GetAsync("/RestApi/mcp/ping?format=json", ct);

            if (response.IsSuccessStatusCode)
            {
                // Sitefinity redirects ALL requests to /sitefinity/status while bootstrapping.
                // HttpClient auto-follows the redirect, so we see a 200 OK with the HTML loading page.
                var finalUrl = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
                if (finalUrl.Contains("/sitefinity/status", StringComparison.OrdinalIgnoreCase))
                {
                    this._logger.LogDebug("Ping redirected to bootstrapping page for {Url}", config.Url);
                    return ApiKeyValidationResult.Unreachable;
                }

                // If the response is HTML (not JSON), the site is likely still bootstrapping
                var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    this._logger.LogDebug("Ping returned HTML instead of JSON for {Url} — site is likely bootstrapping", config.Url);
                    return ApiKeyValidationResult.Unreachable;
                }

                this._logger.LogDebug("API key validated for {Url}", config.Url);
                return ApiKeyValidationResult.Valid;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                this._logger.LogWarning("API key rejected by {Url} (HTTP {StatusCode})", config.Url, (int)response.StatusCode);
                return ApiKeyValidationResult.InvalidKey;
            }

            // Any other status (404, 500, etc.) — treat as unreachable
            this._logger.LogWarning("Unexpected response from {Url}/RestApi/mcp/ping: HTTP {StatusCode}", config.Url, (int)response.StatusCode);
            return ApiKeyValidationResult.Unreachable;
        }
        catch (TaskCanceledException)
        {
            this._logger.LogWarning("API key validation timed out for {Url}", config.Url);
            return ApiKeyValidationResult.Unreachable;
        }
        catch (HttpRequestException ex)
        {
            this._logger.LogWarning(ex, "Could not reach {Url} for API key validation", config.Url);
            return ApiKeyValidationResult.Unreachable;
        }
    }
}

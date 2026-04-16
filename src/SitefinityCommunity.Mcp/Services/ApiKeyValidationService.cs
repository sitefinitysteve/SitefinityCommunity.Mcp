using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Extensions;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Outcome of calling <c>/RestApi/mcp/ping</c> on a Sitefinity environment to check the API key.
/// </summary>
public enum ApiKeyValidationResult
{
    /// <summary>Ping succeeded and the key matches Sitefinity's configured key.</summary>
    Valid,

    /// <summary>Sitefinity responded with 401/403 — the configured key does not match.</summary>
    InvalidKey,

    /// <summary>Sitefinity could not be reached, is still bootstrapping, or returned an unexpected response.</summary>
    Unreachable
}

/// <summary>
/// Proactively validates that the MCP server's API key matches the one configured in Sitefinity
/// by probing <c>/RestApi/mcp/ping</c>. Results are cached per environment to avoid per-tool ping overhead.
/// </summary>
public interface IApiKeyValidationService
{
    /// <summary>
    /// Validates the API key for the given environment (or the current default) against Sitefinity.
    /// Returns a cached result when the previous check is still within its TTL.
    /// </summary>
    Task<ApiKeyValidationResult> ValidateAsync(string? environmentName = null, CancellationToken ct = default);

    /// <summary>
    /// Removes the cached validation result for the given environment (or default).
    /// Call this when a data request fails with HTML, indicating Sitefinity restarted
    /// and the previously cached "Valid" result is stale.
    /// </summary>
    void InvalidateCache(string? environmentName = null);
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

    public void InvalidateCache(string? environmentName = null)
    {
        var (name, _) = this._resolver.Resolve(environmentName);
        this._cache.TryRemove(name, out _);
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
                if (response.IsSitefinityBootstrapping())
                {
                    this._logger.LogDebug(
                        "Ping returned HTML or redirected to bootstrap page for {Url} — site is still starting",
                        config.Url);
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

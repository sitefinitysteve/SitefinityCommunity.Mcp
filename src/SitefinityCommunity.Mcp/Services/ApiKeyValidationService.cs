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
/// by calling the /RestApi/mcp/ping endpoint. Caches results for 5 minutes per environment.
/// </summary>
public sealed class ApiKeyValidationService : IApiKeyValidationService
{
    private readonly IEnvironmentResolver _resolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiKeyValidationService> _logger;
    private readonly ConcurrentDictionary<string, (ApiKeyValidationResult Result, DateTime ExpiresAt)> _cache = new();
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

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

        this._cache[name] = (result, DateTime.UtcNow.Add(CacheDuration));

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

            var response = await client.GetAsync("/RestApi/mcp/ping", ct);

            if (response.IsSuccessStatusCode)
            {
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

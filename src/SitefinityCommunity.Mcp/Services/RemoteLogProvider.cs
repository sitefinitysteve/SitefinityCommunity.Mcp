using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Extensions;
using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Fetches logs via HTTP from the Sitefinity companion plugin endpoints.
/// Used when logsPath is not configured (remote servers like staging/prod).
/// </summary>
public sealed class RemoteLogProvider : ILogProvider
{
    private readonly EnvironmentConfig _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemoteLogProvider> _logger;

    public RemoteLogProvider(
        EnvironmentConfig config,
        IHttpClientFactory httpClientFactory,
        ILogger<RemoteLogProvider> logger)
    {
        this._config = config;
        this._httpClientFactory = httpClientFactory;
        this._logger = logger;
    }

    public async Task<IReadOnlyList<LogFileInfo>> ListFilesAsync(CancellationToken ct = default)
    {
        var client = CreateClient();
        var response = await client.GetAsync("/RestApi/mcp/logs?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        var files = await response.Content.ReadFromJsonAsync<List<LogFileInfo>>(SitefinityJsonOptions.Default, ct)
            ?? new List<LogFileInfo>();

        return files;
    }

    public async Task<string> ReadFileAsync(string fileName, CancellationToken ct = default)
    {
        var client = CreateClient();
        var encodedName = Uri.EscapeDataString(fileName);
        var response = await client.GetAsync($"/RestApi/mcp/logs/{encodedName}?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task<IReadOnlyList<LogSearchResult>> SearchAsync(
        string pattern,
        int contextLines,
        bool caseSensitive,
        string? fileName = null,
        int maxMatches = 0,
        CancellationToken ct = default)
    {
        // Search scans files server-side and can take longer than the cheap metadata calls,
        // so give it a more generous timeout than the 30s default.
        var client = CreateClient(TimeSpan.FromSeconds(120));
        var request = new
        {
            Pattern = pattern,
            ContextLines = contextLines,
            CaseSensitive = caseSensitive,
            FileName = fileName,
            MaxMatches = maxMatches
        };

        var response = await client.PostAsJsonAsync("/RestApi/mcp/logs/search?format=json", request, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        var results = await response.Content.ReadFromJsonAsync<List<LogSearchResult>>(SitefinityJsonOptions.Default, ct)
            ?? new List<LogSearchResult>();

        return results;
    }

    private HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = this._httpClientFactory.CreateClient("SitefinityPlugin");
        client.BaseAddress = new Uri(this._config.Url.TrimEnd('/'));
        client.Timeout = timeout ?? TimeSpan.FromSeconds(30);

        if (!string.IsNullOrEmpty(this._config.SitefinityApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-MCP-API-Key");
            client.DefaultRequestHeaders.Add("X-MCP-API-Key", this._config.SitefinityApiKey);
        }

        return client;
    }
}

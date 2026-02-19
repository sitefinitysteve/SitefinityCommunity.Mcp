using System.Net.Http.Json;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Extensions;
using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Fetches Sitefinity metadata via HTTP from the companion plugin endpoints.
/// </summary>
public sealed class SitefinityMetadataService : ISitefinityMetadataService
{
    private readonly IEnvironmentResolver _resolver;
    private readonly IHttpClientFactory _httpClientFactory;

    public SitefinityMetadataService(
        IEnvironmentResolver resolver,
        IHttpClientFactory httpClientFactory)
    {
        this._resolver = resolver;
        this._httpClientFactory = httpClientFactory;
    }

    public async Task<SiteInfoResponse> GetSiteInfoAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/site-info?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<SiteInfoResponse>(ct)
            ?? new SiteInfoResponse();
    }

    public async Task<IReadOnlyList<ModuleInfo>> ListModulesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/modules?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<List<ModuleInfo>>(ct)
            ?? [];
    }

    public async Task<IReadOnlyList<DynamicTypeInfo>> ListDynamicTypesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/dynamic-types?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<List<DynamicTypeInfo>>(ct)
            ?? [];
    }

    public async Task<IReadOnlyList<DynamicFieldInfo>> GetTypeFieldsAsync(
        string typeFullName, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encodedName = Uri.EscapeDataString(typeFullName);
        var response = await client.GetAsync($"/RestApi/mcp/dynamic-types/{encodedName}/fields?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<List<DynamicFieldInfo>>(ct)
            ?? [];
    }

    public async Task<PageRoutesResponse> ListPageRoutesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/page-routes?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<PageRoutesResponse>(ct)
            ?? new PageRoutesResponse();
    }

    public async Task<ApiRoutesResponse> ListApiRoutesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/api-routes?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ApiRoutesResponse>(ct)
            ?? new ApiRoutesResponse();
    }

    public async Task<PageDetailsResponse> GetPageDetailsAsync(
        string pageIdentifier, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(pageIdentifier);
        var response = await client.GetAsync($"/RestApi/mcp/page-details?PageIdentifier={encoded}&format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<PageDetailsResponse>(ct)
            ?? new PageDetailsResponse();
    }

    public async Task<WidgetPropertiesResponse> GetWidgetPropertiesAsync(
        string widgetId, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(widgetId);
        var response = await client.GetAsync($"/RestApi/mcp/widgets/{encoded}/properties?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<WidgetPropertiesResponse>(ct)
            ?? new WidgetPropertiesResponse();
    }

    private HttpClient CreateClient(string? environment)
    {
        var (_, config) = this._resolver.Resolve(environment);
        var client = this._httpClientFactory.CreateClient("SitefinityPlugin");
        client.BaseAddress = new Uri(config.Url.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(30);

        if (!string.IsNullOrEmpty(config.SitefinityApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-MCP-API-Key");
            client.DefaultRequestHeaders.Add("X-MCP-API-Key", config.SitefinityApiKey);
        }

        return client;
    }
}

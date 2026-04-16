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

        return await response.Content.ReadFromJsonAsync<SiteInfoResponse>(SitefinityJsonOptions.Default, ct)
            ?? new SiteInfoResponse();
    }

    public async Task<IReadOnlyList<ModuleInfo>> ListModulesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/modules?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<List<ModuleInfo>>(SitefinityJsonOptions.Default, ct)
            ?? [];
    }

    public async Task<IReadOnlyList<DynamicTypeInfo>> ListDynamicTypesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/dynamic-types?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<List<DynamicTypeInfo>>(SitefinityJsonOptions.Default, ct)
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

        return await response.Content.ReadFromJsonAsync<List<DynamicFieldInfo>>(SitefinityJsonOptions.Default, ct)
            ?? [];
    }

    public async Task<ModuleStructureResponse> GetModuleStructureAsync(
        string moduleName, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(moduleName);
        var response = await client.GetAsync($"/RestApi/mcp/modules/{encoded}/structure?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ModuleStructureResponse>(SitefinityJsonOptions.Default, ct)
            ?? new ModuleStructureResponse { ModuleName = moduleName };
    }

    public async Task<PageRoutesResponse> ListPageRoutesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/page-routes?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<PageRoutesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new PageRoutesResponse();
    }

    public async Task<ApiRoutesResponse> ListApiRoutesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/api-routes?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ApiRoutesResponse>(SitefinityJsonOptions.Default, ct)
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

        return await response.Content.ReadFromJsonAsync<PageDetailsResponse>(SitefinityJsonOptions.Default, ct)
            ?? new PageDetailsResponse();
    }

    public async Task<WidgetPropertiesResponse> GetWidgetPropertiesAsync(
        string widgetId, string pageIdentifier, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encodedWidget = Uri.EscapeDataString(widgetId);
        var encodedPage = Uri.EscapeDataString(pageIdentifier);
        var response = await client.GetAsync($"/RestApi/mcp/widgets/{encodedWidget}/properties?PageIdentifier={encodedPage}&format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<WidgetPropertiesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new WidgetPropertiesResponse();
    }

    public async Task<ContentListResponse> ListContentAsync(
        string typeFullName, int take, int skip, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(typeFullName);
        var response = await client.GetAsync(
            $"/RestApi/mcp/content?TypeFullName={encoded}&Take={take}&Skip={skip}&format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ContentListResponse>(SitefinityJsonOptions.Default, ct)
            ?? new ContentListResponse { TypeFullName = typeFullName, Take = take, Skip = skip };
    }

    public async Task<TemplatesResponse> ListTemplatesAsync(
        string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/templates?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<TemplatesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new TemplatesResponse();
    }

    public async Task<TaxonomiesResponse> ListTaxonomiesAsync(
        string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/taxonomies?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<TaxonomiesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new TaxonomiesResponse();
    }

    public async Task<PageWidgetTreeResponse> GetPageWidgetTreeAsync(
        string pageIdentifier, bool includeLayoutControls, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(pageIdentifier);
        var response = await client.GetAsync(
            $"/RestApi/mcp/page-widget-tree?PageIdentifier={encoded}&IncludeLayoutControls={includeLayoutControls}&format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<PageWidgetTreeResponse>(SitefinityJsonOptions.Default, ct)
            ?? new PageWidgetTreeResponse();
    }

    public async Task<FormsResponse> ListFormsAsync(
        string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/forms?format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<FormsResponse>(SitefinityJsonOptions.Default, ct)
            ?? new FormsResponse();
    }

    public async Task<FormFieldsResponse> GetFormFieldsAsync(
        string formIdentifier, bool debug = false, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(formIdentifier);
        var debugQuery = debug ? "&Debug=true" : string.Empty;
        var response = await client.GetAsync($"/RestApi/mcp/forms/{encoded}/fields?format=json{debugQuery}", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<FormFieldsResponse>(SitefinityJsonOptions.Default, ct)
            ?? new FormFieldsResponse();
    }

    public async Task<FormResponsesResponse> ListFormResponsesAsync(
        string formIdentifier, int take, int skip, string? searchTerm = null, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(formIdentifier);
        var searchQuery = string.IsNullOrEmpty(searchTerm)
            ? string.Empty
            : $"&SearchTerm={Uri.EscapeDataString(searchTerm)}";
        var response = await client.GetAsync(
            $"/RestApi/mcp/forms/{encoded}/responses?Take={take}&Skip={skip}{searchQuery}&format=json", ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<FormResponsesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new FormResponsesResponse { FormId = formIdentifier, Take = take, Skip = skip };
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

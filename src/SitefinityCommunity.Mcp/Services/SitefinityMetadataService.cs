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
    private readonly IApiKeyValidationService? _apiKeyValidation;

    /// <summary>
    /// Creates the service. <paramref name="apiKeyValidation"/> is optional and used only to name the
    /// installed plugin's version in a 404 "plugin out of date" message; without it the message still
    /// carries the update steps, just not the version it is updating from.
    /// </summary>
    /// <param name="resolver">Environment resolver.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="apiKeyValidation">Ping cache holding the plugin's reported version.</param>
    public SitefinityMetadataService(
        IEnvironmentResolver resolver,
        IHttpClientFactory httpClientFactory,
        IApiKeyValidationService? apiKeyValidation = null)
    {
        this._resolver = resolver;
        this._httpClientFactory = httpClientFactory;
        this._apiKeyValidation = apiKeyValidation;
    }

    public async Task<ScheduledTaskStatusResponse> GetScheduledTaskStatusAsync(
        string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/scheduled-tasks?format=json", ct);

        await this.ThrowIfEndpointMissingAsync(response, "/RestApi/mcp/scheduled-tasks", environment, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ScheduledTaskStatusResponse>(SitefinityJsonOptions.Default, ct)
            ?? new ScheduledTaskStatusResponse();
    }

    public async Task<SearchIndexesResponse> GetSearchIndexesAsync(
        string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/search-indexes?format=json", ct);

        await this.ThrowIfEndpointMissingAsync(response, "/RestApi/mcp/search-indexes", environment, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<SearchIndexesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new SearchIndexesResponse();
    }

    /// <summary>
    /// A 404 from a plugin route means the site is up but this endpoint does not exist — the installed
    /// plugin sources are older than this server. The message names both versions when they are known
    /// and always carries the commands that fix it.
    /// </summary>
    /// <param name="response">Response to inspect.</param>
    /// <param name="endpoint">Route that was called.</param>
    /// <param name="environment">Environment name, or null for the default.</param>
    /// <param name="ct">Cancellation token.</param>
    private async Task ThrowIfEndpointMissingAsync(
        HttpResponseMessage response, string endpoint, string? environment, CancellationToken ct)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        string? pluginVersion = null;

        if (this._apiKeyValidation is not null)
        {
            try
            {
                pluginVersion = await this._apiKeyValidation.GetPluginVersionAsync(environment, ct);
            }
            catch (Exception)
            {
                // Advisory only — the update steps matter more than naming the stale version.
            }
        }

        throw new HttpRequestException(PluginVersionAdvisor.BuildNotFoundMessage(endpoint, pluginVersion));
    }

    public async Task<SiteInfoResponse> GetSiteInfoAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/site-info?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<SiteInfoResponse>(SitefinityJsonOptions.Default, ct)
            ?? new SiteInfoResponse();
    }

    public async Task<IReadOnlyList<ModuleInfo>> ListModulesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/modules?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<List<ModuleInfo>>(SitefinityJsonOptions.Default, ct)
            ?? [];
    }

    public async Task<IReadOnlyList<DynamicTypeInfo>> ListDynamicTypesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/dynamic-types?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ModuleStructureResponse>(SitefinityJsonOptions.Default, ct)
            ?? new ModuleStructureResponse { ModuleName = moduleName };
    }

    public async Task<PageRoutesResponse> ListPageRoutesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/page-routes?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<PageRoutesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new PageRoutesResponse();
    }

    public async Task<ApiRoutesResponse> ListApiRoutesAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/api-routes?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
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
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<FormResponsesResponse>(SitefinityJsonOptions.Default, ct)
            ?? new FormResponsesResponse { FormId = formIdentifier, Take = take, Skip = skip };
    }

    public async Task<ConfigSectionsResponse> ListConfigSectionsAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.GetAsync("/RestApi/mcp/config?format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ConfigSectionsResponse>(SitefinityJsonOptions.Default, ct)
            ?? new ConfigSectionsResponse();
    }

    public async Task<SettingsSearchResponse> SearchSettingsAsync(
        string query, int take = 0, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var url = $"/RestApi/mcp/settings/search?format=json&Query={Uri.EscapeDataString(query)}";

        if (take > 0)
        {
            url += $"&Take={take}";
        }

        var response = await client.GetAsync(url, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        var result = await response.Content.ReadFromJsonAsync<SettingsSearchResponse>(SitefinityJsonOptions.Default, ct)
            ?? new SettingsSearchResponse { Query = query };

        foreach (var hit in result.Results)
        {
            hit.Path = PrettifySettingPath(hit.Path);
        }

        return result;
    }

    /// <summary>
    /// Renders the index's internal FullSettingName as a readable breadcrumb. The raw value is a
    /// leaf-to-root chain of node keys separated by <c>fA==</c> (base64 of <c>|</c>), each key shaped
    /// like <c>ElementTypeName_MemberName</c> — e.g.
    /// <c>FieldDefinitionElement_HighContrastfA==FieldsfA==…fA==contentViewConfig</c> becomes
    /// <c>contentViewConfig > … > Fields > HighContrast</c>.
    /// </summary>
    private static string? PrettifySettingPath(string? rawPath)
    {
        if (string.IsNullOrEmpty(rawPath) || !rawPath.Contains("fA==", StringComparison.Ordinal))
        {
            return rawPath;
        }

        var segments = rawPath.Split("fA==", StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(segments); // leaf-to-root → root-to-leaf

        var parts = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            // Strip the element-type prefix ("FieldDefinitionElement_HighContrast" → "HighContrast");
            // keep segments without one (plain member names like "Fields") as-is.
            var underscore = segment.IndexOf('_');
            var display = underscore > 0 && segment[..underscore].EndsWith("Element", StringComparison.Ordinal)
                ? segment[(underscore + 1)..]
                : segment;

            if (display.Length > 0)
            {
                parts.Add(display);
            }
        }

        return string.Join(" > ", parts);
    }

    public async Task<ConfigSectionResponse> GetConfigSectionAsync(
        string sectionName,
        string? pathFilter = null,
        int maxEntries = 0,
        bool includeDefaults = false,
        string? environment = null,
        CancellationToken ct = default)
    {
        // Credential-like config values (keys, passwords, connection strings, [SecretData] / encrypted
        // properties) are ALWAYS redacted on the plugin side — there is intentionally no flag to reveal
        // them, in any environment. A raw secret in the LLM context is a leak.
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(sectionName);

        // Bounded by default. Sitefinity hands back a fully defaults-merged object graph, so an unbounded
        // dump of a section like ContentViewConfig is ~79 MB / 375k entries — large enough to kill the
        // stdio transport before the caller sees anything.
        var query = $"/RestApi/mcp/config/{encoded}?format=json";

        if (!string.IsNullOrWhiteSpace(pathFilter))
        {
            query += $"&PathFilter={Uri.EscapeDataString(pathFilter)}";
        }

        if (maxEntries > 0)
        {
            query += $"&MaxEntries={maxEntries}";
        }

        if (includeDefaults)
        {
            query += "&IncludeDefaults=true";
        }

        var response = await client.GetAsync(query, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<ConfigSectionResponse>(SitefinityJsonOptions.Default, ct)
            ?? new ConfigSectionResponse { SectionName = sectionName };
    }

    public async Task<WhereUsedResponse> WhereUsedAsync(
        string query, string? kind = null, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(query);
        var kindQuery = string.IsNullOrEmpty(kind) ? string.Empty : $"&Kind={Uri.EscapeDataString(kind)}";
        var response = await client.GetAsync($"/RestApi/mcp/where-used?Query={encoded}{kindQuery}&format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<WhereUsedResponse>(SitefinityJsonOptions.Default, ct)
            ?? new WhereUsedResponse { Query = query };
    }

    public async Task<PermissionsResponse> GetPermissionsAsync(
        string identifier, string? typeFullName = null, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var encoded = Uri.EscapeDataString(identifier);
        var typeQuery = string.IsNullOrEmpty(typeFullName)
            ? string.Empty
            : $"&TypeFullName={Uri.EscapeDataString(typeFullName)}";
        var response = await client.GetAsync($"/RestApi/mcp/permissions?Identifier={encoded}{typeQuery}&format=json", ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<PermissionsResponse>(SitefinityJsonOptions.Default, ct)
            ?? new PermissionsResponse { Target = identifier };
    }

    public async Task<MaintenanceResponse> ClearCacheAsync(
        string? scope = null, string? pageIdentifier = null, string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var scopeQuery = string.IsNullOrEmpty(scope) ? string.Empty : $"&Scope={Uri.EscapeDataString(scope)}";
        var pageQuery = string.IsNullOrEmpty(pageIdentifier)
            ? string.Empty
            : $"&PageIdentifier={Uri.EscapeDataString(pageIdentifier)}";
        var response = await client.PostAsync($"/RestApi/mcp/cache/clear?format=json{scopeQuery}{pageQuery}", null, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<MaintenanceResponse>(SitefinityJsonOptions.Default, ct)
            ?? new MaintenanceResponse { Operation = "clear-cache" };
    }

    public async Task<MaintenanceResponse> RecycleApplicationAsync(string? environment = null, CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var response = await client.PostAsync("/RestApi/mcp/app/recycle?format=json", null, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<MaintenanceResponse>(SitefinityJsonOptions.Default, ct)
            ?? new MaintenanceResponse { Operation = "recycle" };
    }

    public async Task<IncidentResponse> GetIncidentWindowAsync(
        string? center = null,
        int windowMinutes = 0,
        int lookbackHours = 0,
        string? query = null,
        string? sources = null,
        string? environment = null,
        CancellationToken ct = default)
    {
        var client = CreateClient(environment);
        var url = "/RestApi/mcp/incident-window?format=json";

        if (!string.IsNullOrWhiteSpace(center))
        {
            url += $"&Center={Uri.EscapeDataString(center)}";
        }

        if (windowMinutes > 0)
        {
            url += $"&WindowMinutes={windowMinutes}";
        }

        if (lookbackHours > 0)
        {
            url += $"&LookbackHours={lookbackHours}";
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            url += $"&Query={Uri.EscapeDataString(query)}";
        }

        if (!string.IsNullOrWhiteSpace(sources))
        {
            url += $"&Sources={Uri.EscapeDataString(sources)}";
        }

        var response = await client.GetAsync(url, ct);

        await this.ThrowIfEndpointMissingAsync(response, "/RestApi/mcp/incident-window", environment, ct);
        await response.EnsureCapabilityEnabledAsync(ct);
        response.EnsureSuccessStatusCode();
        response.EnsureNotBootstrapping();

        return await response.Content.ReadFromJsonAsync<IncidentResponse>(SitefinityJsonOptions.Default, ct)
            ?? new IncidentResponse { Mode = "window" };
    }

    private HttpClient CreateClient(string? environment)
    {
        var (_, config) = this._resolver.Resolve(environment);
        var client = this._httpClientFactory.CreateClient("SitefinityPlugin");
        client.BaseAddress = new Uri(config.Url.TrimEnd('/'));
        // where-used scans every page + template, so allow generous headroom over the normal sub-second
        // metadata calls (cold app-pool start on a large site can still take a while on the first hit).
        client.Timeout = TimeSpan.FromSeconds(120);

        if (!string.IsNullOrEmpty(config.SitefinityApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-MCP-API-Key");
            client.DefaultRequestHeaders.Add("X-MCP-API-Key", config.SitefinityApiKey);
        }

        return client;
    }
}

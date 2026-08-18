using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Fetches Sitefinity instance metadata via the companion plugin REST endpoints.
/// </summary>
public interface ISitefinityMetadataService
{
    Task<SiteInfoResponse> GetSiteInfoAsync(string? environment = null, CancellationToken ct = default);
    Task<IReadOnlyList<ModuleInfo>> ListModulesAsync(string? environment = null, CancellationToken ct = default);
    Task<IReadOnlyList<DynamicTypeInfo>> ListDynamicTypesAsync(string? environment = null, CancellationToken ct = default);
    Task<IReadOnlyList<DynamicFieldInfo>> GetTypeFieldsAsync(string typeFullName, string? environment = null, CancellationToken ct = default);
    Task<ModuleStructureResponse> GetModuleStructureAsync(string moduleName, string? environment = null, CancellationToken ct = default);
    Task<PageRoutesResponse> ListPageRoutesAsync(string? environment = null, CancellationToken ct = default);
    Task<ApiRoutesResponse> ListApiRoutesAsync(string? environment = null, CancellationToken ct = default);
    Task<PageDetailsResponse> GetPageDetailsAsync(string pageIdentifier, string? environment = null, CancellationToken ct = default);
    Task<WidgetPropertiesResponse> GetWidgetPropertiesAsync(string widgetId, string pageIdentifier, string? environment = null, CancellationToken ct = default);
    Task<ContentListResponse> ListContentAsync(string typeFullName, int take, int skip, string? environment = null, CancellationToken ct = default);
    Task<TemplatesResponse> ListTemplatesAsync(string? environment = null, CancellationToken ct = default);
    Task<TaxonomiesResponse> ListTaxonomiesAsync(string? environment = null, CancellationToken ct = default);
    Task<PageWidgetTreeResponse> GetPageWidgetTreeAsync(string pageIdentifier, bool includeLayoutControls, string? environment = null, CancellationToken ct = default);
    Task<FormsResponse> ListFormsAsync(string? environment = null, CancellationToken ct = default);
    Task<FormFieldsResponse> GetFormFieldsAsync(string formIdentifier, bool debug = false, string? environment = null, CancellationToken ct = default);
    Task<FormResponsesResponse> ListFormResponsesAsync(string formIdentifier, int take, int skip, string? searchTerm = null, string? environment = null, CancellationToken ct = default);
    Task<ConfigSectionsResponse> ListConfigSectionsAsync(string? environment = null, CancellationToken ct = default);
    Task<SettingsSearchResponse> SearchSettingsAsync(
        string query, int take = 0, string? environment = null, CancellationToken ct = default);

    Task<ConfigSectionResponse> GetConfigSectionAsync(
        string sectionName,
        string? pathFilter = null,
        int maxEntries = 0,
        bool includeDefaults = false,
        string? environment = null,
        CancellationToken ct = default);
    Task<WhereUsedResponse> WhereUsedAsync(string query, string? kind = null, string? environment = null, CancellationToken ct = default);
    Task<PermissionsResponse> GetPermissionsAsync(string identifier, string? typeFullName = null, string? environment = null, CancellationToken ct = default);
    Task<MaintenanceResponse> ClearCacheAsync(string? scope = null, string? pageIdentifier = null, string? environment = null, CancellationToken ct = default);
    Task<MaintenanceResponse> RecycleApplicationAsync(string? environment = null, CancellationToken ct = default);
}

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
    Task<RoutesResponse> ListRoutesAsync(string? environment = null, CancellationToken ct = default);
}

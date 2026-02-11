using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Checks whether Sitefinity is bootstrapped and ready to serve requests.
/// </summary>
public interface ISitefinityStatusService
{
    Task<SitefinityHealthResponse> CheckStatusAsync(string? environmentName = null, CancellationToken ct = default);
}

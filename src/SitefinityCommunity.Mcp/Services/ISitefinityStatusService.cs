using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Checks whether Sitefinity is bootstrapped and ready to serve requests.
/// </summary>
public interface ISitefinityStatusService
{
    Task<SitefinityHealthResponse> CheckStatusAsync(string? environmentName = null, CancellationToken ct = default);

    /// <summary>
    /// Polls CheckStatusAsync until Sitefinity is ready or the timeout is reached.
    /// Logs each retry attempt to stderr for visibility.
    /// </summary>
    Task<SitefinityHealthResponse> WaitForReadyAsync(
        string? environmentName = null,
        int maxWaitSeconds = 90,
        int pollIntervalSeconds = 5,
        CancellationToken ct = default);
}

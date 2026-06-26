namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Result of a state-changing maintenance operation (cache clear or app recycle).
/// </summary>
public sealed class MaintenanceResponse
{
    /// <summary>The operation that ran, e.g. "clear-cache" or "recycle".</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>True when the operation was carried out.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable detail (what was cleared, scope, caveats about timing).</summary>
    public string Message { get; set; } = string.Empty;

    public List<string> Warnings { get; set; } = [];
}

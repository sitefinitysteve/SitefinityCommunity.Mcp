namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Result of a Sitefinity health check.
/// </summary>
public sealed class SitefinityHealthResponse
{
    public bool IsReady { get; set; }
    public bool IsBootstrapping { get; set; }
    public bool IsUnreachable { get; set; }
    public string Summary { get; set; } = string.Empty;

    public static SitefinityHealthResponse Ready() => new()
    {
        IsReady = true,
        Summary = "Sitefinity is bootstrapped and ready."
    };

    public static SitefinityHealthResponse Bootstrapping() => new()
    {
        IsBootstrapping = true,
        Summary = "Sitefinity is still bootstrapping. Please wait and try again."
    };

    public static SitefinityHealthResponse Unreachable(string reason) => new()
    {
        IsUnreachable = true,
        Summary = $"Sitefinity is unreachable: {reason}"
    };
}

namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Installed Sitefinity module metadata.
/// </summary>
public sealed class ModuleInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string StartupType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

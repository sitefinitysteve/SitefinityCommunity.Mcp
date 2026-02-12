namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Sitefinity instance metadata — version, project name, multisite info.
/// </summary>
public sealed class SiteInfoResponse
{
    public string SitefinityVersion { get; set; } = string.Empty;
    public string DotNetVersion { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public int ModuleCount { get; set; }
    public List<string> Languages { get; set; } = [];
    public List<SiteEntry> Sites { get; set; } = [];
}

/// <summary>
/// A single site in a multisite configuration.
/// </summary>
public sealed class SiteEntry
{
    public string Name { get; set; } = string.Empty;
    public string LiveUrl { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

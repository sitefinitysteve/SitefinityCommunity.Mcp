namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Summary of a Sitefinity classification (taxonomy).
/// </summary>
public sealed class TaxonomyInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string TaxonomyType { get; set; } = string.Empty;
    public int TaxaCount { get; set; }
}

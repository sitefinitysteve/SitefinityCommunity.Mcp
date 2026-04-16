namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A single taxon within a taxonomy (category value, tag, etc.).
/// </summary>
public sealed class TaxonInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? ParentId { get; set; }
}

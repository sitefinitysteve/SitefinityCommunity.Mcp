namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Response listing taxonomies and a sample of their top-level taxa.
/// </summary>
public sealed class TaxonomiesResponse
{
    public List<TaxonomyInfo> Taxonomies { get; set; } = [];

    /// <summary>
    /// Top-level taxa keyed by taxonomy Id (capped per taxonomy to limit response size).
    /// </summary>
    public Dictionary<string, List<TaxonInfo>> Taxa { get; set; } = [];

    public List<string> Warnings { get; set; } = [];
}

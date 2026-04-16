namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A paged list of content items for a given Sitefinity type.
/// </summary>
public sealed class ContentListResponse
{
    public string TypeFullName { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int Take { get; set; }
    public int Skip { get; set; }
    public List<ContentItemInfo> Items { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

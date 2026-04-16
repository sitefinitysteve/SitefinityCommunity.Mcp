namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Summary of a single content item (news, blog post, dynamic type item, etc.).
/// </summary>
public sealed class ContentItemInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string UrlName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DateCreated { get; set; }
    public DateTime? LastModified { get; set; }
    public string ContentType { get; set; } = string.Empty;
}

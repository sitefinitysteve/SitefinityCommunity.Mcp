namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Metadata for a single CMS page template.
/// </summary>
public sealed class PageTemplateInfo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string? ParentTemplateId { get; set; }
    public string Culture { get; set; } = string.Empty;
}

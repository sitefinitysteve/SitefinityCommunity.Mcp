namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Detailed information about a specific CMS page including widgets and their properties.
/// </summary>
public sealed class PageDetailsResponse
{
    public string Id { get; set; } = string.Empty;
    public string PageDataId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string UrlName { get; set; } = string.Empty;
    public string NodeType { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Depth { get; set; }
    public List<PageWidgetInfo> Widgets { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// A widget (control) placed on a CMS page with all its configuration properties.
/// </summary>
public sealed class PageWidgetInfo
{
    public string Id { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string WidgetName { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string PlaceHolder { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public bool IsLayoutControl { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
    public Dictionary<string, string> SettingsProperties { get; set; } = [];
}

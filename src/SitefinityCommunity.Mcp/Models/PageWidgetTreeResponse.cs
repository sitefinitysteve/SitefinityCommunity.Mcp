namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A full page composition rendered as a tree: top-level placeholders contain widgets in render order,
/// and layout controls own child placeholders named "{ControlId}_Col00", "_Col01", etc.
/// </summary>
public sealed class PageWidgetTreeResponse
{
    public string PageId { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public string PageUrl { get; set; } = string.Empty;
    public string TemplateId { get; set; } = string.Empty;
    public List<PlaceholderNode> Placeholders { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// One placeholder (named slot) on the page or inside a layout control,
/// holding widgets in their rendered sibling order.
/// </summary>
public sealed class PlaceholderNode
{
    public string Name { get; set; } = string.Empty;
    public List<WidgetNode> Widgets { get; set; } = [];
}

/// <summary>
/// A widget in the tree. Properties is a merged view of Level 1 (ORM ControlData) + Level 2 (Settings children),
/// with Level 2 winning on conflict (what the widget designer actually saved).
/// </summary>
public sealed class WidgetNode
{
    public string Id { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ControllerName { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string PlaceHolder { get; set; } = string.Empty;
    public bool IsLayoutControl { get; set; }
    public string SiblingId { get; set; } = string.Empty;
    public int RenderOrder { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
    public List<PlaceholderNode> Children { get; set; } = [];
}

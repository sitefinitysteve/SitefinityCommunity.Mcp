namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Reverse-lookup result: every place a widget type, content item, or page template is referenced
/// across the site's pages and templates.
/// </summary>
public sealed class WhereUsedResponse
{
    /// <summary>The identifier that was searched (Guid, widget type name, or template name).</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>
    /// How the query was interpreted: "widgetType", "contentItem", "template", or "unknown".
    /// </summary>
    public string ResolvedKind { get; set; } = string.Empty;

    /// <summary>Human-readable description of what was resolved (e.g. the template's title).</summary>
    public string ResolvedTitle { get; set; } = string.Empty;

    /// <summary>Total number of usages found.</summary>
    public int TotalUsages { get; set; }

    public List<WhereUsedItem> Usages { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// A single usage of the searched item.
/// </summary>
public sealed class WhereUsedItem
{
    /// <summary>"page" or "template" — the kind of host the reference lives on.</summary>
    public string HostKind { get; set; } = string.Empty;

    /// <summary>Id of the page or template that contains the reference.</summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>Title of the host page or template.</summary>
    public string HostTitle { get; set; } = string.Empty;

    /// <summary>Full URL of the host page (empty for templates).</summary>
    public string HostUrl { get; set; } = string.Empty;

    /// <summary>Id of the specific widget/control carrying the reference, when applicable.</summary>
    public string WidgetId { get; set; } = string.Empty;

    /// <summary>Friendly widget name / controller, when the reference is inside a widget.</summary>
    public string WidgetName { get; set; } = string.Empty;

    /// <summary>Why this counts as a usage (e.g. "ControllerName matches", "SharedContentID references item", "Page uses template").</summary>
    public string MatchReason { get; set; } = string.Empty;
}

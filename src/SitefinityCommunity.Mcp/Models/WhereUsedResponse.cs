namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Reverse-lookup result: every place a widget type, content item, page template, or arbitrary
/// property value is referenced across the site's pages AND templates. Because a widget living on a
/// template implicitly renders on every page that rides that template, template-hosted matches are
/// expanded into the affected pages (transitively through template inheritance) — so this answers
/// "what breaks if I change this?".
/// </summary>
public sealed class WhereUsedResponse
{
    /// <summary>The identifier that was searched (Guid, widget type name, template name, or substring).</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>How the query was interpreted: "widget", "content", "template", or "property".</summary>
    public string ResolvedKind { get; set; } = string.Empty;

    /// <summary>Human-readable description of what was resolved (e.g. the template's title).</summary>
    public string ResolvedTitle { get; set; } = string.Empty;

    /// <summary>Total number of usages reported (size of <see cref="Usages"/>).</summary>
    public int TotalUsages { get; set; }

    /// <summary>Usages where the match lives directly on a page.</summary>
    public int PageUsageCount { get; set; }

    /// <summary>Usages where the match lives on a template.</summary>
    public int TemplateUsageCount { get; set; }

    /// <summary>Pages reported because the match lives on a template they ride (not on the page itself).</summary>
    public int InheritedPageCount { get; set; }

    /// <summary>How many pages were scanned.</summary>
    public int ScannedPages { get; set; }

    /// <summary>How many templates were scanned.</summary>
    public int ScannedTemplates { get; set; }

    /// <summary>Hosts skipped because they could not be read.</summary>
    public int SkippedHosts { get; set; }

    public List<WhereUsedItem> Usages { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>A single usage of the searched item.</summary>
public sealed class WhereUsedItem
{
    /// <summary>"page" or "template" — the kind of host the reference lives on.</summary>
    public string HostKind { get; set; } = string.Empty;

    /// <summary>Id of the page or template that contains the reference.</summary>
    public string HostId { get; set; } = string.Empty;

    /// <summary>Title of the host page or template.</summary>
    public string HostTitle { get; set; } = string.Empty;

    /// <summary>Site URL of the host page (empty for templates).</summary>
    public string HostUrl { get; set; } = string.Empty;

    /// <summary>Id of the specific widget/control carrying the reference, when applicable.</summary>
    public string WidgetId { get; set; } = string.Empty;

    /// <summary>Friendly widget name, when the reference is inside a widget.</summary>
    public string WidgetName { get; set; } = string.Empty;

    /// <summary>The widget's controller type (for MVC widgets), when known.</summary>
    public string ControllerName { get; set; } = string.Empty;

    /// <summary>The widget's raw object type.</summary>
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>"medportal", "sitefinity", or "unknown" — provenance of the matched widget.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>The placeholder the widget sits in on its host.</summary>
    public string PlaceHolder { get; set; } = string.Empty;

    /// <summary>Why this counts as a usage.</summary>
    public string MatchReason { get; set; } = string.Empty;

    /// <summary>For content/property matches: the property whose value matched.</summary>
    public string MatchedProperty { get; set; } = string.Empty;

    /// <summary>For content/property matches: a short snippet of the value around the match.</summary>
    public string MatchSnippet { get; set; } = string.Empty;

    /// <summary>Set when this page usage is inherited from a widget that actually lives on a template.</summary>
    public string ViaTemplateId { get; set; } = string.Empty;

    /// <summary>Title of the template the usage is inherited from, when applicable.</summary>
    public string ViaTemplateTitle { get; set; } = string.Empty;
}

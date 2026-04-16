namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A CMS page route from the Sitefinity page tree.
/// </summary>
public sealed class PageRoute
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public List<string> AdditionalUrls { get; set; } = [];
    public string NodeType { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public int Depth { get; set; }
    public bool HasUrlEvaluation { get; set; }
    public string UrlEvaluationMode { get; set; } = string.Empty;
}

/// <summary>
/// A ServiceStack API route registered with the application host.
/// </summary>
public sealed class ApiRoute
{
    public string Path { get; set; } = string.Empty;
    public string Verbs { get; set; } = string.Empty;
    public string RequestType { get; set; } = string.Empty;
}

/// <summary>
/// Response containing only CMS page routes and URL evaluation warnings.
/// </summary>
public sealed class PageRoutesResponse
{
    public List<PageRoute> PageRoutes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Response containing ServiceStack API routes and OData entity sets.
/// </summary>
public sealed class ApiRoutesResponse
{
    public List<ApiRoute> ServiceStackRoutes { get; set; } = [];
    public List<ODataRoute> ODataRoutes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// An OData entity set exposed by Sitefinity's Web API at /api/default.
/// </summary>
public sealed class ODataRoute
{
    public string EntitySetName { get; set; } = string.Empty;
    public string EntitySetUrl { get; set; } = string.Empty;
}

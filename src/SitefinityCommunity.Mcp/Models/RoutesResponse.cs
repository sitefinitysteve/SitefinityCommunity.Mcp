namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Composite response containing page routes, API routes, and diagnostic warnings.
/// </summary>
public sealed class RoutesResponse
{
    public List<PageRoute> PageRoutes { get; set; } = [];
    public List<ApiRoute> ApiRoutes { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// A CMS page route from the Sitefinity page tree.
/// </summary>
public sealed class PageRoute
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
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

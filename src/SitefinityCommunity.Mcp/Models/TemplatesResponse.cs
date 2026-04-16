namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Response listing all available CMS page templates.
/// </summary>
public sealed class TemplatesResponse
{
    public List<PageTemplateInfo> Templates { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

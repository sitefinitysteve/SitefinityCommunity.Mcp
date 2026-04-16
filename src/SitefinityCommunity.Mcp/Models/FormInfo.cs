namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Summary of a Sitefinity form.
/// </summary>
public sealed class FormInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public int FieldCount { get; set; }
    public int EntryCount { get; set; }
    public DateTime? LastModified { get; set; }
}

/// <summary>
/// Response listing all forms.
/// </summary>
public sealed class FormsResponse
{
    public List<FormInfo> Forms { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

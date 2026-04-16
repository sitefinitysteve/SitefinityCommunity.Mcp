namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Field definition for a Module Builder dynamic content type.
/// </summary>
public sealed class DynamicFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string ClrType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public bool IsMainField { get; set; }
    public string ClassificationName { get; set; } = string.Empty;
    public string RelatedDataType { get; set; } = string.Empty;
}

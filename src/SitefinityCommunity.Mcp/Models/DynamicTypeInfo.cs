namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Module Builder dynamic content type metadata.
/// </summary>
public sealed class DynamicTypeInfo
{
    public string ModuleName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
    public int FieldCount { get; set; }
}

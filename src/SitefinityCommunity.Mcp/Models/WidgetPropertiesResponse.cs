namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Full widget details including both Level 1 properties and Level 2 Settings children.
/// </summary>
public sealed class WidgetPropertiesResponse
{
    public string WidgetId { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string PlaceHolder { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public bool IsLayoutControl { get; set; }
    public Dictionary<string, string> Properties { get; set; } = [];
    public Dictionary<string, string> SettingsProperties { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

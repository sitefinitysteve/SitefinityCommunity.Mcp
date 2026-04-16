namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A single field on a Sitefinity form.
/// </summary>
public sealed class FormFieldInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public bool IsRequired { get; set; }
    public string PlaceHolder { get; set; } = string.Empty;
    public string DefaultValue { get; set; } = string.Empty;
    public List<string> Choices { get; set; } = [];
}

/// <summary>
/// Response listing the field definitions for one form.
/// </summary>
public sealed class FormFieldsResponse
{
    public string FormId { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;
    public string FormTitle { get; set; } = string.Empty;
    public List<FormFieldInfo> Fields { get; set; } = [];
    public List<string> Warnings { get; set; } = [];

    /// <summary>
    /// Populated only when the caller passes <c>debug: true</c>. Raw text dump of the form's
    /// full Properties + ChildProperties tree — intended for troubleshooting empty field
    /// Name/Title on unfamiliar Sitefinity versions by pasting the output back to a maintainer.
    /// </summary>
    public string? DebugDump { get; set; }
}

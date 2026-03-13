using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace SitefinityCommunity.Mcp.Resources;

[McpServerResourceType]
public sealed class SitefinityDocsResources
{
    private static readonly Lazy<string> WidgetDesignerAttributesContent = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("SitefinityCommunity.Mcp.Docs.WidgetDesignerAttributes.md")
            ?? throw new InvalidOperationException("Embedded resource 'Docs/WidgetDesignerAttributes.md' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    [McpServerResource(
        Name = "sitefinity_widget_designer_attributes",
        Title = "Sitefinity Widget Designer Attributes Reference",
        MimeType = "text/markdown")]
    [Description(
        "Complete reference for all Sitefinity widget designer attributes — field types, display attributes, " +
        "validation, sections, conditional visibility, content selectors, color palettes, complex objects, " +
        "TableView, choice fields, LinkModel, media items, KnownFieldTypes, and PropertyCategory. " +
        "Read this resource before building or modifying Sitefinity widget property editors.")]
    public static string GetWidgetDesignerAttributes() => WidgetDesignerAttributesContent.Value;
}

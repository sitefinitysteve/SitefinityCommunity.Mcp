using System.Reflection;
using SitefinityCommunity.Mcp.Resources;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class SitefinityDocsResourcesTests
{
    [Fact]
    public void EmbeddedResource_Exists()
    {
        var assembly = typeof(SitefinityDocsResources).Assembly;
        using var stream = assembly.GetManifestResourceStream("SitefinityCommunity.Mcp.Docs.WidgetDesignerAttributes.md");

        Assert.NotNull(stream);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void GetWidgetDesignerAttributes_ReturnsMarkdownContent()
    {
        var result = SitefinityDocsResources.GetWidgetDesignerAttributes();

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.StartsWith("# Sitefinity Widget Designer Attributes Reference", result);
    }

    [Fact]
    public void GetWidgetDesignerAttributes_ContainsExpectedSections()
    {
        var result = SitefinityDocsResources.GetWidgetDesignerAttributes();

        Assert.Contains("## Field Types", result);
        Assert.Contains("## Display Attributes", result);
        Assert.Contains("## Validation Attributes", result);
        Assert.Contains("## Conditional Visibility", result);
        Assert.Contains("## Content Selectors", result);
        Assert.Contains("## KnownFieldTypes Reference", result);
        Assert.Contains("## Links (LinkModel)", result);
    }

    [Fact]
    public void GetWidgetDesignerAttributes_ReturnsSameInstanceOnMultipleCalls()
    {
        var result1 = SitefinityDocsResources.GetWidgetDesignerAttributes();
        var result2 = SitefinityDocsResources.GetWidgetDesignerAttributes();

        Assert.Same(result1, result2);
    }

    [Fact]
    public void ResourceClass_HasMcpServerResourceTypeAttribute()
    {
        var attr = typeof(SitefinityDocsResources)
            .GetCustomAttribute(typeof(ModelContextProtocol.Server.McpServerResourceTypeAttribute));

        Assert.NotNull(attr);
    }

    [Fact]
    public void GetWidgetDesignerAttributes_HasMcpServerResourceAttribute()
    {
        var method = typeof(SitefinityDocsResources)
            .GetMethod(nameof(SitefinityDocsResources.GetWidgetDesignerAttributes));

        Assert.NotNull(method);

        var attr = method.GetCustomAttribute(typeof(ModelContextProtocol.Server.McpServerResourceAttribute));
        Assert.NotNull(attr);
    }
}

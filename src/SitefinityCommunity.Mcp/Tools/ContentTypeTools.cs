using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

[McpServerToolType]
public sealed class ContentTypeTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public ContentTypeTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_list_dynamic_types", ReadOnly = true)]
    [Description("List all Module Builder dynamic content types grouped by module. Shows the CLR type name and field count for each type.")]
    public async Task<string> ListDynamicTypes(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var types = await this._metadataService.ListDynamicTypesAsync(environment, ct);

            if (types.Count == 0)
                return "No dynamic types found. The Module Builder may not have any active modules.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {types.Count} dynamic type(s):");
            sb.AppendLine();

            var grouped = types.GroupBy(t => t.ModuleName).OrderBy(g => g.Key);
            foreach (var group in grouped)
            {
                sb.AppendLine($"── {group.Key} ──");
                foreach (var type in group.OrderBy(t => t.TypeName))
                {
                    sb.AppendLine($"  {type.TypeName} ({type.FieldCount} fields)");
                    sb.AppendLine($"    CLR type: {type.TypeFullName}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error listing dynamic types: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error listing dynamic types: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_get_type_fields", ReadOnly = true)]
    [Description("Get all fields for a specific dynamic content type. Shows field name, type, required/main flags, and taxonomy/related type info. Use sitefinity_list_dynamic_types first to get the CLR type name.")]
    public async Task<string> GetTypeFields(
        [Description("Full CLR type name of the dynamic type (e.g., 'Telerik.Sitefinity.DynamicTypes.Model.PressReleases.PressRelease')")] string typeFullName,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var fields = await this._metadataService.GetTypeFieldsAsync(typeFullName, environment, ct);

            if (fields.Count == 0)
                return $"No fields found for type: {typeFullName}";

            var sb = new StringBuilder();
            sb.AppendLine($"Fields for {typeFullName} ({fields.Count} fields):");
            sb.AppendLine();

            foreach (var field in fields)
            {
                var flags = new List<string>();
                if (field.IsRequired) flags.Add("required");
                if (field.IsMainField) flags.Add("main field");
                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";

                sb.AppendLine($"  {field.Name}{flagStr}");
                sb.AppendLine($"    Title: {field.Title}");
                sb.AppendLine($"    Type:  {field.FieldType}");

                if (!string.IsNullOrEmpty(field.ClassificationName))
                    sb.AppendLine($"    Taxonomy: {field.ClassificationName}");

                if (!string.IsNullOrEmpty(field.RelatedDataType))
                    sb.AppendLine($"    Related type: {field.RelatedDataType}");

                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error fetching type fields: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error fetching type fields: {ex.Message}";
        }
    }
}

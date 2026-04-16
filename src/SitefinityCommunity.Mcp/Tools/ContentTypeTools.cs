using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools exposing Sitefinity Module Builder metadata: the list of dynamic types, their field
/// definitions, and the full nested module structure useful for generating POCOs.
/// </summary>
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
            {
                return "No dynamic types found. The Module Builder may not have any active modules.";
            }

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

    [McpServerTool(Name = "sitefinity_get_module_structure", ReadOnly = true)]
    [Description("Get the full structure of a Module Builder module: every type nested by parent/child, " +
                 "every field on every type with CLR type hints. Ideal input for generating a POCO, a widget " +
                 "view-model, or any code that needs to bind to the module's shape in one call.")]
    public async Task<string> GetModuleStructure(
        [Description("Module name as shown in Admin > Modules (e.g., 'Session', 'Community Groups')")] string moduleName,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var structure = await this._metadataService.GetModuleStructureAsync(moduleName, environment, ct);

            if (structure.RootTypes.Count == 0)
            {
                return $"No types found for module: {moduleName}. " +
                       "Verify the module name exists (see sitefinity_list_dynamic_types).";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Module: {structure.ModuleTitle} ({structure.ModuleName})");
            sb.AppendLine();

            foreach (var root in structure.RootTypes)
            {
                AppendTypeNode(sb, root, indent: 0);
            }

            if (structure.Warnings.Count > 0)
            {
                sb.AppendLine("Warnings:");
                foreach (var w in structure.Warnings)
                {
                    sb.AppendLine($"  - {w}");
                }
            }

            return sb.ToString();
        }
        catch (HttpRequestException ex)
        {
            return $"Error fetching module structure: {ex.Message}. Ensure the Sitefinity plugin is installed and the site is running.";
        }
        catch (Exception ex)
        {
            return $"Error fetching module structure: {ex.Message}";
        }
    }

    private static void AppendTypeNode(StringBuilder sb, Models.DynamicTypeNode node, int indent)
    {
        var pad = new string(' ', indent * 2);
        sb.AppendLine($"{pad}── {node.TypeName}");
        sb.AppendLine($"{pad}   CLR: {node.TypeFullName}");

        if (node.Fields.Count == 0)
        {
            sb.AppendLine($"{pad}   (no fields)");
        }
        else
        {
            sb.AppendLine($"{pad}   Fields:");
            foreach (var f in node.Fields)
            {
                var flags = new List<string>();

                if (f.IsRequired)
                {
                    flags.Add("required");
                }

                if (f.IsMainField)
                {
                    flags.Add("main");
                }

                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";
                var clr = !string.IsNullOrEmpty(f.ClrType) ? f.ClrType : f.FieldType;
                sb.AppendLine($"{pad}     {f.Name} : {clr}{flagStr}");
                if (!string.IsNullOrEmpty(f.RelatedDataType))
                {
                    sb.AppendLine($"{pad}       → related: {f.RelatedDataType}");
                }
                if (!string.IsNullOrEmpty(f.ClassificationName))
                {
                    sb.AppendLine($"{pad}       → taxonomy: {f.ClassificationName}");
                }
            }
        }

        sb.AppendLine();
        foreach (var child in node.ChildTypes)
        {
            AppendTypeNode(sb, child, indent + 1);
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
            {
                return $"No fields found for type: {typeFullName}";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Fields for {typeFullName} ({fields.Count} fields):");
            sb.AppendLine();

            foreach (var field in fields)
            {
                var flags = new List<string>();

                if (field.IsRequired)
                {
                    flags.Add("required");
                }

                if (field.IsMainField)
                {
                    flags.Add("main field");
                }

                var flagStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : "";

                sb.AppendLine($"  {field.Name}{flagStr}");
                sb.AppendLine($"    Title: {field.Title}");
                sb.AppendLine($"    Type:  {field.FieldType}");

                if (!string.IsNullOrEmpty(field.ClassificationName))
                {
                    sb.AppendLine($"    Taxonomy: {field.ClassificationName}");
                }

                if (!string.IsNullOrEmpty(field.RelatedDataType))
                {
                    sb.AppendLine($"    Related type: {field.RelatedDataType}");
                }

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

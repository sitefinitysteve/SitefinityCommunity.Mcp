namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Full structural snapshot of a Module Builder module: every type nested by parent/child,
/// every field on every type. Intended for code-generation workflows where the LLM needs
/// to see the complete shape of a module in one call.
/// </summary>
public sealed class ModuleStructureResponse
{
    public string ModuleName { get; set; } = string.Empty;
    public string ModuleTitle { get; set; } = string.Empty;
    public List<DynamicTypeNode> RootTypes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// One type in a module, with its fields and any child types nested beneath it.
/// </summary>
public sealed class DynamicTypeNode
{
    public string TypeName { get; set; } = string.Empty;
    public string TypeFullName { get; set; } = string.Empty;
    public string? ParentTypeName { get; set; }
    public List<DynamicFieldInfo> Fields { get; set; } = new();
    public List<DynamicTypeNode> ChildTypes { get; set; } = new();
}

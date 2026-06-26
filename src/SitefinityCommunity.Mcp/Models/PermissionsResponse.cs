namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Effective permissions for a securable Sitefinity object (a page node or a content item),
/// resolved per role across each permission set.
/// </summary>
public sealed class PermissionsResponse
{
    /// <summary>The identifier that was inspected.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>"page" or "content".</summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>Title/name of the resolved object.</summary>
    public string TargetTitle { get; set; } = string.Empty;

    /// <summary>
    /// True when the object inherits permissions from its parent (i.e. has no explicit overrides).
    /// When true the <see cref="Permissions"/> list reflects inherited grants.
    /// </summary>
    public bool InheritsPermissions { get; set; }

    public List<PermissionEntry> Permissions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// The granted/denied actions for one role within one permission set.
/// </summary>
public sealed class PermissionEntry
{
    /// <summary>The permission set, e.g. "Pages" or "Security".</summary>
    public string PermissionSetName { get; set; } = string.Empty;

    /// <summary>The principal (role) Guid the grant applies to.</summary>
    public string RoleId { get; set; } = string.Empty;

    /// <summary>Resolved role name (e.g. "Administrators", "Anonymous"), or the raw id if unresolved.</summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>Actions explicitly granted (e.g. "View", "Modify", "Delete", "Create", "ChangePermissions").</summary>
    public List<string> GrantedActions { get; set; } = [];

    /// <summary>Actions explicitly denied.</summary>
    public List<string> DeniedActions { get; set; } = [];
}

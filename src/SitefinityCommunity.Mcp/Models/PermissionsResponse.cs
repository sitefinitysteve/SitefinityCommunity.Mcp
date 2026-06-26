namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Effective permissions for a securable Sitefinity object (a page node or a content item). The
/// plugin decodes the runtime Grant/Deny bitmasks against each set's action vocabulary into
/// per-principal effective access (granted AND not denied), and resolves public visibility and
/// inheritance — so this answers "is it public?" and "what can each role actually do here?".
/// </summary>
public sealed class PermissionsResponse
{
    /// <summary>The identifier that was inspected.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>"page" or "content".</summary>
    public string TargetKind { get; set; } = string.Empty;

    /// <summary>Title/name of the resolved object.</summary>
    public string TargetTitle { get; set; } = string.Empty;

    /// <summary>One-line human summary of the access picture.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>True when the object inherits permissions from its parent rather than carrying overrides.</summary>
    public bool InheritsPermissions { get; set; }

    /// <summary>True when the object is allowed to inherit (some objects always carry their own).</summary>
    public bool CanInheritPermissions { get; set; }

    /// <summary>True when the object carries its own (local) permission rows.</summary>
    public bool HasLocalOverrides { get; set; }

    /// <summary>For a page that inherits: the parent the permissions flow from.</summary>
    public string InheritedFrom { get; set; } = string.Empty;

    /// <summary>True when the Everyone (anonymous) role can effectively View the object.</summary>
    public bool IsPublic { get; set; }

    /// <summary>True when any authenticated user can effectively View the object.</summary>
    public bool IsAuthenticatedAccessible { get; set; }

    /// <summary>The permission sets this object supports (e.g. "Pages").</summary>
    public List<string> SupportedPermissionSets { get; set; } = [];

    /// <summary>Flattened, deny-resolved access across every set — the quick "who can do what" view.</summary>
    public List<PrincipalAccess> Principals { get; set; } = [];

    /// <summary>Per-set detail, including each set's full action vocabulary.</summary>
    public List<PermissionSetView> Sets { get; set; } = [];

    /// <summary>Populated only when the request supplied an action and/or principal to answer directly.</summary>
    public AccessAnswer? Answer { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>Computed access for one principal (role/user), after deny-wins resolution.</summary>
public sealed class PrincipalAccess
{
    public string PrincipalId { get; set; } = string.Empty;
    public string PrincipalName { get; set; } = string.Empty;

    /// <summary>"Role", "User", "SpecialRole" (Everyone / Authenticated / Owner), or "Unknown".</summary>
    public string PrincipalType { get; set; } = string.Empty;

    /// <summary>True for an administrative role — implicitly has full control regardless of the grants shown.</summary>
    public bool IsAdministrative { get; set; }

    /// <summary>The permission set this row belongs to (e.g. "Pages").</summary>
    public string PermissionSet { get; set; } = string.Empty;

    /// <summary>Actions the principal can effectively perform (granted AND not denied). The headline result.</summary>
    public List<string> EffectiveActions { get; set; } = [];

    /// <summary>Actions explicitly granted (before deny resolution).</summary>
    public List<string> GrantedActions { get; set; } = [];

    /// <summary>Actions explicitly denied (deny wins over grant).</summary>
    public List<string> DeniedActions { get; set; } = [];

    /// <summary>Optional note, e.g. an administrative-role caveat or a missing action vocabulary.</summary>
    public string? Note { get; set; }
}

/// <summary>One permission set on the object: its full action vocabulary plus per-principal access.</summary>
public sealed class PermissionSetView
{
    public string SetName { get; set; } = string.Empty;

    /// <summary>Every action the set defines — the vocabulary the grant/deny masks decode against.</summary>
    public List<string> AvailableActions { get; set; } = [];

    public List<PrincipalAccess> Principals { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>Direct yes/no answer to "can &lt;Principal&gt; &lt;Action&gt; this object?" when supplied.</summary>
public sealed class AccessAnswer
{
    public string Principal { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public string Reason { get; set; } = string.Empty;
}

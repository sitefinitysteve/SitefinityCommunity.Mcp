namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Response body of <c>GET /RestApi/mcp/ping</c>.
/// </summary>
public sealed class PingResponse
{
    /// <summary>Always <c>"ok"</c> when the API key was accepted.</summary>
    public string? Status { get; set; }

    /// <summary>
    /// Version of the plugin sources installed in the Sitefinity project. Plugin builds before 3.6.0
    /// omit this — a null value means "3.5.0 or earlier", not "unknown site".
    /// </summary>
    public string? PluginVersion { get; set; }

    /// <summary>
    /// Per-capability roster. Plugin builds before 3.5.0 omit this — a null roster means
    /// "everything enabled", which is what those builds actually do.
    /// </summary>
    public FeatureRoster? Features { get; set; }
}

/// <summary>
/// Which MCP capability areas the Sitefinity administrator has left switched on
/// (Admin &gt; Advanced &gt; McpSettings).
/// <para>
/// This is a courtesy layer only: it lets the MCP server refuse a disabled tool without a
/// network round trip and with a clearer message. The plugin enforces the same flags on
/// every request, so a stale roster can never grant access.
/// </para>
/// <para>
/// Every property defaults to <c>true</c> so a missing or partial payload fails open to the
/// shipped defaults rather than blocking a working install.
/// </para>
/// </summary>
public sealed class FeatureRoster
{
    /// <summary>Sitefinity log endpoints.</summary>
    public bool Logs { get; set; } = true;

    /// <summary>Site info, modules, dynamic types, routes, pages, widgets, templates, taxonomies.</summary>
    public bool Metadata { get; set; } = true;

    /// <summary>Live content queries.</summary>
    public bool Content { get; set; } = true;

    /// <summary>Form definitions and submissions.</summary>
    public bool Forms { get; set; } = true;

    /// <summary>Configuration section reader and advanced-settings search.</summary>
    public bool ConfigReader { get; set; } = true;

    /// <summary>Reverse lookup of widget / content / template usage.</summary>
    public bool WhereUsed { get; set; } = true;

    /// <summary>Effective permissions reader.</summary>
    public bool Permissions { get; set; } = true;

    /// <summary>Scheduled-task status and search index diagnostics.</summary>
    public bool Tasks { get; set; } = true;

    /// <summary>Cache clear / application recycle. Mirrors the plugin's Allow Write Operations flag.</summary>
    public bool Maintenance { get; set; } = true;

    /// <summary>Incident forensics, plus its three OS-level source flags.</summary>
    public IncidentFeatures Incident { get; set; } = new();

    /// <summary>A roster with every capability enabled — used when a plugin reports no roster.</summary>
    public static FeatureRoster AllEnabled => new();
}

/// <summary>
/// Incident capability state: whether the endpoint is reachable, and which OS-level sources it may read.
/// Disabled sources are skipped server-side and surface as entries in the response's warnings.
/// </summary>
public sealed class IncidentFeatures
{
    /// <summary>Whether the incident endpoint is reachable at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether the IIS W3C access log may be scanned.</summary>
    public bool AllowIisLogs { get; set; } = true;

    /// <summary>Whether the Windows Application and System event logs may be read.</summary>
    public bool AllowEventLogs { get; set; } = true;

    /// <summary>Whether the http.sys HTTPERR logs may be read.</summary>
    public bool AllowHttpErr { get; set; } = true;
}

/// <summary>
/// Body the plugin returns with HTTP 403 when a capability has been switched off.
/// </summary>
public sealed class CapabilityDisabledBody
{
    /// <summary>Name of the disabled capability (e.g. <c>Forms</c>).</summary>
    public string? Disabled { get; set; }

    /// <summary>Human-readable explanation pointing at the admin screen.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// ServiceStack error envelope. Depending on the host's ServiceStack configuration the 403 body
    /// may arrive wrapped as <c>{ ResponseStatus: { ErrorCode: "CapabilityDisabled", Message: … } }</c>
    /// instead of the raw DTO — both shapes must be recognized.
    /// </summary>
    public CapabilityDisabledResponseStatus? ResponseStatus { get; set; }
}

/// <summary>The <c>ResponseStatus</c> envelope ServiceStack may wrap the 403 body in.</summary>
public sealed class CapabilityDisabledResponseStatus
{
    public string? ErrorCode { get; set; }

    public string? Message { get; set; }
}

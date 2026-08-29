namespace SitefinityCommunity.Mcp.Extensions;

/// <summary>
/// Thrown when a Sitefinity MCP endpoint answers HTTP 403 with a capability-disabled body,
/// meaning an administrator switched that capability off in Admin &gt; Advanced &gt; McpSettings.
/// <para>
/// The MCP server normally refuses such tools up front using the roster from <c>/mcp/ping</c>;
/// this covers the gaps — a stale cached roster, a setting changed mid-session, or a log tool
/// in remote mode (which is never pre-blocked, because in local mode it reads the filesystem).
/// </para>
/// </summary>
public sealed class SitefinityCapabilityDisabledException : Exception
{
    /// <summary>Name of the disabled capability, when the plugin reported one.</summary>
    public string? Capability { get; }

    /// <summary>
    /// Creates the exception for the named capability.
    /// </summary>
    /// <param name="capability">Capability reported by the plugin, or null when absent.</param>
    /// <param name="message">Message shown to the MCP client.</param>
    public SitefinityCapabilityDisabledException(string? capability, string message)
        : base(message)
    {
        this.Capability = capability;
    }
}

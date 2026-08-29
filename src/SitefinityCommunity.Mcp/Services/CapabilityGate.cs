using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Maps MCP tool names onto the Sitefinity capability that gates them, so the tool filter can
/// refuse a disabled tool immediately instead of making a call the plugin will answer with 403.
/// <para>
/// Advisory only. The plugin is the security boundary — it re-checks the same flags on every
/// request, so a stale roster here can never grant access, only cost a round trip.
/// </para>
/// <para>
/// Only tools that are <i>purely</i> remote are listed. Environment, status and log tools are
/// deliberately absent: the log tools read the filesystem directly in local mode, so pre-blocking
/// them on the remote <c>Logs</c> flag would break a working local setup. In remote mode the
/// plugin's 403 handles them and is surfaced with the same message.
/// </para>
/// </summary>
public static class CapabilityGate
{
    private static readonly Dictionary<string, string> ToolCapabilities = new(StringComparer.Ordinal)
    {
        // Metadata — site info, modules, types, routes, pages, widgets, templates, taxonomies
        ["sitefinity_get_site_info"] = "Metadata",
        ["sitefinity_list_modules"] = "Metadata",
        ["sitefinity_list_dynamic_types"] = "Metadata",
        ["sitefinity_get_type_fields"] = "Metadata",
        ["sitefinity_get_module_structure"] = "Metadata",
        ["sitefinity_list_page_routes"] = "Metadata",
        ["sitefinity_list_api_routes"] = "Metadata",
        ["sitefinity_get_page_details"] = "Metadata",
        ["sitefinity_get_page_widget_tree"] = "Metadata",
        ["sitefinity_get_widget_properties"] = "Metadata",
        ["sitefinity_list_templates"] = "Metadata",
        ["sitefinity_list_taxonomies"] = "Metadata",

        // Content
        ["sitefinity_list_content"] = "Content",

        // Forms
        ["sitefinity_list_forms"] = "Forms",
        ["sitefinity_get_form_fields"] = "Forms",
        ["sitefinity_list_form_responses"] = "Forms",

        // Config reader (includes the advanced-settings search index)
        ["sitefinity_list_config_sections"] = "ConfigReader",
        ["sitefinity_get_config_section"] = "ConfigReader",
        ["sitefinity_search_settings"] = "ConfigReader",

        // Where used
        ["sitefinity_where_used"] = "WhereUsed",

        // Permissions
        ["sitefinity_get_permissions"] = "Permissions",

        // Incident
        ["sitefinity_investigate_incident"] = "Incident",

        // Scheduled tasks + search index diagnostics
        ["sitefinity_get_scheduled_task_status"] = "Tasks",
        ["sitefinity_list_search_indexes"] = "Tasks",

        // Maintenance (write) — mirrors the plugin's Allow Write Operations flag
        ["sitefinity_clear_cache"] = "Maintenance",
        ["sitefinity_recycle_app"] = "Maintenance",
    };

    /// <summary>
    /// Admin element name to quote in the error message for each capability.
    /// </summary>
    private static readonly Dictionary<string, string> AdminElementNames = new(StringComparer.Ordinal)
    {
        ["Logs"] = "Logs",
        ["Metadata"] = "Metadata",
        ["Content"] = "Content",
        ["Forms"] = "Forms",
        ["ConfigReader"] = "Config Reader",
        ["WhereUsed"] = "Where Used",
        ["Permissions"] = "Permissions",
        ["Incident"] = "Incident",
        ["Tasks"] = "Scheduled Tasks",
        ["Maintenance"] = "Allow Write Operations",

        // Sub-capabilities: not roster entries and never pre-blocked (they gate one endpoint, or one
        // named section, rather than a tool group). They only ever arrive as a 403 body, so they need
        // an admin path here for the message to read sensibly.
        ["FormsResponses"] = "Forms > Allow Responses",
        ["ConfigSection"] = "Config Reader > Excluded Sections",
    };

    /// <summary>
    /// Returns the capability gating the given tool, or <c>null</c> when the tool is not
    /// capability-gated (environment, status and log tools).
    /// </summary>
    /// <param name="toolName">MCP tool name, e.g. <c>sitefinity_list_forms</c>.</param>
    public static string? GetCapability(string toolName)
    {
        return ToolCapabilities.TryGetValue(toolName, out var capability) ? capability : null;
    }

    /// <summary>
    /// Returns whether the roster has the named capability switched on. Unknown names return true.
    /// </summary>
    /// <param name="roster">Roster reported by the plugin's ping endpoint.</param>
    /// <param name="capability">Capability name from <see cref="GetCapability"/>.</param>
    public static bool IsEnabled(FeatureRoster roster, string capability)
    {
        return capability switch
        {
            "Logs" => roster.Logs,
            "Metadata" => roster.Metadata,
            "Content" => roster.Content,
            "Forms" => roster.Forms,
            "ConfigReader" => roster.ConfigReader,
            "WhereUsed" => roster.WhereUsed,
            "Permissions" => roster.Permissions,
            "Incident" => roster.Incident.Enabled,
            "Tasks" => roster.Tasks,
            "Maintenance" => roster.Maintenance,
            _ => true
        };
    }

    /// <summary>
    /// The message shown when a tool's capability has been switched off in Sitefinity.
    /// </summary>
    /// <param name="capability">Capability name from <see cref="GetCapability"/>.</param>
    public static string BuildDisabledMessage(string capability)
    {
        var element = AdminElementNames.TryGetValue(capability, out var name) ? name : capability;
        return $"This tool is disabled by the Sitefinity administrator (Admin > Advanced > McpSettings > {element}).";
    }

    /// <summary>
    /// Returns the refusal message when the tool's capability is disabled, or <c>null</c> when the
    /// call may proceed. A null roster (older plugin with no roster in its ping) always proceeds.
    /// </summary>
    /// <param name="toolName">MCP tool name being invoked.</param>
    /// <param name="roster">Roster reported by the plugin, or null when unknown.</param>
    public static string? CheckTool(string toolName, FeatureRoster? roster)
    {
        if (roster is null)
        {
            return null;
        }

        var capability = GetCapability(toolName);

        if (capability is null || IsEnabled(roster, capability))
        {
            return null;
        }

        return BuildDisabledMessage(capability);
    }
}

// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// Register in Global.asax: McpInit.Register();
// ============================================================================

using System.Configuration;
using System.Net;
using ServiceStack;
using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Localization;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// MCP configuration section. Appears in Sitefinity admin under:
    /// Settings → Advanced → McpSettings
    /// </summary>
    public class McpConfig : ConfigSection
    {
        [ObjectInfo(Title = "API Key", Description = "API key for authenticating MCP server requests. Must match the sitefinityApiKey in your sitefinity-mcp.json config.")]
        [ConfigurationProperty("ApiKey", DefaultValue = "")]
        [SecretData]
        public string ApiKey
        {
            get { return (string)this["ApiKey"]; }
            set { this["ApiKey"] = value; }
        }

        [ObjectInfo(Title = "Enabled", Description = "Enable or disable MCP endpoints. When disabled, all /RestApi/mcp/* routes are not registered. Requires app pool recycle to take effect, but the runtime filter also blocks requests immediately.")]
        [ConfigurationProperty("Enabled", DefaultValue = false)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }

        [ObjectInfo(Title = "Allow Write Operations", Description = "Permit state-changing MCP tools (clear cache, recycle application). When false, those endpoints are refused even with a valid API key. Read-only tools are unaffected. Leave OFF unless you intend to let the MCP server clear caches or recycle this instance.")]
        [ConfigurationProperty("AllowWriteOperations", DefaultValue = false)]
        public bool AllowWriteOperations
        {
            get { return (bool)this["AllowWriteOperations"]; }
            set { this["AllowWriteOperations"] = value; }
        }

        // ── Per-capability toggles ───────────────────────────────────
        // Each capability is a nested config element rendered as an expandable node in
        // Admin &gt; Advanced &gt; McpSettings. Every one defaults to Enabled, so upgrading
        // an existing site changes nothing until an administrator turns something off.

        [ObjectInfo(Title = "Logs", Description = "Sitefinity log endpoints (/mcp/logs, /mcp/logs/{FileName}, /mcp/logs/search, /mcp/logs/last-error).")]
        [ConfigurationProperty("Logs")]
        public McpLogsToolElement Logs
        {
            get { return (McpLogsToolElement)this["Logs"]; }
        }

        [ObjectInfo(Title = "Metadata", Description = "Site info, modules, dynamic types, routes, pages, widgets, templates and taxonomies.")]
        [ConfigurationProperty("Metadata")]
        public McpMetadataToolElement Metadata
        {
            get { return (McpMetadataToolElement)this["Metadata"]; }
        }

        [ObjectInfo(Title = "Content", Description = "Live content queries (/mcp/content).")]
        [ConfigurationProperty("Content")]
        public McpContentToolElement Content
        {
            get { return (McpContentToolElement)this["Content"]; }
        }

        [ObjectInfo(Title = "Forms", Description = "Form definitions and form submissions (/mcp/forms and children).")]
        [ConfigurationProperty("Forms")]
        public McpFormsToolElement Forms
        {
            get { return (McpFormsToolElement)this["Forms"]; }
        }

        [ObjectInfo(Title = "Config Reader", Description = "Configuration section reader and advanced-settings search (/mcp/config, /mcp/settings/search). Values are always secret-redacted; this switch removes the endpoints entirely.")]
        [ConfigurationProperty("ConfigReader")]
        public McpConfigReaderToolElement ConfigReader
        {
            get { return (McpConfigReaderToolElement)this["ConfigReader"]; }
        }

        [ObjectInfo(Title = "Where Used", Description = "Reverse lookup of pages and templates referencing a widget, content item or template (/mcp/where-used).")]
        [ConfigurationProperty("WhereUsed")]
        public McpWhereUsedToolElement WhereUsed
        {
            get { return (McpWhereUsedToolElement)this["WhereUsed"]; }
        }

        [ObjectInfo(Title = "Permissions", Description = "Effective per-role permissions on a page or content item (/mcp/permissions).")]
        [ConfigurationProperty("Permissions")]
        public McpPermissionsToolElement Permissions
        {
            get { return (McpPermissionsToolElement)this["Permissions"]; }
        }

        [ObjectInfo(Title = "Incident", Description = "Incident forensics across Sitefinity logs, IIS access logs, the Windows event logs and HTTPERR (/mcp/incident-window). Individual OS-level sources can be turned off separately.")]
        [ConfigurationProperty("Incident")]
        public McpIncidentToolElement Incident
        {
            get { return (McpIncidentToolElement)this["Incident"]; }
        }
    }

    /// <summary>
    /// Marker base for every MCP tool element.
    /// <para>
    /// Every capability gets its own named subclass — even one that adds nothing beyond
    /// <c>Enabled</c> — so that a future per-tool setting has an obvious home and does not need a
    /// config migration. Element granularity follows the plugin service boundary: tools backed by
    /// one service (the log trio, the metadata family) share one element.
    /// </para>
    /// <para>
    /// <b>Do not move <c>Enabled</c> onto this base.</b> Sitefinity's advanced-settings UI lists a
    /// subclass's own properties first and inherited ones after them, so an inherited
    /// <c>Enabled</c> renders at the BOTTOM of the screen — below Incident's source flags, for
    /// example. Each subclass therefore declares <c>Enabled</c> itself, as its FIRST property, so it
    /// renders at the top of every capability screen. The <c>ConfigurationProperty</c> name and
    /// default are identical everywhere, so saved values are unaffected by where it is declared.
    /// </para>
    /// </summary>
    public abstract class McpToolElement : ConfigElement
    {
        /// <summary>
        /// Creates the element. Sitefinity's configuration engine supplies the parent element.
        /// </summary>
        /// <param name="parent">Owning configuration element.</param>
        protected McpToolElement(ConfigElement parent)
            : base(parent)
        {
        }
    }

    /// <summary>
    /// Sitefinity log endpoints (<c>McpLogService</c>).
    /// </summary>
    public class McpLogsToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpLogsToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Site info, modules, dynamic types, routes, pages, widgets, templates and taxonomies
    /// (<c>McpMetadataService</c>).
    /// </summary>
    public class McpMetadataToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpMetadataToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Live content queries (<c>McpContentService</c>).
    /// </summary>
    public class McpContentToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpContentToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Form definitions and submissions (<c>McpFormsService</c>).
    /// </summary>
    public class McpFormsToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpFormsToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Configuration section reader and advanced-settings search
    /// (<c>McpConfigService</c> and <c>McpSettingsSearchService</c>).
    /// </summary>
    public class McpConfigReaderToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpConfigReaderToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Reverse lookup of widget, content and template usage (<c>McpWhereUsedService</c>).
    /// </summary>
    public class McpWhereUsedToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpWhereUsedToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Effective per-role permissions reader (<c>McpPermissionsService</c>).
    /// </summary>
    public class McpPermissionsToolElement : McpToolElement
    {
        /// <summary>Creates the element.</summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpPermissionsToolElement(ConfigElement parent) : base(parent) { }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }
    }

    /// <summary>
    /// Incident forensics (<c>McpSystemLogService</c>). Additionally gates the three OS-level log
    /// sources it reads and holds the IIS log folder override. A disabled source is skipped and
    /// reported in the response's <c>Warnings</c> rather than failing the whole call.
    /// </summary>
    public class McpIncidentToolElement : McpToolElement
    {
        /// <summary>
        /// Creates the element. Sitefinity's configuration engine supplies the parent element.
        /// </summary>
        /// <param name="parent">Owning configuration element.</param>
        public McpIncidentToolElement(ConfigElement parent)
            : base(parent)
        {
        }

        [ObjectInfo(Title = "Enabled", Description = "When unchecked, every endpoint in this capability is refused with HTTP 403 and the MCP server hides the matching tools.")]
        [ConfigurationProperty("Enabled", DefaultValue = true)]
        public bool Enabled
        {
            get { return (bool)this["Enabled"]; }
            set { this["Enabled"] = value; }
        }

        [ObjectInfo(Title = "Allow IIS Logs", Description = "Permit reading this site's IIS W3C access log during incident scans. Cookie and Authorization fields are never read; c-ip and cs-username are returned.")]
        [ConfigurationProperty("AllowIisLogs", DefaultValue = true)]
        public bool AllowIisLogs
        {
            get { return (bool)this["AllowIisLogs"]; }
            set { this["AllowIisLogs"] = value; }
        }

        [ObjectInfo(Title = "Allow Event Logs", Description = "Permit reading the Windows Application and System event logs during incident scans. The Security log is never read.")]
        [ConfigurationProperty("AllowEventLogs", DefaultValue = true)]
        public bool AllowEventLogs
        {
            get { return (bool)this["AllowEventLogs"]; }
            set { this["AllowEventLogs"] = value; }
        }

        [ObjectInfo(Title = "Allow HTTPERR", Description = "Permit reading Windows' http.sys error log (C:\\Windows\\System32\\LogFiles\\HTTPERR) during incident scans. http.sys sits in front of IIS and records the requests IIS never saw — the 503s served while the app pool was stopped, crashed, or its queue was full. During an outage this is often the only log that captured anything.")]
        [ConfigurationProperty("AllowHttpErr", DefaultValue = true)]
        public bool AllowHttpErr
        {
            get { return (bool)this["AllowHttpErr"]; }
            set { this["AllowHttpErr"] = value; }
        }

        [ObjectInfo(Title = "IIS Log Path", Description = "Optional override for the folder holding this site's IIS W3C access logs (e.g. D:\\Logs\\W3SVC3). Leave blank to auto-detect %SystemDrive%\\inetpub\\logs\\LogFiles\\W3SVC{siteId} from the hosting environment.")]
        [ConfigurationProperty("IisLogPath", DefaultValue = "")]
        public string IisLogPath
        {
            get { return (string)this["IisLogPath"]; }
            set { this["IisLogPath"] = value; }
        }
    }

    /// <summary>
    /// Plugin-side enforcement of the per-capability toggles configured in
    /// Sitefinity Admin &gt; Advanced &gt; McpSettings.
    /// <para>
    /// This is the security boundary. The MCP server also reads the roster from
    /// <c>/mcp/ping</c> so it can refuse a disabled tool without a round trip, but that
    /// is only a courtesy — every handler still calls <see cref="EnsureEnabled"/>.
    /// </para>
    /// <para>
    /// Fail-open by design: a missing or unreadable config section is treated as
    /// "everything enabled", matching the shipped defaults. A configuration read error
    /// must never silently disable a working install.
    /// </para>
    /// </summary>
    public static class McpCapabilities
    {
        /// <summary>Sitefinity log endpoints.</summary>
        public const string Logs = "Logs";

        /// <summary>Site info, modules, dynamic types, routes, pages, widgets, templates, taxonomies.</summary>
        public const string Metadata = "Metadata";

        /// <summary>Live content queries.</summary>
        public const string Content = "Content";

        /// <summary>Form definitions and submissions.</summary>
        public const string Forms = "Forms";

        /// <summary>Configuration section reader and advanced-settings search.</summary>
        public const string ConfigReader = "ConfigReader";

        /// <summary>Reverse lookup of widget / content / template usage.</summary>
        public const string WhereUsed = "WhereUsed";

        /// <summary>Effective permissions reader.</summary>
        public const string Permissions = "Permissions";

        /// <summary>Incident forensics across Sitefinity, IIS, Event Log and HTTPERR.</summary>
        public const string Incident = "Incident";

        /// <summary>
        /// Shown to the administrator (and forwarded to the MCP client) when a capability is off.
        /// </summary>
        public const string DisabledReason =
            "Disabled by the administrator in Sitefinity Admin > Advanced > McpSettings.";

        /// <summary>
        /// Returns the configuration section, or <c>null</c> when it cannot be read.
        /// </summary>
        public static McpConfig TryGetConfig()
        {
            try
            {
                return Config.Get<McpConfig>();
            }
            catch
            {
                // Fail open — an unreadable config must not disable a working install.
                return null;
            }
        }

        /// <summary>
        /// Returns whether the named capability is enabled. Unknown names, a missing section
        /// and read failures all return <c>true</c> (the shipped default).
        /// </summary>
        /// <param name="capability">One of the capability name constants on this class.</param>
        public static bool IsEnabled(string capability)
        {
            var config = TryGetConfig();

            if (config == null)
            {
                return true;
            }

            try
            {
                switch (capability)
                {
                    case Logs: return config.Logs.Enabled;
                    case Metadata: return config.Metadata.Enabled;
                    case Content: return config.Content.Enabled;
                    case Forms: return config.Forms.Enabled;
                    case ConfigReader: return config.ConfigReader.Enabled;
                    case WhereUsed: return config.WhereUsed.Enabled;
                    case Permissions: return config.Permissions.Enabled;
                    case Incident: return config.Incident.Enabled;
                    default: return true;
                }
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Throws an HTTP 403 carrying a structured body when the capability is turned off.
        /// Call this as the first line of every MCP service handler.
        /// </summary>
        /// <param name="capability">One of the capability name constants on this class.</param>
        public static void EnsureEnabled(string capability)
        {
            if (IsEnabled(capability))
            {
                return;
            }

            var body = new McpCapabilityDisabledResponse
            {
                Disabled = capability,
                Reason = DisabledReason,
            };

            throw new HttpError(body, HttpStatusCode.Forbidden, "CapabilityDisabled",
                "The '" + capability + "' capability is disabled. " + DisabledReason);
        }

        /// <summary>
        /// Builds the feature roster reported by <c>/mcp/ping</c>. Always reflects the live
        /// configuration; defaults to everything enabled when the section is unreadable.
        /// </summary>
        public static McpFeatureRoster BuildRoster()
        {
            var roster = new McpFeatureRoster();
            var config = TryGetConfig();

            if (config == null)
            {
                return roster;
            }

            try
            {
                roster.Logs = config.Logs.Enabled;
                roster.Metadata = config.Metadata.Enabled;
                roster.Content = config.Content.Enabled;
                roster.Forms = config.Forms.Enabled;
                roster.ConfigReader = config.ConfigReader.Enabled;
                roster.WhereUsed = config.WhereUsed.Enabled;
                roster.Permissions = config.Permissions.Enabled;
                roster.Maintenance = config.AllowWriteOperations;
                roster.Incident = new McpIncidentFeatures
                {
                    Enabled = config.Incident.Enabled,
                    AllowIisLogs = config.Incident.AllowIisLogs,
                    AllowEventLogs = config.Incident.AllowEventLogs,
                    AllowHttpErr = config.Incident.AllowHttpErr,
                };
            }
            catch
            {
                // Partial read — return the defaults rather than a misleading half-roster.
                return new McpFeatureRoster();
            }

            return roster;
        }

        /// <summary>
        /// Reads the three Incident source flags, defaulting to allowed when unreadable.
        /// </summary>
        /// <param name="allowIis">Receives whether the IIS access log may be read.</param>
        /// <param name="allowEventLog">Receives whether the Windows event logs may be read.</param>
        /// <param name="allowHttpErr">Receives whether the HTTPERR logs may be read.</param>
        public static void GetIncidentSourceFlags(out bool allowIis, out bool allowEventLog, out bool allowHttpErr)
        {
            allowIis = true;
            allowEventLog = true;
            allowHttpErr = true;

            var config = TryGetConfig();

            if (config == null)
            {
                return;
            }

            try
            {
                allowIis = config.Incident.AllowIisLogs;
                allowEventLog = config.Incident.AllowEventLogs;
                allowHttpErr = config.Incident.AllowHttpErr;
            }
            catch
            {
                allowIis = true;
                allowEventLog = true;
                allowHttpErr = true;
            }
        }
    }
}

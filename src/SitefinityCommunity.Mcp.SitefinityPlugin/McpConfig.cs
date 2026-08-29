// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// Register in Global.asax: McpInit.Register();
// ============================================================================

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Text.RegularExpressions;
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

        [ObjectInfo(Title = "Audit Requests", Description = "Append every MCP request — including rejected ones — as one line to App_Data\\Sitefinity\\Logs\\McpAudit.log: who called what, from where, and whether authentication passed. Requests only; results are never logged, and query strings are secret-redacted. The file lives in the standard Sitefinity Logs folder, so the MCP's own log tools can read it.")]
        [ConfigurationProperty("AuditRequests", DefaultValue = true)]
        public bool AuditRequests
        {
            get { return (bool)this["AuditRequests"]; }
            set { this["AuditRequests"] = value; }
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

        [ObjectInfo(Title = "Allow Responses", Description = "Permit reading form submissions. When unchecked, form DEFINITIONS stay readable (list forms, inspect fields) but the responses endpoint is refused with HTTP 403 — so an assistant can still reason about a form's shape without ever seeing what people submitted.")]
        [ConfigurationProperty("AllowResponses", DefaultValue = true)]
        public bool AllowResponses
        {
            get { return (bool)this["AllowResponses"]; }
            set { this["AllowResponses"] = value; }
        }

        [ObjectInfo(Title = "Excluded Fields", Description = "Comma-separated form field names that are stripped from submissions and never returned (e.g. \"SSN, HealthCard\"). Matching is case-insensitive on the exact field name. Excluded fields are removed before redaction and before any search term is matched, so they cannot be discovered by searching for their values.")]
        [ConfigurationProperty("ExcludedFields", DefaultValue = "")]
        public string ExcludedFields
        {
            get { return (string)this["ExcludedFields"]; }
            set { this["ExcludedFields"] = value; }
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

        [ObjectInfo(Title = "Excluded Sections", Description = "Comma-separated configuration section names hidden from the MCP entirely: omitted from the section list, refused with HTTP 403 when requested directly, and stripped from advanced-settings search results. A \"Config\" or \".config\" suffix is optional — \"Authentication\" also matches \"AuthenticationConfig\" and \"Authentication.config\". Supports * wildcards: \"Auth*\" hides everything starting with Auth, \"*Security*\" everything containing Security.")]
        [ConfigurationProperty("ExcludedSections", DefaultValue = "")]
        public string ExcludedSections
        {
            get { return (string)this["ExcludedSections"]; }
            set { this["ExcludedSections"] = value; }
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
        /// Form submissions specifically. A sub-capability of <see cref="Forms"/>: form definitions
        /// stay readable when this is off. Deliberately NOT in the ping roster — it gates one
        /// endpoint, not a tool group, so the plugin's 403 is the only enforcement point.
        /// </summary>
        public const string FormsResponses = "FormsResponses";

        /// <summary>
        /// A single configuration section hidden by name. Reported as the <c>Disabled</c> value on the
        /// 403 from <c>/mcp/config/{SectionName}</c>. Not a roster entry — it gates individual
        /// sections, not the Config Reader capability as a whole.
        /// </summary>
        public const string ConfigSection = "ConfigSection";

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
        /// Throws an HTTP 403 when form submissions are switched off (Forms &gt; Allow Responses).
        /// Call this in the responses handler only — form definitions stay readable.
        /// </summary>
        public static void EnsureFormResponsesAllowed()
        {
            var config = TryGetConfig();

            if (config == null)
            {
                return;
            }

            bool allowed;

            try
            {
                allowed = config.Forms.AllowResponses;
            }
            catch
            {
                // Fail open, same as every other capability read.
                return;
            }

            if (allowed)
            {
                return;
            }

            var body = new McpCapabilityDisabledResponse
            {
                Disabled = FormsResponses,
                Reason = DisabledReason,
            };

            throw new HttpError(body, HttpStatusCode.Forbidden, "CapabilityDisabled",
                "Form responses are disabled. " + DisabledReason +
                " Form definitions remain available.");
        }

        /// <summary>
        /// Field names an administrator has excluded from form submissions
        /// (Forms &gt; Excluded Fields). Comma-separated, trimmed, compared case-insensitively
        /// against the exact field name. Returns an empty set when unset or unreadable.
        /// </summary>
        public static HashSet<string> GetExcludedFormFields()
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var config = TryGetConfig();

            if (config == null)
            {
                return excluded;
            }

            string raw;

            try
            {
                raw = config.Forms.ExcludedFields;
            }
            catch
            {
                return excluded;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return excluded;
            }

            foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = token.Trim();

                if (name.Length > 0)
                {
                    excluded.Add(name);
                }
            }

            return excluded;
        }

        /// <summary>
        /// Configuration section names an administrator has hidden
        /// (Config Reader &gt; Excluded Sections). Compare with <see cref="IsSectionExcluded"/>,
        /// which handles the optional Config / .config suffix and <c>*</c> wildcards.
        /// Returns an empty list when unset or unreadable.
        /// <para>
        /// Plain tokens are suffix-stripped here so they compare exactly. Wildcard tokens are only
        /// trimmed and lower-cased — stripping <c>config</c> off <c>*config*</c> would change what the
        /// pattern means.
        /// </para>
        /// </summary>
        public static List<string> GetExcludedConfigSections()
        {
            var tokens = new List<string>();
            var config = TryGetConfig();

            if (config == null)
            {
                return tokens;
            }

            string raw;

            try
            {
                raw = config.ConfigReader.ExcludedSections;
            }
            catch
            {
                return tokens;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                return tokens;
            }

            foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = token.IndexOf('*') >= 0
                    ? token.Trim().ToLowerInvariant()
                    : NormalizeSectionName(token);

                if (name.Length > 0 && !tokens.Contains(name))
                {
                    tokens.Add(name);
                }
            }

            return tokens;
        }

        /// <summary>
        /// Whether a section name matches one of the administrator's excluded tokens.
        /// <para>
        /// Plain tokens compare exactly after <see cref="NormalizeSectionName"/>, so
        /// <c>Authentication</c>, <c>authenticationconfig</c> and <c>Authentication.config</c> all
        /// match the section <c>AuthenticationConfig</c>.
        /// </para>
        /// <para>
        /// Tokens containing <c>*</c> are treated as wildcards and matched against BOTH the raw
        /// section name and its suffix-stripped form, so <c>Auth*</c> catches
        /// <c>AuthenticationConfig</c> either way. A malformed or pathological pattern is ignored
        /// rather than throwing.
        /// </para>
        /// </summary>
        /// <param name="sectionName">Candidate section name (type name, file name, or index value).</param>
        /// <param name="excludedTokens">Tokens from <see cref="GetExcludedConfigSections"/>.</param>
        public static bool IsSectionExcluded(string sectionName, List<string> excludedTokens)
        {
            if (excludedTokens == null || excludedTokens.Count == 0 || string.IsNullOrWhiteSpace(sectionName))
            {
                return false;
            }

            var raw = sectionName.Trim().ToLowerInvariant();
            var stripped = NormalizeSectionName(sectionName);

            foreach (var token in excludedTokens)
            {
                if (token.IndexOf('*') >= 0)
                {
                    if (WildcardMatches(token, raw) || WildcardMatches(token, stripped))
                    {
                        return true;
                    }

                    continue;
                }

                if (stripped.Length > 0 && string.Equals(token, stripped, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Matches a <c>*</c> wildcard token against a candidate. The token is escaped so only
        /// <c>*</c> is special, then anchored. A parse failure or match timeout is treated as
        /// "no match" so one bad token can never break the endpoint.
        /// </summary>
        /// <param name="token">Wildcard token, already trimmed and lower-cased.</param>
        /// <param name="candidate">Candidate section name to test.</param>
        private static bool WildcardMatches(string token, string candidate)
        {
            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            try
            {
                var pattern = "^" + Regex.Escape(token).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(candidate, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Malformed or pathological pattern — ignore the token rather than failing the call.
                return false;
            }
        }

        /// <summary>
        /// Trims, lower-cases, then strips a trailing <c>.config</c> and a trailing <c>config</c>,
        /// so every spelling of a section name collapses to one comparable token.
        /// </summary>
        /// <param name="name">Raw section name or configured token.</param>
        public static string NormalizeSectionName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var value = name.Trim().ToLowerInvariant();

            if (value.EndsWith(".config", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - ".config".Length);
            }

            if (value.EndsWith("config", StringComparison.Ordinal) && value.Length > "config".Length)
            {
                value = value.Substring(0, value.Length - "config".Length);
            }

            return value.Trim();
        }

        /// <summary>
        /// Throws an HTTP 403 when the requested configuration section has been hidden by an
        /// administrator (Config Reader &gt; Excluded Sections).
        /// </summary>
        /// <param name="sectionName">Section the caller asked for.</param>
        public static void EnsureSectionNotExcluded(string sectionName)
        {
            if (!IsSectionExcluded(sectionName, GetExcludedConfigSections()))
            {
                return;
            }

            var body = new McpCapabilityDisabledResponse
            {
                Disabled = ConfigSection,
                Reason = "Configuration section '" + sectionName + "' is excluded by the administrator in " +
                    "Sitefinity Admin > Advanced > McpSettings > Config Reader > Excluded Sections.",
            };

            throw new HttpError(body, HttpStatusCode.Forbidden, "CapabilityDisabled",
                "Configuration section '" + sectionName + "' is hidden by the administrator " +
                "(McpSettings > Config Reader > Excluded Sections).");
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

// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// Register in Global.asax: McpInit.Register();
// ============================================================================

using System.Configuration;
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

        [ObjectInfo(Title = "IIS Log Path", Description = "Optional override for the folder holding this site's IIS W3C access logs (e.g. D:\\Logs\\W3SVC3). Leave blank to auto-detect %SystemDrive%\\inetpub\\logs\\LogFiles\\W3SVC{siteId} from the hosting environment. Used by the incident-window endpoint.")]
        [ConfigurationProperty("IisLogPath", DefaultValue = "")]
        public string IisLogPath
        {
            get { return (string)this["IisLogPath"]; }
            set { this["IisLogPath"] = value; }
        }
    }
}

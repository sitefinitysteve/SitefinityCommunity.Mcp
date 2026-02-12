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
    }
}

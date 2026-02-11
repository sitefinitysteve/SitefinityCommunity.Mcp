// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
//
// Single-line registration in Global.asax Bootstrapper_Initialized:
//   SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();
// ============================================================================

using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// One-line initializer for the MCP plugin.
    /// Call McpInit.Register() in your Bootstrapper_Initialized handler.
    /// </summary>
    public static class McpInit
    {
        public static void Register()
        {
            Config.RegisterSection<McpConfig>();
            SystemManager.RegisterServiceStackPlugin(new McpServicePlugin());
        }
    }
}

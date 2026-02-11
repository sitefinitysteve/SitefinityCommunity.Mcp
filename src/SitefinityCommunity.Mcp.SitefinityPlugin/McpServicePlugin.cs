// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// Registered automatically via McpInit.Register();
// ============================================================================

using ServiceStack;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// ServiceStack plugin that registers all MCP REST endpoints.
    /// Endpoints are available at /RestApi/mcp/*
    /// </summary>
    public class McpServicePlugin : IPlugin
    {
        public void Register(IAppHost appHost)
        {
            appHost.RegisterService(typeof(McpLogService));
        }
    }
}

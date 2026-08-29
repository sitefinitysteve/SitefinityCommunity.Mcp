// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// Registered automatically via McpInit.Register();
// ============================================================================

using ServiceStack;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Identity of this plugin build, reported by <c>GET /mcp/ping</c> so the MCP server can tell the
    /// operator when the copy of the plugin installed in the Sitefinity project has fallen behind.
    /// <para>
    /// <b>Bump <c>Version</c> on every release.</b> It is one of FOUR places the version lives, and
    /// they must agree: this constant, <c>src/SitefinityCommunity.Mcp/SitefinityCommunity.Mcp.csproj</c>
    /// (<c>Version</c>), <c>npm/package.json</c> (<c>version</c>), and the new section in
    /// <c>CHANGELOG.md</c>. A stale constant makes a current plugin look out of date.
    /// </para>
    /// </summary>
    public static class McpPluginInfo
    {
        /// <summary>Semantic version of this plugin source drop.</summary>
        public const string Version = "3.6.0";
    }

    /// <summary>
    /// ServiceStack plugin that registers all MCP REST endpoints.
    /// Endpoints are available at /RestApi/mcp/*
    /// </summary>
    public class McpServicePlugin : IPlugin
    {
        public void Register(IAppHost appHost)
        {
            appHost.RegisterService(typeof(McpLogService));
            appHost.RegisterService(typeof(McpMetadataService));
            appHost.RegisterService(typeof(McpContentService));
            appHost.RegisterService(typeof(McpFormsService));
            appHost.RegisterService(typeof(McpConfigService));
            appHost.RegisterService(typeof(McpSettingsSearchService));
            appHost.RegisterService(typeof(McpWhereUsedService));
            appHost.RegisterService(typeof(McpPermissionsService));
            appHost.RegisterService(typeof(McpSystemLogService));
            appHost.RegisterService(typeof(McpTasksService));
            appHost.RegisterService(typeof(McpMaintenanceService));
        }
    }
}

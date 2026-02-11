// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using System;
using ServiceStack.Web;
using Telerik.Sitefinity.Configuration;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// ServiceStack request filter that validates the X-MCP-API-Key header
    /// against the API key stored in Sitefinity's McpConfig section.
    /// Throws UnauthorizedAccessException on failure (ServiceStack returns 401).
    /// </summary>
    public class McpApiKeyAttribute : ServiceStack.RequestFilterAttribute
    {
        public override void Execute(IRequest req, IResponse res, object requestDto)
        {
            var config = Config.Get<McpConfig>();

            if (!config.Enabled)
            {
                throw new UnauthorizedAccessException("MCP endpoints are disabled.");
            }

            if (string.IsNullOrEmpty(config.ApiKey))
            {
                throw new InvalidOperationException("MCP API key not configured in Sitefinity settings.");
            }

            var apiKey = req.Headers["X-MCP-API-Key"];

            if (string.IsNullOrEmpty(apiKey) || apiKey != config.ApiKey)
            {
                throw new UnauthorizedAccessException("Invalid or missing MCP API key.");
            }
        }
    }
}

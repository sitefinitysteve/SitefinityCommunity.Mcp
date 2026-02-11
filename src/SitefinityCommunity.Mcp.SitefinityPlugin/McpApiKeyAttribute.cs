// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack.Web;
using Telerik.Sitefinity.Configuration;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// ServiceStack request filter that validates the X-MCP-API-Key header
    /// against the API key stored in Sitefinity's McpConfig section.
    /// Returns 401 if invalid or if the MCP module is disabled.
    /// </summary>
    public class McpApiKeyAttribute : ServiceStack.RequestFilterAttribute
    {
        public override void Execute(IRequest req, IResponse res, object requestDto)
        {
            var config = Config.Get<McpConfig>();

            // Check if MCP endpoints are enabled
            if (!config.Enabled)
            {
                res.StatusCode = 404;
                res.EndRequest();
                return;
            }

            // Validate API key
            var apiKey = req.Headers["X-MCP-API-Key"];

            if (string.IsNullOrEmpty(config.ApiKey))
            {
                res.StatusCode = 500;
                res.StatusDescription = "MCP API key not configured in Sitefinity settings.";
                res.EndRequest();
                return;
            }

            if (string.IsNullOrEmpty(apiKey) || apiKey != config.ApiKey)
            {
                res.StatusCode = 401;
                res.StatusDescription = "Invalid or missing MCP API key.";
                res.EndRequest();
                return;
            }
        }
    }
}

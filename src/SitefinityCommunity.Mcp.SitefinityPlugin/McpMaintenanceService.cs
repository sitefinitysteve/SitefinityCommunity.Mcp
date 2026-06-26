// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Telerik.Sitefinity;
using Telerik.Sitefinity.Abstractions;
using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Data;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// State-changing maintenance: clear caches and recycle the application. These are the only write
    /// endpoints in the plugin and are gated by <see cref="McpConfig.AllowWriteOperations"/> — refused
    /// (HTTP 403) unless an administrator explicitly enables the switch in
    /// Admin &gt; Advanced &gt; McpSettings. The MCP server enforces a matching per-environment gate, so both
    /// layers must opt in.
    ///
    /// Uses the real Sitefinity 15.x cache/restart APIs directly:
    /// <c>SystemManager.GetCacheManager(CacheManagerInstance).Flush()</c>,
    /// <c>CacheDependency.Notify(...)</c>, and <c>SystemManager.RestartApplication(...)</c>.
    /// </summary>
    [McpApiKey]
    public class McpMaintenanceService : Service
    {
        // The cache instances flushed for a "whole" clear — the rendering/navigation-facing ones whose
        // staleness a content editor would notice. (CacheManagerInstance.Internal is deliberately left
        // alone — it backs framework internals, not page output.)
        private static readonly CacheManagerInstance[] WholeCacheInstances =
        {
            CacheManagerInstance.Global,
            CacheManagerInstance.ContentOutput,
            CacheManagerInstance.PageFullPath,
            CacheManagerInstance.SiteMap,
            CacheManagerInstance.SiteMapPageData,
            CacheManagerInstance.SiteMapNodeUrl,
        };

        /// <summary>
        /// POST /RestApi/mcp/cache/clear?Scope=output|whole|page&amp;PageIdentifier=...
        /// </summary>
        public McpMaintenanceResponse Post(ClearCache request)
        {
            EnsureWriteAllowed();

            var scope = string.IsNullOrWhiteSpace(request.Scope) ? "output" : request.Scope.Trim().ToLowerInvariant();
            var response = new McpMaintenanceResponse { Operation = "clear-cache" };

            try
            {
                switch (scope)
                {
                    case "page":
                        ClearPageOutputCache(request.PageIdentifier, response);
                        break;
                    case "output":
                        FlushInstances(new[] { CacheManagerInstance.ContentOutput }, response);
                        break;
                    case "whole":
                        FlushInstances(WholeCacheInstances, response);
                        NotifyAllDependencies(response);
                        break;
                    default:
                        throw HttpError.BadRequest("Unknown Scope '" + request.Scope + "'. Use output, whole, or page.");
                }
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Cache clear failed: " + ex.Message;
            }

            return response;
        }

        /// <summary>
        /// POST /RestApi/mcp/app/recycle — restart the Sitefinity application (app-domain recycle).
        /// </summary>
        public McpMaintenanceResponse Post(RecycleApp request)
        {
            EnsureWriteAllowed();

            var response = new McpMaintenanceResponse { Operation = "recycle" };

            try
            {
                // (reason, flags, sendRestartApplicationSystemMessage) — notify other nodes in a farm.
                var invoked = SystemManager.RestartApplication(
                    "Recycle requested via SitefinityCommunity.Mcp",
                    SystemRestartFlags.Default,
                    true);

                response.Success = invoked;
                response.Message = invoked
                    ? "Application restart initiated. The site will be briefly unavailable and the next request will incur a cold start."
                    : "SystemManager.RestartApplication returned false; the restart may not have been initiated.";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Recycle failed: " + ex.Message;
            }

            return response;
        }

        // ── Write gate ───────────────────────────────────────────────

        private static void EnsureWriteAllowed()
        {
            var config = Config.Get<McpConfig>();
            if (config == null || !config.AllowWriteOperations)
            {
                throw new HttpError(HttpStatusCode.Forbidden,
                    "Write operations are disabled. Enable 'Allow Write Operations' in " +
                    "Sitefinity Admin > Advanced > McpSettings to permit cache clears and recycles.");
            }
        }

        // ── Cache clearing ───────────────────────────────────────────

        private static void FlushInstances(CacheManagerInstance[] instances, McpMaintenanceResponse response)
        {
            var flushed = new List<string>();

            foreach (var instance in instances)
            {
                try
                {
                    var cacheManager = SystemManager.GetCacheManager(instance);
                    if (cacheManager != null)
                    {
                        cacheManager.Flush();
                        flushed.Add(instance.ToString());
                    }
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Could not flush the " + instance + " cache: " + ex.Message);
                }
            }

            response.Success = flushed.Count > 0;
            response.Message = flushed.Count > 0
                ? "Flushed cache instance(s): " + string.Join(", ", flushed) + "."
                : "No cache instances were flushed (see warnings).";
        }

        private static void NotifyAllDependencies(McpMaintenanceResponse response)
        {
            try
            {
                CacheDependency.NotifyAll();
                response.Warnings.Add("Notified all cache dependencies (output/data invalidation).");
            }
            catch (Exception ex)
            {
                response.Warnings.Add("CacheDependency.NotifyAll() failed: " + ex.Message);
            }
        }

        private void ClearPageOutputCache(string pageIdentifier, McpMaintenanceResponse response)
        {
            if (string.IsNullOrWhiteSpace(pageIdentifier))
            {
                throw HttpError.BadRequest("PageIdentifier is required when Scope is 'page'.");
            }

            var node = ResolvePageNode(pageIdentifier, response);
            if (node == null)
            {
                throw HttpError.NotFound("Page not found: " + pageIdentifier);
            }

            // Invalidate via cache-dependency notification keyed on the page node (and its page data when
            // reachable) — the same mechanism Sitefinity uses to expire a single page's output cache.
            var keys = new List<CacheDependencyKey>
            {
                new CacheDependencyKey { Key = node.Id.ToString(), Type = typeof(PageNode) },
            };

            try
            {
                var pageData = node.GetPageData();
                if (pageData != null)
                {
                    keys.Add(new CacheDependencyKey { Key = pageData.Id.ToString(), Type = typeof(PageData) });
                }
            }
            catch (Exception) { /* page-data key is a bonus; the node key is the primary */ }

            try
            {
                CacheDependency.Notify(keys);
                response.Success = true;
                response.Message = "Invalidated the output-cache dependency for page '" + (node.Title ?? node.Name) + "'.";
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Message = "Could not invalidate this page's output cache: " + ex.Message +
                    " Use Scope=whole or recycle instead.";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private PageNode ResolvePageNode(string identifier, McpMaintenanceResponse response)
        {
            var pageManager = PageManager.GetManager();
            pageManager.Provider.SuppressSecurityChecks = true;

            Guid guid;
            if (Guid.TryParse(identifier, out guid))
            {
                try
                {
                    var byId = pageManager.GetPageNode(guid);
                    if (byId != null)
                    {
                        return byId;
                    }
                }
                catch (Exception) { /* fall through */ }
            }

            var backendRootId = SiteInitializer.BackendRootNodeId;
            var nodes = pageManager.GetPageNodes()
                .Where(p => p.RootNodeId != backendRootId && !p.IsDeleted)
                .ToList();

            var slug = identifier.Trim('/');

            if (identifier.IndexOf('/') >= 0)
            {
                foreach (var node in nodes)
                {
                    try
                    {
                        var url = (node.GetFullUrl() ?? string.Empty).TrimStart('~').TrimEnd('/');
                        if (!url.StartsWith("/"))
                        {
                            url = "/" + url;
                        }

                        if (string.Equals(url, "/" + slug, StringComparison.OrdinalIgnoreCase))
                        {
                            return node;
                        }
                    }
                    catch (Exception) { /* skip */ }
                }
            }

            var bySlug = nodes.FirstOrDefault(n => string.Equals(n.UrlName, slug, StringComparison.OrdinalIgnoreCase));
            if (bySlug != null)
            {
                return bySlug;
            }

            var byTitle = nodes.FirstOrDefault(n =>
                string.Equals(n.Title ?? n.Name, identifier, StringComparison.OrdinalIgnoreCase));
            if (byTitle == null)
            {
                response.Warnings.Add("No page matched '" + identifier + "'.");
            }

            return byTitle;
        }
    }
}

// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Telerik.Sitefinity;
using Telerik.Sitefinity.Abstractions;
using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// State-changing maintenance: clear caches and recycle the application. These are the only write
    /// endpoints in the plugin and are gated by <see cref="McpConfig.AllowWriteOperations"/> — refused
    /// (HTTP 403) unless an administrator explicitly enables the switch in
    /// Admin > Advanced > McpSettings. The MCP server enforces a matching per-environment gate, so both
    /// layers must opt in. The exact cache APIs vary across Sitefinity versions, so cache operations are
    /// invoked reflectively and report what they actually did.
    /// </summary>
    [McpApiKey]
    public class McpMaintenanceService : Service
    {
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
                    case "whole":
                    case "output":
                        ClearWholeCache(scope, response);
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
                var invoked = TryRestartApplication("Recycle requested via SitefinityCommunity.Mcp", out var detail);

                if (invoked)
                {
                    response.Success = true;
                    response.Message = "Application restart initiated. " + detail +
                        " The site will be briefly unavailable and the next request will incur a cold start.";
                }
                else
                {
                    response.Success = false;
                    response.Message = "Could not invoke SystemManager.RestartApplication on this Sitefinity version. " + detail;
                }
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

        // ── Cache clearing (reflective — version tolerant) ───────────

        private void ClearWholeCache(string scope, McpMaintenanceResponse response)
        {
            // Preferred: SystemManager.ClearWholeCache() — present on most versions but not guaranteed.
            try
            {
                var clear = typeof(SystemManager).GetMethod("ClearWholeCache",
                    BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

                if (clear != null)
                {
                    clear.Invoke(null, null);
                    response.Success = true;
                    response.Message = "Cleared the whole Sitefinity cache via SystemManager.ClearWholeCache().";

                    if (scope == "output")
                    {
                        response.Warnings.Add("Scope 'output' was satisfied by a whole-cache clear (no narrower API available).");
                    }

                    return;
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("SystemManager.ClearWholeCache() threw: " + ex.Message);
            }

            // Fallback: flush the global cache manager.
            if (TryFlushGlobalCacheManager(out var detail))
            {
                response.Success = true;
                response.Message = "Flushed the global cache manager. " + detail;
                response.Warnings.Add("ClearWholeCache was unavailable; output cache may persist — recycle for a hard reset.");
                return;
            }

            response.Success = false;
            response.Message = "No supported cache-clear API was found on this Sitefinity version. " +
                "Use scope=page for a specific page, or recycle the application for a full reset.";
            response.Warnings.Add(detail);
        }

        private bool TryFlushGlobalCacheManager(out string detail)
        {
            detail = string.Empty;

            try
            {
                var getCacheManager = typeof(SystemManager).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetCacheManager");

                if (getCacheManager == null)
                {
                    detail = "SystemManager.GetCacheManager not found.";
                    return false;
                }

                object cacheManager;
                var pars = getCacheManager.GetParameters();
                if (pars.Length == 0)
                {
                    cacheManager = getCacheManager.Invoke(null, null);
                }
                else if (pars.Length == 1 && pars[0].ParameterType.IsEnum)
                {
                    // CacheManagerInstance.Global is conventionally the default (value 0).
                    var enumVal = Enum.ToObject(pars[0].ParameterType, 0);
                    cacheManager = getCacheManager.Invoke(null, new[] { enumVal });
                }
                else
                {
                    detail = "Unrecognized GetCacheManager signature.";
                    return false;
                }

                if (cacheManager == null)
                {
                    detail = "GetCacheManager returned null.";
                    return false;
                }

                foreach (var methodName in new[] { "Flush", "Clear", "RemoveAll" })
                {
                    var m = cacheManager.GetType().GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                    if (m != null)
                    {
                        m.Invoke(cacheManager, null);
                        detail = "Invoked " + methodName + "() on " + cacheManager.GetType().Name + ".";
                        return true;
                    }
                }

                detail = "Cache manager exposed no Flush/Clear method.";
                return false;
            }
            catch (Exception ex)
            {
                detail = "Cache manager flush threw: " + ex.Message;
                return false;
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

            // Invalidate via cache dependency notification on the page node. Best-effort + reflective:
            // the dependency key/type for a page is version-dependent.
            if (TryNotifyCacheDependency(node.Id, out var detail))
            {
                response.Success = true;
                response.Message = "Invalidated output cache for page '" + (node.Title ?? node.Name) + "'. " + detail;
            }
            else
            {
                response.Success = false;
                response.Message = "Could not invalidate this page's output cache on this Sitefinity version. " +
                    "Use scope=whole or recycle instead.";
                response.Warnings.Add(detail);
            }
        }

        private bool TryNotifyCacheDependency(Guid pageNodeId, out string detail)
        {
            detail = string.Empty;

            try
            {
                var keyType = ResolveSitefinityType("Telerik.Sitefinity.Data.CacheDependencyKey");
                var depType = ResolveSitefinityType("Telerik.Sitefinity.Data.CacheDependency");

                if (keyType == null || depType == null)
                {
                    detail = "CacheDependency / CacheDependencyKey types not found.";
                    return false;
                }

                // new CacheDependencyKey { Key = id, Type = typeof(PageNode) }
                var key = Activator.CreateInstance(keyType);
                SetIfWritable(key, "Key", pageNodeId.ToString());
                SetIfWritable(key, "Type", typeof(PageNode));

                var listType = typeof(List<>).MakeGenericType(keyType);
                var list = (IList)Activator.CreateInstance(listType);
                list.Add(key);

                var notify = depType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Notify" && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsAssignableFrom(listType));

                if (notify == null)
                {
                    detail = "CacheDependency.Notify(IList<CacheDependencyKey>) not found.";
                    return false;
                }

                notify.Invoke(null, new object[] { list });
                detail = "Notified CacheDependency for PageNode key.";
                return true;
            }
            catch (Exception ex)
            {
                detail = "CacheDependency.Notify threw: " + ex.Message;
                return false;
            }
        }

        // ── Recycle (reflective — version tolerant) ──────────────────

        private static bool TryRestartApplication(string reason, out string detail)
        {
            detail = string.Empty;

            var candidates = typeof(SystemManager).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "RestartApplication")
                .OrderByDescending(m => m.GetParameters().Length) // prefer the richest overload
                .ToList();

            if (candidates.Count == 0)
            {
                detail = "SystemManager.RestartApplication not found.";
                return false;
            }

            foreach (var method in candidates)
            {
                try
                {
                    var pars = method.GetParameters();
                    var args = new object[pars.Length];
                    var ok = true;

                    for (var i = 0; i < pars.Length; i++)
                    {
                        var pt = pars[i].ParameterType;

                        if (pt == typeof(string))
                        {
                            args[i] = reason;
                        }
                        else if (pt == typeof(bool))
                        {
                            args[i] = true; // notify other nodes in a load-balanced setup
                        }
                        else if (pt.IsEnum)
                        {
                            args[i] = ResolveEnumDefault(pt);
                        }
                        else if (!pt.IsValueType)
                        {
                            args[i] = null; // e.g. OperationReason — null is acceptable
                        }
                        else
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (!ok)
                    {
                        continue;
                    }

                    method.Invoke(null, args);
                    detail = "Invoked RestartApplication(" + string.Join(", ", pars.Select(p => p.ParameterType.Name)) + ").";
                    return true;
                }
                catch (Exception ex)
                {
                    detail = "RestartApplication invocation threw: " + (ex.InnerException ?? ex).Message;
                }
            }

            return false;
        }

        private static object ResolveEnumDefault(Type enumType)
        {
            foreach (var name in new[] { "Default", "None" })
            {
                if (Enum.GetNames(enumType).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return Enum.Parse(enumType, name, true);
                }
            }

            return Enum.ToObject(enumType, 0);
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

            return nodes.FirstOrDefault(n =>
                string.Equals(n.Title ?? n.Name, identifier, StringComparison.OrdinalIgnoreCase));
        }

        private static Type ResolveSitefinityType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (Exception) { /* skip */ }
            }

            return null;
        }

        private static void SetIfWritable(object target, string propName, object value)
        {
            try
            {
                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(target, value, null);
                }
            }
            catch (Exception) { /* best-effort */ }
        }
    }
}

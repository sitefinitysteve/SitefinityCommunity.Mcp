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
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Reverse lookup ("where used") across the site: finds every page — and template — that
    /// references a given widget/controller type, content item, or page template. Sitefinity has no
    /// built-in view for this, so it is the safety check before deleting or refactoring shared
    /// resources. Read-only; iterates the same PageData.Controls path the other metadata endpoints use.
    /// </summary>
    [McpApiKey]
    public class McpWhereUsedService : Service
    {
        private const int MaxUsages = 500;

        /// <summary>
        /// GET /RestApi/mcp/where-used?Query=...&amp;Kind=widget|content|template
        /// </summary>
        public McpWhereUsedResponse Get(WhereUsed request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                throw HttpError.BadRequest("Query is required (a Guid or a widget/controller type name).");
            }

            var response = new McpWhereUsedResponse { Query = request.Query };

            try
            {
                var pageManager = PageManager.GetManager();
                pageManager.Provider.SuppressSecurityChecks = true;

                try
                {
                    var kind = ResolveKind(pageManager, request, response);
                    response.ResolvedKind = kind;

                    switch (kind)
                    {
                        case "template":
                            FindTemplateUsages(pageManager, request.Query, response);
                            break;
                        case "content":
                            FindContentUsages(pageManager, request.Query, response);
                            break;
                        case "widget":
                            FindWidgetTypeUsages(pageManager, request.Query, response);
                            break;
                        default:
                            response.Warnings.Add("Could not determine what '" + request.Query +
                                "' refers to. Pass kind=widget|content|template to disambiguate.");
                            break;
                    }
                }
                finally
                {
                    pageManager.Provider.SuppressSecurityChecks = false;
                }

                response.TotalUsages = response.Usages.Count;
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpError(HttpStatusCode.InternalServerError, "Error resolving where-used: " + ex.Message);
            }

            return response;
        }

        // ── Kind resolution ──────────────────────────────────────────

        private string ResolveKind(PageManager pageManager, WhereUsed request, McpWhereUsedResponse response)
        {
            if (!string.IsNullOrWhiteSpace(request.Kind))
            {
                var k = request.Kind.Trim().ToLowerInvariant();
                if (k == "widget" || k == "content" || k == "template")
                {
                    return k;
                }

                response.Warnings.Add("Unknown kind '" + request.Kind + "'; auto-detecting instead.");
            }

            Guid guid;
            if (Guid.TryParse(request.Query, out guid))
            {
                // A Guid is either a template id or a content item id. Probe templates first.
                var template = TryFindTemplate(pageManager, guid);
                if (template != null)
                {
                    response.ResolvedTitle = GetTemplateTitle(template);
                    return "template";
                }

                response.ResolvedTitle = "content item " + guid;
                return "content";
            }

            // A non-Guid token is a widget / controller type name.
            response.ResolvedTitle = request.Query;
            return "widget";
        }

        // ── Template usages ──────────────────────────────────────────

        private void FindTemplateUsages(PageManager pageManager, string query, McpWhereUsedResponse response)
        {
            Guid templateId;
            if (!Guid.TryParse(query, out templateId))
            {
                // Allow lookup by template name/title
                var byName = pageManager.GetTemplates().FirstOrDefault(t =>
                    string.Equals(t.Name, query, StringComparison.OrdinalIgnoreCase) ||
                    (t.Title != null && string.Equals(t.Title, query, StringComparison.OrdinalIgnoreCase)));

                if (byName == null)
                {
                    response.Warnings.Add("Template not found: " + query);
                    return;
                }

                templateId = byName.Id;
                response.ResolvedTitle = GetTemplateTitle(byName);
            }

            // Pages whose page data points at this template
            foreach (var node in GetFrontendNodes(pageManager))
            {
                if (response.Usages.Count >= MaxUsages)
                {
                    response.Warnings.Add("Result truncated at " + MaxUsages + " usages.");
                    return;
                }

                try
                {
                    var pageData = node.GetPageData();
                    if (pageData != null && pageData.Template != null && pageData.Template.Id == templateId)
                    {
                        response.Usages.Add(new McpWhereUsedItem
                        {
                            HostKind = "page",
                            HostId = node.Id.ToString(),
                            HostTitle = node.Title ?? node.Name ?? string.Empty,
                            HostUrl = SafeFullUrl(node),
                            MatchReason = "Page uses this template",
                        });
                    }
                }
                catch (Exception) { /* skip node */ }
            }

            // Child templates that inherit from this template
            try
            {
                var children = pageManager.GetTemplates()
                    .Where(t => t.BaseTemplate != null && t.BaseTemplate.Id == templateId)
                    .ToList();

                foreach (var child in children)
                {
                    response.Usages.Add(new McpWhereUsedItem
                    {
                        HostKind = "template",
                        HostId = child.Id.ToString(),
                        HostTitle = GetTemplateTitle(child),
                        MatchReason = "Template inherits from this template",
                    });
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not enumerate inheriting templates: " + ex.Message);
            }
        }

        // ── Content item usages ──────────────────────────────────────

        private void FindContentUsages(PageManager pageManager, string query, McpWhereUsedResponse response)
        {
            // Match the content item's Guid anywhere in a control's serialized property values.
            var needle = query.Trim();

            foreach (var node in GetFrontendNodes(pageManager))
            {
                if (response.Usages.Count >= MaxUsages)
                {
                    response.Warnings.Add("Result truncated at " + MaxUsages + " usages.");
                    return;
                }

                PageData pageData;
                try
                {
                    pageData = node.GetPageData();
                }
                catch (Exception)
                {
                    continue;
                }

                if (pageData == null || pageData.Controls == null)
                {
                    continue;
                }

                foreach (var control in pageData.Controls)
                {
                    try
                    {
                        var values = CollectControlValues(control);
                        var hitProp = values.FirstOrDefault(kv =>
                            kv.Value != null && kv.Value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (!string.IsNullOrEmpty(hitProp.Key))
                        {
                            response.Usages.Add(new McpWhereUsedItem
                            {
                                HostKind = "page",
                                HostId = node.Id.ToString(),
                                HostTitle = node.Title ?? node.Name ?? string.Empty,
                                HostUrl = SafeFullUrl(node),
                                WidgetId = control.Id.ToString(),
                                WidgetName = WidgetFriendlyName(control, values),
                                MatchReason = "Property '" + hitProp.Key + "' references this item",
                            });
                        }
                    }
                    catch (Exception) { /* skip control */ }
                }
            }
        }

        // ── Widget type usages ───────────────────────────────────────

        private void FindWidgetTypeUsages(PageManager pageManager, string query, McpWhereUsedResponse response)
        {
            var needle = query.Trim();

            foreach (var node in GetFrontendNodes(pageManager))
            {
                if (response.Usages.Count >= MaxUsages)
                {
                    response.Warnings.Add("Result truncated at " + MaxUsages + " usages.");
                    return;
                }

                PageData pageData;
                try
                {
                    pageData = node.GetPageData();
                }
                catch (Exception)
                {
                    continue;
                }

                if (pageData == null || pageData.Controls == null)
                {
                    continue;
                }

                foreach (var control in pageData.Controls)
                {
                    try
                    {
                        var objectType = control.ObjectType ?? string.Empty;
                        var values = CollectControlValues(control);
                        string controllerName;
                        values.TryGetValue("ControllerName", out controllerName);
                        controllerName = controllerName ?? string.Empty;

                        var reason = MatchWidgetType(needle, objectType, controllerName);
                        if (reason != null)
                        {
                            response.Usages.Add(new McpWhereUsedItem
                            {
                                HostKind = "page",
                                HostId = node.Id.ToString(),
                                HostTitle = node.Title ?? node.Name ?? string.Empty,
                                HostUrl = SafeFullUrl(node),
                                WidgetId = control.Id.ToString(),
                                WidgetName = WidgetFriendlyName(control, values),
                                MatchReason = reason,
                            });
                        }
                    }
                    catch (Exception) { /* skip control */ }
                }
            }
        }

        private static string MatchWidgetType(string needle, string objectType, string controllerName)
        {
            if (!string.IsNullOrEmpty(controllerName))
            {
                if (string.Equals(controllerName, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ShortName(controllerName), needle, StringComparison.OrdinalIgnoreCase))
                {
                    return "ControllerName matches '" + ShortName(controllerName) + "'";
                }

                if (controllerName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "ControllerName contains '" + needle + "'";
                }
            }

            if (!string.IsNullOrEmpty(objectType))
            {
                if (string.Equals(objectType, needle, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ShortName(objectType), needle, StringComparison.OrdinalIgnoreCase))
                {
                    return "Widget type matches '" + ShortName(objectType) + "'";
                }

                if (objectType.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Widget type contains '" + needle + "'";
                }
            }

            return null;
        }

        // ── Shared helpers ───────────────────────────────────────────

        private static IEnumerable<PageNode> GetFrontendNodes(PageManager pageManager)
        {
            var backendRootId = SiteInitializer.BackendRootNodeId;
            return pageManager.GetPageNodes()
                .Where(p => p.RootNodeId != backendRootId && !p.IsDeleted)
                .Where(p => p.NodeType == NodeType.Standard)
                .ToList();
        }

        /// <summary>
        /// Merged Level 1 + Level 2 (Settings children) property values for a control, mirroring the
        /// page-widget-tree merge so references stored in either tier are found.
        /// </summary>
        private static Dictionary<string, string> CollectControlValues(PageControl control)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (control == null || control.Properties == null)
            {
                return values;
            }

            foreach (var prop in control.Properties)
            {
                if (string.Equals(prop.Name, "Settings", StringComparison.OrdinalIgnoreCase))
                {
                    if (prop.ChildProperties != null)
                    {
                        foreach (var child in prop.ChildProperties)
                        {
                            values[child.Name] = child.Value ?? string.Empty;
                        }
                    }

                    continue;
                }

                values[prop.Name] = prop.Value ?? string.Empty;
            }

            return values;
        }

        private static string WidgetFriendlyName(PageControl control, Dictionary<string, string> values)
        {
            string controllerName;
            if (values != null && values.TryGetValue("ControllerName", out controllerName)
                && !string.IsNullOrEmpty(controllerName))
            {
                return ShortName(controllerName);
            }

            return ShortName(control.ObjectType ?? string.Empty);
        }

        private static PageTemplate TryFindTemplate(PageManager pageManager, Guid id)
        {
            try
            {
                return pageManager.GetTemplates().FirstOrDefault(t => t.Id == id);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string GetTemplateTitle(PageTemplate template)
        {
            if (template == null)
            {
                return string.Empty;
            }

            if (template.Title != null && !string.IsNullOrEmpty(template.Title.ToString()))
            {
                return template.Title.ToString();
            }

            return template.Name ?? template.Id.ToString();
        }

        private static string SafeFullUrl(PageNode node)
        {
            try
            {
                var url = node.GetFullUrl() ?? string.Empty;

                if (url.StartsWith("~/"))
                {
                    url = url.Substring(1);
                }

                if (!string.IsNullOrEmpty(url) && !url.StartsWith("/"))
                {
                    url = "/" + url;
                }

                return url;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static string ShortName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return fullName ?? string.Empty;
            }

            var lastDot = fullName.LastIndexOf('.');
            return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
        }
    }
}

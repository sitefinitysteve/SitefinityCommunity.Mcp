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
using Telerik.Sitefinity.Data;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Reverse lookup ("where used") across the whole site. Finds every page AND template that
    /// references a given widget/controller type, content item, or arbitrary property value, plus the
    /// templates that inherit a given template. Sitefinity ships no such view, so this is the safety
    /// check before deleting or refactoring a shared resource ("what breaks if I change this?").
    ///
    /// Comprehensive by design — in a single elevated pass it scans page controls AND template controls
    /// (both derive from <see cref="ControlData"/>), and because a widget living on a template implicitly
    /// renders on every page that rides that template, a template-hosted match is expanded into the
    /// affected pages, transitively through template inheritance. Read-only.
    /// </summary>
    [McpApiKey]
    public class McpWhereUsedService : Service
    {
        private const int MaxUsages = 1000;
        private const int SnippetPad = 50;

        /// <summary>
        /// GET /RestApi/mcp/where-used?Query=...&amp;Kind=widget|content|template|property
        /// </summary>
        public McpWhereUsedResponse Get(WhereUsed request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.WhereUsed);

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                throw HttpError.BadRequest("Query is required (a widget/controller type, a content Guid, " +
                    "a template id/name, or — with Kind=property — any substring to find in widget values).");
            }

            var response = new McpWhereUsedResponse { Query = request.Query };

            try
            {
                var pageManager = PageManager.GetManager();

                // ElevatedModeRegion bypasses security trimming for the duration of the scan and restores
                // it on dispose — the MCP request has no Sitefinity user session to authorize against.
                using (new ElevatedModeRegion(pageManager))
                {
                    var kind = ResolveKind(pageManager, request, response);
                    response.ResolvedKind = kind;

                    // One pass builds the site model (template inheritance + page→template map) and, for the
                    // reference kinds, collects every matching control on every page and template.
                    var model = ScanSite(pageManager, kind, request.Query.Trim(), response);

                    switch (kind)
                    {
                        case "template":
                            FindTemplateUsages(request.Query.Trim(), model, request.TemplateHostsOnly, response);
                            break;
                        case "widget":
                        case "content":
                        case "property":
                            EmitReferenceUsages(model, request.TemplateHostsOnly, response);
                            break;
                        default:
                            response.Warnings.Add("Could not determine what '" + request.Query +
                                "' refers to. Pass Kind=widget|content|template|property to disambiguate.");
                            break;
                    }
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
                if (k == "widget" || k == "content" || k == "template" || k == "property")
                {
                    return k;
                }

                response.Warnings.Add("Unknown Kind '" + request.Kind + "'; auto-detecting instead.");
            }

            Guid guid;
            if (Guid.TryParse(request.Query, out guid))
            {
                // A Guid is either a template id or a content item id. Probe templates first.
                var template = TryFindTemplate(pageManager, guid);
                if (template != null)
                {
                    response.ResolvedTitle = TemplateTitle(template);
                    return "template";
                }

                response.ResolvedTitle = "content item " + guid;
                return "content";
            }

            // A non-Guid token is a widget / controller type name. (Kind=property is opt-in only.)
            response.ResolvedTitle = request.Query;
            return "widget";
        }

        // ── Single-pass site scan ────────────────────────────────────

        /// <summary>
        /// Walks every template and every frontend page once. Always records template-inheritance edges
        /// and the page→template map (needed to expand template matches into affected pages). For the
        /// reference kinds it also matches each control, collecting template matches (grouped by template)
        /// and direct page usages. Per-host failures are counted, never fatal.
        /// </summary>
        private SiteModel ScanSite(PageManager pageManager, string kind, string needle, McpWhereUsedResponse response)
        {
            var model = new SiteModel();
            var scanControls = kind == "widget" || kind == "content" || kind == "property";

            // Templates
            foreach (var tmpl in pageManager.GetTemplates().ToList())
            {
                try
                {
                    var id = tmpl.Id;
                    var host = new TemplateHost
                    {
                        Id = id,
                        Title = TemplateTitle(tmpl),
                        ParentId = SafeParentTemplateId(tmpl),
                    };
                    model.Templates[id] = host;
                    response.ScannedTemplates++;

                    if (host.ParentId != Guid.Empty)
                    {
                        List<Guid> children;
                        if (!model.TemplateChildren.TryGetValue(host.ParentId, out children))
                        {
                            children = new List<Guid>();
                            model.TemplateChildren[host.ParentId] = children;
                        }

                        children.Add(id);
                    }

                    if (scanControls && tmpl.Controls != null)
                    {
                        var matches = MatchControls(tmpl.Controls, kind, needle, "template", id.ToString(), host.Title, null).ToList();
                        if (matches.Count > 0)
                        {
                            model.TemplateMatches[id] = matches;
                        }
                    }
                }
                catch (Exception)
                {
                    response.SkippedHosts++;
                }
            }

            // Pages — load ALL frontend non-deleted nodes once and index them by id. GetPageNodes()
            // materializes each node's scalar UrlName/ParentId/RootNodeId, so the URL can be rebuilt by an
            // in-memory parent-chain walk over this index (see BuildUrl) — NOT node.GetFullUrl(), which
            // lazy-loads every ancestor from the database and was, by far, the scan's dominant cost.
            // (Group/redirect ancestors are kept in the index so the walk can resolve them; only Standard
            // nodes are actually scanned.)
            var backendRootId = SiteInitializer.BackendRootNodeId;
            var frontendNodes = pageManager.GetPageNodes()
                .Where(p => p.RootNodeId != backendRootId && !p.IsDeleted)
                .ToList();

            var nodesById = new Dictionary<Guid, PageNode>();
            foreach (var n in frontendNodes)
            {
                nodesById[n.Id] = n;
            }

            foreach (var node in frontendNodes)
            {
                if (node.NodeType != NodeType.Standard)
                {
                    continue;
                }

                try
                {
                    var pageData = node.GetPageData();
                    if (pageData == null)
                    {
                        continue;
                    }

                    response.ScannedPages++;

                    var page = new PageHost
                    {
                        Id = node.Id,
                        Title = node.Title ?? node.Name ?? string.Empty,
                        Url = BuildUrl(node, nodesById),
                        TemplateId = pageData.Template != null ? pageData.Template.Id : Guid.Empty,
                    };
                    model.Pages.Add(page);

                    if (page.TemplateId != Guid.Empty)
                    {
                        List<PageHost> pages;
                        if (!model.PagesByTemplate.TryGetValue(page.TemplateId, out pages))
                        {
                            pages = new List<PageHost>();
                            model.PagesByTemplate[page.TemplateId] = pages;
                        }

                        pages.Add(page);
                    }

                    if (scanControls && pageData.Controls != null)
                    {
                        model.DirectPageUsages.AddRange(
                            MatchControls(pageData.Controls, kind, needle, "page", node.Id.ToString(), page.Title, page.Url));
                    }
                }
                catch (Exception)
                {
                    response.SkippedHosts++;
                }
            }

            return model;
        }

        // ── Reference usages (widget / content / property) ───────────

        /// <summary>
        /// Emits direct page matches, then each template match — and, unless TemplateHostsOnly, expands
        /// every template match into the pages that ride that template (and any template inheriting it).
        /// </summary>
        private void EmitReferenceUsages(SiteModel model, bool templateHostsOnly, McpWhereUsedResponse response)
        {
            // Widgets placed directly on a page.
            foreach (var usage in model.DirectPageUsages)
            {
                if (AddUsage(response, usage))
                {
                    response.PageUsageCount++;
                }
            }

            // Widgets placed on a template.
            foreach (var kv in model.TemplateMatches)
            {
                var templateId = kv.Key;
                var matches = kv.Value;

                foreach (var usage in matches)
                {
                    if (AddUsage(response, usage))
                    {
                        response.TemplateUsageCount++;
                    }
                }

                if (templateHostsOnly)
                {
                    continue;
                }

                // Every page riding this template (or a template that inherits it) inherits the match.
                var affectedTemplateIds = TemplateSubtree(templateId, model);
                var identity = matches[0];
                var hostTitle = model.Templates.ContainsKey(templateId) ? model.Templates[templateId].Title : templateId.ToString();

                foreach (var affectedTemplateId in affectedTemplateIds)
                {
                    List<PageHost> pages;
                    if (!model.PagesByTemplate.TryGetValue(affectedTemplateId, out pages))
                    {
                        continue;
                    }

                    foreach (var page in pages)
                    {
                        var item = new McpWhereUsedItem
                        {
                            HostKind = "page",
                            HostId = page.Id.ToString(),
                            HostTitle = page.Title,
                            HostUrl = page.Url,
                            WidgetId = identity.WidgetId,
                            WidgetName = identity.WidgetName,
                            ControllerName = identity.ControllerName,
                            ObjectType = identity.ObjectType,
                            Origin = identity.Origin,
                            MatchReason = matches.Count == 1
                                ? "Inherited from template '" + hostTitle + "'"
                                : "Inherits " + matches.Count + " matching widget(s) from template '" + hostTitle + "'",
                            ViaTemplateId = templateId.ToString(),
                            ViaTemplateTitle = hostTitle,
                        };

                        if (AddUsage(response, item))
                        {
                            response.InheritedPageCount++;
                        }
                    }
                }
            }
        }

        // ── Template usages ──────────────────────────────────────────

        private void FindTemplateUsages(string query, SiteModel model, bool templateHostsOnly, McpWhereUsedResponse response)
        {
            Guid templateId;
            if (!Guid.TryParse(query, out templateId) || !model.Templates.ContainsKey(templateId))
            {
                // Resolve by name/title against the scanned templates.
                var byName = model.Templates.Values.FirstOrDefault(t =>
                    string.Equals(t.Title, query, StringComparison.OrdinalIgnoreCase));

                if (byName == null)
                {
                    response.Warnings.Add("Template not found: " + query);
                    return;
                }

                templateId = byName.Id;
            }

            response.ResolvedTitle = model.Templates[templateId].Title;

            // Templates that inherit (transitively) from the target.
            var subtree = TemplateSubtree(templateId, model);
            foreach (var childId in subtree)
            {
                if (childId == templateId || !model.Templates.ContainsKey(childId))
                {
                    continue;
                }

                var child = model.Templates[childId];
                var added = AddUsage(response, new McpWhereUsedItem
                {
                    HostKind = "template",
                    HostId = childId.ToString(),
                    HostTitle = child.Title,
                    MatchReason = "Template inherits from this template",
                });

                if (added)
                {
                    response.TemplateUsageCount++;
                }
            }

            if (templateHostsOnly)
            {
                return;
            }

            // Pages riding the target template directly.
            EmitTemplatePages(model, templateId, templateId, "Page uses this template", false, response);

            // Pages riding a template that inherits the target.
            foreach (var childId in subtree)
            {
                if (childId == templateId)
                {
                    continue;
                }

                var childTitle = model.Templates.ContainsKey(childId) ? model.Templates[childId].Title : childId.ToString();
                EmitTemplatePages(model, childId, childId, "Page uses inheriting template '" + childTitle + "'", true, response);
            }
        }

        private void EmitTemplatePages(SiteModel model, Guid templateId, Guid viaTemplateId, string reason, bool inherited, McpWhereUsedResponse response)
        {
            List<PageHost> pages;
            if (!model.PagesByTemplate.TryGetValue(templateId, out pages))
            {
                return;
            }

            var viaTitle = model.Templates.ContainsKey(viaTemplateId) ? model.Templates[viaTemplateId].Title : null;

            foreach (var page in pages)
            {
                var added = AddUsage(response, new McpWhereUsedItem
                {
                    HostKind = "page",
                    HostId = page.Id.ToString(),
                    HostTitle = page.Title,
                    HostUrl = page.Url,
                    MatchReason = reason,
                    ViaTemplateId = inherited ? viaTemplateId.ToString() : null,
                    ViaTemplateTitle = inherited ? viaTitle : null,
                });

                if (!added)
                {
                    continue;
                }

                if (inherited)
                {
                    response.InheritedPageCount++;
                }
                else
                {
                    response.PageUsageCount++;
                }
            }
        }

        /// <summary>
        /// The target template plus every template that inherits it, transitively. Cycle-guarded.
        /// </summary>
        private static HashSet<Guid> TemplateSubtree(Guid rootId, SiteModel model)
        {
            var result = new HashSet<Guid> { rootId };
            var queue = new Queue<Guid>();
            queue.Enqueue(rootId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                List<Guid> children;
                if (!model.TemplateChildren.TryGetValue(current, out children))
                {
                    continue;
                }

                foreach (var childId in children)
                {
                    if (result.Add(childId))
                    {
                        queue.Enqueue(childId);
                    }
                }
            }

            return result;
        }

        // ── Control matching ─────────────────────────────────────────

        /// <summary>
        /// Matches every control on a host against the query. Works for both pages and templates because
        /// <see cref="PageControl"/> and <see cref="TemplateControl"/> share the <see cref="ControlData"/>
        /// base (and <c>IEnumerable&lt;T&gt;</c> covariance lets either list bind to ControlData here).
        /// </summary>
        private IEnumerable<McpWhereUsedItem> MatchControls(
            IEnumerable<ControlData> controls, string kind, string needle,
            string hostKind, string hostId, string hostTitle, string hostUrl)
        {
            foreach (var control in controls)
            {
                McpWhereUsedItem item = null;

                try
                {
                    var values = CollectControlValues(control);
                    var objectType = control.ObjectType ?? string.Empty;
                    string controllerName;
                    values.TryGetValue("ControllerName", out controllerName);
                    controllerName = controllerName ?? string.Empty;

                    if (kind == "widget")
                    {
                        var reason = MatchWidgetType(needle, objectType, controllerName);
                        if (reason != null)
                        {
                            item = BuildItem(hostKind, hostId, hostTitle, hostUrl, control, values, objectType, controllerName, reason);
                        }
                    }
                    else
                    {
                        // content (Guid) and property (arbitrary substring) both scan property VALUES.
                        var hit = values.FirstOrDefault(kv =>
                            !string.IsNullOrEmpty(kv.Value) &&
                            kv.Value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (!string.IsNullOrEmpty(hit.Key))
                        {
                            var reason = kind == "content"
                                ? "Property '" + hit.Key + "' references this item"
                                : "Property '" + hit.Key + "' contains the search term";

                            item = BuildItem(hostKind, hostId, hostTitle, hostUrl, control, values, objectType, controllerName, reason);
                            item.MatchedProperty = hit.Key;
                            item.MatchSnippet = Snippet(hit.Value, needle);
                        }
                    }
                }
                catch (Exception)
                {
                    item = null; // skip a malformed control rather than abort the host
                }

                if (item != null)
                {
                    yield return item;
                }
            }
        }

        private static McpWhereUsedItem BuildItem(
            string hostKind, string hostId, string hostTitle, string hostUrl,
            ControlData control, Dictionary<string, string> values, string objectType, string controllerName, string reason)
        {
            return new McpWhereUsedItem
            {
                HostKind = hostKind,
                HostId = hostId,
                HostTitle = hostTitle,
                HostUrl = hostUrl,
                WidgetId = control.Id.ToString(),
                WidgetName = WidgetFriendlyName(objectType, controllerName),
                ControllerName = string.IsNullOrEmpty(controllerName) ? null : controllerName,
                ObjectType = objectType,
                Origin = DeriveOrigin(controllerName, objectType),
                PlaceHolder = control.PlaceHolder ?? string.Empty,
                MatchReason = reason,
            };
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

        private bool AddUsage(McpWhereUsedResponse response, McpWhereUsedItem item)
        {
            if (response.Usages.Count >= MaxUsages)
            {
                if (!response.Warnings.Any(w => w.StartsWith("Result truncated")))
                {
                    response.Warnings.Add("Result truncated at " + MaxUsages + " usages; refine the query (counts above the cap are not reported).");
                }

                return false;
            }

            response.Usages.Add(item);
            return true;
        }

        /// <summary>
        /// Merged Level 1 + Level 2 (Settings children) property values for a control, mirroring the
        /// page-widget-tree merge so references stored in either tier are found.
        /// </summary>
        private static Dictionary<string, string> CollectControlValues(ControlData control)
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

        private static string WidgetFriendlyName(string objectType, string controllerName)
        {
            if (!string.IsNullOrEmpty(controllerName))
            {
                return ShortName(controllerName);
            }

            return ShortName(objectType ?? string.Empty);
        }

        /// <summary>Provenance of a widget by its controller/type namespace.</summary>
        private static string DeriveOrigin(string controllerName, string objectType)
        {
            var s = !string.IsNullOrEmpty(controllerName) ? controllerName : (objectType ?? string.Empty);

            if (s.IndexOf("Medportal", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "medportal";
            }

            if (s.IndexOf("Telerik.Sitefinity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "sitefinity";
            }

            return "unknown";
        }

        private static string Snippet(string value, string needle)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var idx = value.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                return value.Length <= SnippetPad * 2 ? value : value.Substring(0, SnippetPad * 2) + "...";
            }

            var start = Math.Max(0, idx - SnippetPad);
            var end = Math.Min(value.Length, idx + needle.Length + SnippetPad);
            var snippet = value.Substring(start, end - start);

            if (start > 0)
            {
                snippet = "..." + snippet;
            }

            if (end < value.Length)
            {
                snippet = snippet + "...";
            }

            return snippet;
        }

        private static PageTemplate TryFindTemplate(PageManager pageManager, Guid id)
        {
            try
            {
                return pageManager.GetTemplate(id);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Guid SafeParentTemplateId(PageTemplate template)
        {
            try
            {
                return template.ParentTemplate != null ? template.ParentTemplate.Id : Guid.Empty;
            }
            catch (Exception)
            {
                return Guid.Empty;
            }
        }

        private static string TemplateTitle(PageTemplate template)
        {
            if (template == null)
            {
                return string.Empty;
            }

            var title = template.Title == null ? null : template.Title.ToString();
            if (!string.IsNullOrEmpty(title))
            {
                return title;
            }

            return template.Name ?? template.Id.ToString();
        }

        /// <summary>
        /// Rebuilds a page node's site URL by walking the parent chain in memory over the pre-loaded node
        /// index. The scalar UrlName/ParentId/RootNodeId are already materialized, so this issues NO
        /// database queries — unlike <c>PageNode.GetFullUrl()</c>, which lazy-loads each ancestor
        /// and dominated the scan time. Cycle-guarded; stops at (and excludes) the site root.
        /// </summary>
        private static string BuildUrl(PageNode node, Dictionary<Guid, PageNode> nodesById)
        {
            var segments = new List<string>();
            var rootId = node.RootNodeId;
            var current = node;
            var guard = 0;

            while (current != null && current.Id != rootId && guard++ < 64)
            {
                if (!string.IsNullOrEmpty(current.UrlName))
                {
                    segments.Insert(0, current.UrlName);
                }

                PageNode parent;
                current = nodesById.TryGetValue(current.ParentId, out parent) ? parent : null;
            }

            return "/" + string.Join("/", segments);
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

        // ── In-memory site model ─────────────────────────────────────

        private sealed class SiteModel
        {
            public Dictionary<Guid, TemplateHost> Templates { get; } = new Dictionary<Guid, TemplateHost>();
            public Dictionary<Guid, List<Guid>> TemplateChildren { get; } = new Dictionary<Guid, List<Guid>>();
            public Dictionary<Guid, List<McpWhereUsedItem>> TemplateMatches { get; } = new Dictionary<Guid, List<McpWhereUsedItem>>();
            public Dictionary<Guid, List<PageHost>> PagesByTemplate { get; } = new Dictionary<Guid, List<PageHost>>();
            public List<PageHost> Pages { get; } = new List<PageHost>();
            public List<McpWhereUsedItem> DirectPageUsages { get; } = new List<McpWhereUsedItem>();
        }

        private sealed class TemplateHost
        {
            public Guid Id { get; set; }
            public string Title { get; set; }
            public Guid ParentId { get; set; }
        }

        private sealed class PageHost
        {
            public Guid Id { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
            public Guid TemplateId { get; set; }
        }
    }
}

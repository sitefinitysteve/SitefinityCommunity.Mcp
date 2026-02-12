// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using Telerik.Sitefinity.Abstractions;
using Telerik.Sitefinity.DynamicModules.Builder;
using Telerik.Sitefinity.DynamicModules.Builder.Model;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;
using Telerik.Sitefinity.Web;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// ServiceStack service exposing Sitefinity metadata via REST API.
    /// All endpoints require [McpApiKey] authentication.
    /// </summary>
    [McpApiKey]
    public class McpMetadataService : Service
    {
        /// <summary>
        /// GET /RestApi/mcp/site-info — Sitefinity version, .NET version, project name, multisite info.
        /// </summary>
        public McpSiteInfoResponse Get(GetSiteInfo request)
        {
            var sfAssembly = typeof(SystemManager).Assembly.GetName();

            var response = new McpSiteInfoResponse
            {
                SitefinityVersion = sfAssembly.Version != null ? sfAssembly.Version.ToString() : "Unknown",
                DotNetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ProjectName = SystemManager.CurrentContext.CurrentSite.Name,
                ModuleCount = SystemManager.ApplicationModules.Count
            };

            // Configured languages
            try
            {
                var cultures = SystemManager.CurrentContext.SystemCultures;
                if (cultures != null)
                {
                    foreach (var culture in cultures)
                    {
                        response.Languages.Add(culture.Name + " (" + culture.DisplayName + ")");
                    }
                }
            }
            catch (Exception)
            {
                // Language info not available
            }

            // Multisite info — try-catch for environments without multisite license
            try
            {
                var multisiteManager = Telerik.Sitefinity.Multisite.MultisiteManager.GetManager();
                var sites = multisiteManager.GetSites().ToList();
                foreach (var site in sites)
                {
                    response.Sites.Add(new McpSiteEntry
                    {
                        Name = site.Name,
                        LiveUrl = site.LiveUrl ?? string.Empty,
                        IsDefault = site.IsDefault
                    });
                }
            }
            catch (Exception)
            {
                // Multisite not licensed or not available — single site
                response.Sites.Add(new McpSiteEntry
                {
                    Name = response.ProjectName,
                    LiveUrl = string.Empty,
                    IsDefault = true
                });
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/modules — All installed modules with type and status.
        /// </summary>
        public List<McpModuleInfo> Get(ListModules request)
        {
            var modules = new List<McpModuleInfo>();

            foreach (var kvp in SystemManager.ApplicationModules)
            {
                var module = kvp.Value;
                var moduleType = "Custom";

                var fullName = module.GetType().FullName ?? string.Empty;
                if (fullName.StartsWith("Telerik.Sitefinity.DynamicModules"))
                    moduleType = "Dynamic";
                else if (fullName.StartsWith("Telerik.Sitefinity."))
                    moduleType = "System";

                modules.Add(new McpModuleInfo
                {
                    Name = kvp.Key,
                    Type = moduleType,
                    StartupType = module.GetType().Name,
                    Status = "Loaded"
                });
            }

            return modules.OrderBy(m => m.Name).ToList();
        }

        /// <summary>
        /// GET /RestApi/mcp/dynamic-types — All Module Builder types grouped by module.
        /// </summary>
        public List<McpDynamicTypeInfo> Get(ListDynamicTypes request)
        {
            var result = new List<McpDynamicTypeInfo>();

            try
            {
                var manager = ModuleBuilderManager.GetManager();

                // Query modules and types separately via GetItems — the Types navigation property can be null
                var dynamicModules = manager.GetItems(typeof(DynamicModule), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModule>().ToList();
                var allTypes = manager.GetItems(typeof(DynamicModuleType), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModuleType>().ToList();

                // Build module name lookup
                var moduleNames = dynamicModules.ToDictionary(m => m.Id, m => m.Name);

                foreach (var dynType in allTypes)
                {
                    string moduleName;
                    if (!moduleNames.TryGetValue(dynType.ParentModuleId, out moduleName))
                        moduleName = "Unknown";

                    result.Add(new McpDynamicTypeInfo
                    {
                        ModuleName = moduleName,
                        TypeName = dynType.DisplayName,
                        TypeFullName = dynType.GetFullTypeName(),
                        FieldCount = dynType.Fields != null ? dynType.Fields.Count() : 0
                    });
                }
            }
            catch (Exception)
            {
                // Module Builder not available
            }

            return result.OrderBy(t => t.ModuleName).ThenBy(t => t.TypeName).ToList();
        }

        /// <summary>
        /// GET /RestApi/mcp/dynamic-types/{TypeFullName}/fields — Fields for a specific dynamic type.
        /// </summary>
        public List<McpDynamicFieldInfo> Get(GetTypeFields request)
        {
            if (string.IsNullOrWhiteSpace(request.TypeFullName))
                throw HttpError.BadRequest("TypeFullName is required.");

            var result = new List<McpDynamicFieldInfo>();

            try
            {
                var manager = ModuleBuilderManager.GetManager();
                var allTypes = manager.GetItems(typeof(DynamicModuleType), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModuleType>().ToList();

                DynamicModuleType targetType = null;
                foreach (var dynType in allTypes)
                {
                    if (string.Equals(dynType.GetFullTypeName(), request.TypeFullName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetType = dynType;
                        break;
                    }
                }

                if (targetType == null)
                    throw HttpError.NotFound("Dynamic type not found: " + request.TypeFullName);

                if (targetType.Fields != null)
                {
                    var mainFieldName = targetType.MainShortTextFieldName;

                    foreach (var field in targetType.Fields)
                    {
                        var isMain = !string.IsNullOrEmpty(mainFieldName)
                            && string.Equals(field.Name, mainFieldName, StringComparison.OrdinalIgnoreCase);

                        // Resolve classification name from ClassificationId if present
                        string classificationName = string.Empty;
                        if (field.ClassificationId != Guid.Empty)
                        {
                            try
                            {
                                var taxonomyManager = Telerik.Sitefinity.Taxonomies.TaxonomyManager.GetManager();
                                var taxonomy = taxonomyManager.GetTaxonomy(field.ClassificationId);
                                if (taxonomy != null)
                                    classificationName = taxonomy.Title;
                            }
                            catch (Exception)
                            {
                                classificationName = field.ClassificationId.ToString();
                            }
                        }

                        result.Add(new McpDynamicFieldInfo
                        {
                            Name = field.Name,
                            Title = field.Title ?? field.Name,
                            FieldType = field.TypeUIName ?? field.FieldType.ToString(),
                            IsRequired = field.IsRequired,
                            IsMainField = isMain,
                            ClassificationName = classificationName,
                            RelatedDataType = field.RelatedDataType ?? string.Empty
                        });
                    }
                }
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpError(HttpStatusCode.InternalServerError, "Error reading type fields: " + ex.Message);
            }

            return result;
        }

        /// <summary>
        /// GET /RestApi/mcp/page-routes — CMS page routes via cached SiteMap (elevated context).
        /// </summary>
        public McpPageRoutesResponse Get(ListPageRoutes request)
        {
            var response = new McpPageRoutesResponse();

            try
            {
                response.PageRoutes = GetPageRoutes(out var warnings);
                response.Warnings.AddRange(warnings);
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error enumerating page routes: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/api-routes — ServiceStack API routes and OData entity sets.
        /// </summary>
        public McpApiRoutesResponse Get(ListApiRoutes request)
        {
            var response = new McpApiRoutesResponse();

            try
            {
                response.ServiceStackRoutes = GetServiceStackRoutes(out var warnings);
                response.Warnings.AddRange(warnings);
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error enumerating ServiceStack routes: " + ex.Message);
            }

            // ── OData Entity Sets ────────────────────────────────────
            try
            {
                var baseUrl = new Uri(base.Request.AbsoluteUri).GetLeftPart(UriPartial.Authority);
                var odataUrl = baseUrl + "/api/default";

                using (var webClient = new System.Net.WebClient())
                {
                    var json = webClient.DownloadString(odataUrl);
                    var serializer = new JavaScriptSerializer();
                    var doc = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (doc != null && doc.ContainsKey("value"))
                    {
                        var items = doc["value"] as System.Collections.ArrayList;
                        if (items != null)
                        {
                            foreach (var item in items)
                            {
                                var entry = item as Dictionary<string, object>;
                                if (entry != null && entry.ContainsKey("name"))
                                {
                                    var name = entry["name"]?.ToString() ?? string.Empty;
                                    response.ODataRoutes.Add(new McpODataRoute
                                    {
                                        EntitySetName = name,
                                        EntitySetUrl = "/api/default/" + name
                                    });
                                }
                            }
                        }
                    }
                }

                response.ODataRoutes = response.ODataRoutes.OrderBy(r => r.EntitySetName).ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not discover OData entity sets: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/page-details — Detailed page info including widgets and properties.
        /// </summary>
        public McpPageDetailsResponse Get(GetPageDetails request)
        {
            if (string.IsNullOrWhiteSpace(request.PageIdentifier))
                throw HttpError.BadRequest("PageIdentifier is required.");

            var response = new McpPageDetailsResponse();

            try
            {
                var pageManager = PageManager.GetManager();
                pageManager.Provider.SuppressSecurityChecks = true;

                try
                {
                    var node = ResolvePageNode(pageManager, request.PageIdentifier, response.Warnings);
                    if (node == null)
                        throw HttpError.NotFound("Page not found: " + request.PageIdentifier);

                    response.Id = node.Id.ToString();
                    response.Title = node.Title ?? node.Name ?? string.Empty;
                    response.UrlName = node.UrlName ?? string.Empty;
                    response.NodeType = node.NodeType.ToString();
                    response.IsPublished = string.Equals(
                        node.ApprovalWorkflowState, "Published",
                        StringComparison.OrdinalIgnoreCase);

                    // URL
                    var url = node.GetFullUrl() ?? string.Empty;
                    if (string.IsNullOrEmpty(url))
                        url = "/" + (node.UrlName ?? string.Empty);
                    if (url.StartsWith("~/"))
                        url = url.Substring(1);
                    if (!url.StartsWith("/"))
                        url = "/" + url;
                    response.Url = url;

                    // Depth
                    var depth = 0;
                    var parent = node.Parent;
                    while (parent != null)
                    {
                        depth++;
                        parent = parent.Parent;
                    }
                    response.Depth = depth;

                    // Page data (template, description, widgets)
                    var pageData = node.GetPageData();
                    if (pageData != null)
                    {
                        response.PageDataId = pageData.Id.ToString();

                        if (pageData.Template != null)
                            response.TemplateName = pageData.Template.Name ?? string.Empty;

                        response.Description = pageData.Description ?? string.Empty;

                        // Widgets
                        if (pageData.Controls != null)
                        {
                            foreach (var control in pageData.Controls)
                            {
                                try
                                {
                                    var widget = new McpPageWidgetInfo
                                    {
                                        ObjectType = control.ObjectType ?? string.Empty,
                                        PlaceHolder = control.PlaceHolder ?? string.Empty,
                                        Caption = control.Caption ?? string.Empty,
                                        IsLayoutControl = control.IsLayoutControl
                                    };

                                    // Derive friendly names
                                    widget.WidgetName = ExtractWidgetName(widget.ObjectType);
                                    widget.FriendlyName = widget.WidgetName;

                                    // Extract all properties
                                    if (control.Properties != null)
                                    {
                                        foreach (var prop in control.Properties)
                                        {
                                            var val = prop.Value ?? string.Empty;
                                            if (val.Length > 500)
                                                val = val.Substring(0, 500) + "... (truncated)";
                                            widget.Properties[prop.Name] = val;
                                        }

                                        // For MVC widgets, extract controller name as friendly name
                                        string controllerName;
                                        if (widget.Properties.TryGetValue("ControllerName", out controllerName)
                                            && !string.IsNullOrEmpty(controllerName))
                                        {
                                            widget.FriendlyName = controllerName;
                                        }
                                    }

                                    response.Widgets.Add(widget);
                                }
                                catch (Exception)
                                {
                                    // Skip individual widget errors
                                }
                            }
                        }
                    }
                    else
                    {
                        response.Warnings.Add("No page data found (page may be a group node or redirect).");
                    }

                }
                finally
                {
                    pageManager.Provider.SuppressSecurityChecks = false;
                }
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpError(HttpStatusCode.InternalServerError, "Error reading page details: " + ex.Message);
            }

            return response;
        }

        // ── Private Helpers ──────────────────────────────────────────

        /// <summary>
        /// Resolves a page node from a flexible identifier: Guid, URL path, UrlName slug, or title.
        /// </summary>
        private PageNode ResolvePageNode(PageManager pageManager, string identifier, List<string> warnings)
        {
            // 1. Try as Guid
            Guid pageGuid;
            if (Guid.TryParse(identifier, out pageGuid))
            {
                try
                {
                    var node = pageManager.GetPageNode(pageGuid);
                    if (node != null)
                        return node;
                }
                catch (Exception) { /* not found by guid, continue */ }
            }

            // Load all frontend page nodes for searching
            var backendRootId = SiteInitializer.BackendRootNodeId;
            var allNodes = pageManager.GetPageNodes()
                .Where(p => p.RootNodeId != backendRootId && !p.IsDeleted)
                .ToList();

            // 2. Try as URL path
            var normalizedUrl = identifier.Trim().TrimEnd('/');
            if (!normalizedUrl.StartsWith("/"))
                normalizedUrl = "/" + normalizedUrl;

            foreach (var node in allNodes)
            {
                try
                {
                    var nodeUrl = node.GetFullUrl() ?? string.Empty;
                    if (nodeUrl.StartsWith("~/"))
                        nodeUrl = nodeUrl.Substring(1);
                    if (!nodeUrl.StartsWith("/"))
                        nodeUrl = "/" + nodeUrl;
                    nodeUrl = nodeUrl.TrimEnd('/');

                    if (string.Equals(nodeUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                        return node;
                }
                catch (Exception) { /* skip */ }
            }

            // 3. Try as UrlName slug
            foreach (var node in allNodes)
            {
                try
                {
                    if (string.Equals(node.UrlName, identifier.Trim('/'), StringComparison.OrdinalIgnoreCase))
                        return node;
                }
                catch (Exception) { /* skip */ }
            }

            // 4. Try as title (exact, then partial)
            foreach (var node in allNodes)
            {
                try
                {
                    var title = node.Title ?? node.Name ?? string.Empty;
                    if (string.Equals(title, identifier, StringComparison.OrdinalIgnoreCase))
                        return node;
                }
                catch (Exception) { /* skip */ }
            }

            foreach (var node in allNodes)
            {
                try
                {
                    var title = node.Title ?? node.Name ?? string.Empty;
                    if (title.IndexOf(identifier, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        warnings.Add("Partial title match: '" + title + "' matched identifier '" + identifier + "'");
                        return node;
                    }
                }
                catch (Exception) { /* skip */ }
            }

            return null;
        }

        /// <summary>
        /// Extracts a short widget name from a fully-qualified ObjectType string.
        /// </summary>
        private string ExtractWidgetName(string objectType)
        {
            if (string.IsNullOrEmpty(objectType))
                return string.Empty;

            // ObjectType is typically "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy" or similar
            var lastDot = objectType.LastIndexOf('.');
            return lastDot >= 0 ? objectType.Substring(lastDot + 1) : objectType;
        }

        private List<McpPageRoute> GetPageRoutes(out List<string> warnings)
        {
            var pageRoutes = new List<McpPageRoute>();
            var warningsList = new List<string>();

            // Elevate to admin context — MCP API key requests have no Sitefinity user session,
            // so the SiteMap security trimming would hide all nodes without elevation.
            SystemManager.RunWithElevatedPrivilege(d =>
            {
                var provider = SiteMapBase.GetSiteMapProvider("FrontendSiteMap");
                var root = provider.RootNode;
                if (root == null)
                {
                    warningsList.Add("FrontendSiteMap root node is null.");
                    return;
                }

                CollectSiteMapNodes(root, pageRoutes, 0);
            });

            pageRoutes = pageRoutes.OrderBy(p => p.Url).ToList();
            warnings = warningsList;
            return pageRoutes;
        }

        private void CollectSiteMapNodes(System.Web.SiteMapNode node, List<McpPageRoute> routes, int depth)
        {
            foreach (System.Web.SiteMapNode child in node.ChildNodes)
            {
                try
                {
                    var url = child.Url ?? string.Empty;
                    if (url.StartsWith("~/"))
                        url = url.Substring(1);
                    if (!url.StartsWith("/"))
                        url = "/" + url;

                    routes.Add(new McpPageRoute
                    {
                        Title = child.Title ?? string.Empty,
                        Url = url,
                        NodeType = "Standard",
                        IsPublished = true,
                        Depth = depth,
                        HasUrlEvaluation = false,
                        UrlEvaluationMode = string.Empty
                    });

                    CollectSiteMapNodes(child, routes, depth + 1);
                }
                catch (Exception) { /* skip individual node errors */ }
            }
        }

        private List<McpApiRoute> GetServiceStackRoutes(out List<string> warnings)
        {
            var apiRoutes = new List<McpApiRoute>();
            warnings = new List<string>();

            var appHost = HostContext.AppHost;
            if (appHost != null && appHost.RestPaths != null)
            {
                foreach (var restPath in appHost.RestPaths)
                {
                    var apiPath = restPath.Path ?? string.Empty;
                    if (!apiPath.StartsWith("/RestApi", StringComparison.OrdinalIgnoreCase))
                        apiPath = "/RestApi" + apiPath;

                    apiRoutes.Add(new McpApiRoute
                    {
                        Path = apiPath,
                        Verbs = restPath.AllowedVerbs ?? "ANY",
                        RequestType = restPath.RequestType != null
                            ? restPath.RequestType.Name
                            : string.Empty
                    });
                }

                apiRoutes = apiRoutes.OrderBy(r => r.Path).ToList();
            }

            return apiRoutes;
        }
    }
}

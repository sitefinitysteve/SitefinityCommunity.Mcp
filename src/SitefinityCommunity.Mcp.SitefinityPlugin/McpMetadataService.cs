// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Telerik.Sitefinity.Abstractions;
using Telerik.Sitefinity.DynamicModules.Builder;
using Telerik.Sitefinity.DynamicModules.Builder.Model;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;

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
                DotNetVersion = Environment.Version.ToString(),
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
        /// GET /RestApi/mcp/routes — Page routes, API routes, and URL evaluation warnings.
        /// </summary>
        public McpRoutesResponse Get(ListRoutes request)
        {
            var response = new McpRoutesResponse();

            // ── Page Routes ──────────────────────────────────────────
            try
            {
                var pageManager = PageManager.GetManager();
                var backendRootId = SiteInitializer.BackendRootNodeId;

                var pageNodes = pageManager.GetPageNodes()
                    .Where(p => p.RootNodeId != backendRootId && !p.IsDeleted)
                    .ToList();

                foreach (var node in pageNodes)
                {
                    try
                    {
                        var url = node.GetFullUrl() ?? string.Empty;
                        if (string.IsNullOrEmpty(url))
                            url = "/" + (node.UrlName ?? string.Empty);

                        if (!url.StartsWith("/"))
                            url = "/" + url;

                        var nodeType = node.NodeType.ToString();
                        var isPublished = string.Equals(
                            node.ApprovalWorkflowState, "Published",
                            StringComparison.OrdinalIgnoreCase);

                        // Calculate depth by walking parent chain
                        var depth = 0;
                        var parent = node.Parent;
                        while (parent != null)
                        {
                            depth++;
                            parent = parent.Parent;
                        }

                        var pageRoute = new McpPageRoute
                        {
                            Title = node.Title ?? node.Name ?? string.Empty,
                            Url = url,
                            NodeType = nodeType,
                            IsPublished = isPublished,
                            Depth = depth,
                            HasUrlEvaluation = false,
                            UrlEvaluationMode = string.Empty
                        };

                        // Check URL evaluation mode for Standard pages only
                        if (node.NodeType == NodeType.Standard)
                        {
                            try
                            {
                                var pageData = node.GetPageData();
                                if (pageData != null && pageData.Controls != null)
                                {
                                    foreach (PageControl control in pageData.Controls)
                                    {
                                        if (control.Properties == null)
                                            continue;

                                        foreach (ControlProperty prop in control.Properties)
                                        {
                                            if (prop.Name != null
                                                && prop.Name.IndexOf("UrlEvaluationMode", StringComparison.OrdinalIgnoreCase) >= 0
                                                && !string.IsNullOrEmpty(prop.Value)
                                                && !string.Equals(prop.Value, "DoNotEvaluate", StringComparison.OrdinalIgnoreCase))
                                            {
                                                pageRoute.HasUrlEvaluation = true;
                                                pageRoute.UrlEvaluationMode = prop.Value;
                                                break;
                                            }
                                        }

                                        if (pageRoute.HasUrlEvaluation)
                                            break;
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Could not inspect page data — skip URL eval check
                            }
                        }

                        response.PageRoutes.Add(pageRoute);
                    }
                    catch (Exception)
                    {
                        // Skip individual page errors
                    }
                }

                response.PageRoutes = response.PageRoutes.OrderBy(p => p.Url).ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error enumerating page routes: " + ex.Message);
            }

            // ── API Routes ───────────────────────────────────────────
            try
            {
                var appHost = HostContext.AppHost;
                if (appHost != null && appHost.RestPaths != null)
                {
                    foreach (var restPath in appHost.RestPaths)
                    {
                        response.ApiRoutes.Add(new McpApiRoute
                        {
                            Path = restPath.Path ?? string.Empty,
                            Verbs = restPath.AllowedVerbs ?? "ANY",
                            RequestType = restPath.RequestType != null
                                ? restPath.RequestType.Name
                                : string.Empty
                        });
                    }

                    response.ApiRoutes = response.ApiRoutes.OrderBy(r => r.Path).ToList();
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error enumerating API routes: " + ex.Message);
            }

            // ── Warnings for URL evaluation ──────────────────────────
            foreach (var page in response.PageRoutes.Where(p => p.HasUrlEvaluation))
            {
                response.Warnings.Add(
                    page.Url + " has UrlEvaluationMode=" + page.UrlEvaluationMode
                    + " — verify this is intentional");
            }

            return response;
        }
    }
}

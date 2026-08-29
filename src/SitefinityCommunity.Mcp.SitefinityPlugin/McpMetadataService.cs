// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Text.RegularExpressions;
using Telerik.Sitefinity;
using Telerik.Sitefinity.Abstractions;
using Telerik.Sitefinity.DynamicModules.Builder;
using Telerik.Sitefinity.DynamicModules.Builder.Model;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Services;
using Telerik.Sitefinity.Taxonomies;
using Telerik.Sitefinity.Taxonomies.Model;
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
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

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
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            var modules = new List<McpModuleInfo>();

            foreach (var kvp in SystemManager.ApplicationModules)
            {
                var module = kvp.Value;
                var moduleType = "Custom";

                var fullName = module.GetType().FullName ?? string.Empty;
                if (fullName.StartsWith("Telerik.Sitefinity.DynamicModules"))
                {
                    moduleType = "Dynamic";
                }
                else if (fullName.StartsWith("Telerik.Sitefinity."))
                {
                    moduleType = "System";
                }

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
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            var result = new List<McpDynamicTypeInfo>();

            try
            {
                var manager = ModuleBuilderManager.GetManager();

                // Query modules and types separately via GetItems — the Types navigation property can be null
                var dynamicModules = manager.GetItems(typeof(DynamicModule), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModule>().ToList();
                var allTypes = manager.GetItems(typeof(DynamicModuleType), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModuleType>().ToList();

                // Preload all fields in one query and group by ParentTypeId — the
                // DynamicModuleType.Fields navigation property frequently returns empty.
                var fieldCountsByType = manager.GetItems(typeof(DynamicModuleField), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModuleField>()
                    .GroupBy(f => f.ParentTypeId)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Build module name lookup
                var moduleNames = dynamicModules.ToDictionary(m => m.Id, m => m.Name);

                foreach (var dynType in allTypes)
                {
                    string moduleName;
                    if (!moduleNames.TryGetValue(dynType.ParentModuleId, out moduleName))
                    {
                        moduleName = "Unknown";
                    }

                    int fieldCount;
                    if (!fieldCountsByType.TryGetValue(dynType.Id, out fieldCount))
                    {
                        fieldCount = dynType.Fields != null ? dynType.Fields.Count() : 0;
                    }

                    result.Add(new McpDynamicTypeInfo
                    {
                        ModuleName = moduleName,
                        TypeName = dynType.DisplayName,
                        TypeFullName = dynType.GetFullTypeName(),
                        FieldCount = fieldCount
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
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            if (string.IsNullOrWhiteSpace(request.TypeFullName))
            {
                throw HttpError.BadRequest("TypeFullName is required.");
            }

            var result = new List<McpDynamicFieldInfo>();

            try
            {
                var manager = ModuleBuilderManager.GetManager();
                TrySetSuppressSecurityChecks(manager, true);

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
                {
                    throw HttpError.NotFound("Dynamic type not found: " + request.TypeFullName);
                }

                var fields = LoadFieldsForType(manager, targetType);

                if (fields.Count > 0)
                {
                    var mainFieldName = targetType.MainShortTextFieldName;

                    foreach (var field in fields)
                    {
                        var isMain = !string.IsNullOrEmpty(mainFieldName)
                            && string.Equals(field.Name, mainFieldName, StringComparison.OrdinalIgnoreCase);
                        var classificationName = ResolveClassificationName(field.ClassificationId);
                        result.Add(BuildFieldInfo(field, isMain, classificationName));
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
        /// GET /RestApi/mcp/modules/{ModuleName}/structure — Full nested tree of every type in a module,
        /// including all fields with CLR type hints. One-shot shape for POCO generation.
        /// </summary>
        public McpModuleStructureResponse Get(GetModuleStructure request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            if (string.IsNullOrWhiteSpace(request.ModuleName))
            {
                throw HttpError.BadRequest("ModuleName is required.");
            }

            var response = new McpModuleStructureResponse { ModuleName = request.ModuleName };

            try
            {
                var manager = ModuleBuilderManager.GetManager();
                TrySetSuppressSecurityChecks(manager, true);

                // Locate the module (match on Name or Title, case-insensitive)
                var modules = manager.GetItems(typeof(DynamicModule), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModule>().ToList();
                var module = modules.FirstOrDefault(m =>
                    string.Equals(m.Name, request.ModuleName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(m.Title, request.ModuleName, StringComparison.OrdinalIgnoreCase));

                if (module == null)
                {
                    throw HttpError.NotFound("Module not found: " + request.ModuleName);
                }
                response.ModuleTitle = module.Title ?? module.Name;

                // Load all types for this module
                var allTypes = manager.GetItems(typeof(DynamicModuleType), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModuleType>()
                    .Where(t => t.ParentModuleId == module.Id)
                    .ToList();

                if (allTypes.Count == 0)
                {
                    response.Warnings.Add("Module has no types.");
                    return response;
                }

                // Build nodes and index by id
                var nodesById = new Dictionary<Guid, McpDynamicTypeNode>();
                var typesById = new Dictionary<Guid, DynamicModuleType>();
                foreach (var t in allTypes)
                {
                    typesById[t.Id] = t;
                    nodesById[t.Id] = new McpDynamicTypeNode
                    {
                        TypeName = t.DisplayName ?? t.TypeName,
                        TypeFullName = t.GetFullTypeName(),
                    };
                }

                // Preload ALL fields for the module in one query, grouped by ParentTypeId — avoids the
                // per-type field query (the N+1 that calling LoadFieldsForType in the loop would cause).
                // Mirrors the preload in ListDynamicTypes. Some Sitefinity versions return an empty bulk
                // result, which is exactly why LoadFieldsForType keeps its 3-strategy fallback — so we
                // fall back to it per-type whenever the bulk query doesn't cover a type.
                Dictionary<Guid, List<DynamicModuleField>> fieldsByType;
                try
                {
                    fieldsByType = manager.GetItems(typeof(DynamicModuleField), string.Empty, string.Empty, 0, 0)
                        .Cast<DynamicModuleField>()
                        .GroupBy(f => f.ParentTypeId)
                        .ToDictionary(g => g.Key, g => g.ToList());
                }
                catch (Exception)
                {
                    fieldsByType = new Dictionary<Guid, List<DynamicModuleField>>();
                }

                // Preload taxonomy names once so per-field classification lookups don't each call
                // TaxonomyManager.GetTaxonomy.
                var classificationNames = BuildClassificationNameLookup();

                // Populate fields for every node
                foreach (var kv in typesById)
                {
                    List<DynamicModuleField> fields;
                    if (!fieldsByType.TryGetValue(kv.Key, out fields) || fields.Count == 0)
                    {
                        fields = LoadFieldsForType(manager, kv.Value);
                    }

                    var mainFieldName = kv.Value.MainShortTextFieldName;
                    var node = nodesById[kv.Key];
                    foreach (var field in fields)
                    {
                        var isMain = !string.IsNullOrEmpty(mainFieldName)
                            && string.Equals(field.Name, mainFieldName, StringComparison.OrdinalIgnoreCase);
                        var classificationName = ResolveClassificationName(field.ClassificationId, classificationNames);
                        node.Fields.Add(BuildFieldInfo(field, isMain, classificationName));
                    }
                }

                // Build parent/child edges. DynamicModuleType exposes the parent type id under a
                // version-dependent name — probe the common candidates.
                var roots = new List<McpDynamicTypeNode>();
                foreach (var kv in typesById)
                {
                    var parentId = TryReadParentTypeId(kv.Value);
                    if (parentId.HasValue && parentId.Value != Guid.Empty && nodesById.ContainsKey(parentId.Value))
                    {
                        var parentNode = nodesById[parentId.Value];
                        var childNode = nodesById[kv.Key];
                        childNode.ParentTypeName = parentNode.TypeName;
                        parentNode.ChildTypes.Add(childNode);
                    }
                    else
                    {
                        roots.Add(nodesById[kv.Key]);
                    }
                }

                // Sort siblings alphabetically for stable output
                foreach (var node in nodesById.Values)
                    node.ChildTypes = node.ChildTypes.OrderBy(n => n.TypeName).ToList();
                response.RootTypes = roots.OrderBy(n => n.TypeName).ToList();
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpError(HttpStatusCode.InternalServerError, "Error reading module structure: " + ex.Message);
            }

            return response;
        }

        private static McpDynamicFieldInfo BuildFieldInfo(DynamicModuleField field, bool isMain, string classificationName)
        {
            return new McpDynamicFieldInfo
            {
                Name = field.Name,
                Title = field.Title ?? field.Name,
                FieldType = field.TypeUIName ?? field.FieldType.ToString(),
                ClrType = MapToClrType(field),
                IsRequired = field.IsRequired,
                IsMainField = isMain,
                ClassificationName = classificationName,
                RelatedDataType = field.RelatedDataType ?? string.Empty
            };
        }

        private static string ResolveClassificationName(Guid classificationId)
        {
            if (classificationId == Guid.Empty)
            {
                return string.Empty;
            }

            try
            {
                var taxonomyManager = Telerik.Sitefinity.Taxonomies.TaxonomyManager.GetManager();
                var taxonomy = taxonomyManager.GetTaxonomy(classificationId);
                return taxonomy != null ? taxonomy.Title.ToString() : classificationId.ToString();
            }
            catch (Exception)
            {
                return classificationId.ToString();
            }
        }

        /// <summary>
        /// Resolves a classification (taxonomy) name from a preloaded Id→Title lookup, falling back
        /// to a direct <see cref="ResolveClassificationName(Guid)"/> call when the id isn't present
        /// (or no lookup was supplied). Lets a caller resolve many fields without a per-field query.
        /// </summary>
        private static string ResolveClassificationName(Guid classificationId, Dictionary<Guid, string> lookup)
        {
            if (classificationId == Guid.Empty)
            {
                return string.Empty;
            }

            string name;
            if (lookup != null && lookup.TryGetValue(classificationId, out name))
            {
                return name;
            }

            return ResolveClassificationName(classificationId);
        }

        /// <summary>
        /// Loads every taxonomy's Id→Title once so classification field lookups don't each call
        /// TaxonomyManager.GetTaxonomy. Returns an empty dictionary on failure — callers then fall
        /// back to per-id resolution.
        /// </summary>
        private static Dictionary<Guid, string> BuildClassificationNameLookup()
        {
            var lookup = new Dictionary<Guid, string>();

            try
            {
                var taxonomyManager = TaxonomyManager.GetManager();
                foreach (var taxonomy in taxonomyManager.GetTaxonomies<Taxonomy>())
                {
                    if (taxonomy != null && !lookup.ContainsKey(taxonomy.Id))
                    {
                        lookup[taxonomy.Id] = taxonomy.Title != null
                            ? taxonomy.Title.ToString()
                            : taxonomy.Id.ToString();
                    }
                }
            }
            catch (Exception)
            {
                // best-effort — an empty lookup just means callers resolve per-id
            }

            return lookup;
        }

        /// <summary>
        /// Best-effort map from Module Builder field shape to a POCO-friendly CLR type string.
        /// Prefers the field's own <c>ClrType</c> property when present (newer Sitefinity), otherwise
        /// falls back to a conservative mapping from <c>FieldType</c>.
        /// </summary>
        private static string MapToClrType(DynamicModuleField field)
        {
            // Newer Sitefinity exposes ClrType directly on the field definition
            try
            {
                var prop = field.GetType().GetProperty("ClrType",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var raw = prop.GetValue(field, null) as string;

                    if (!string.IsNullOrEmpty(raw))
                    {
                        return NormalizeClrType(raw);
                    }
                }
            }
            catch (Exception)
            {
            }

            // Fallback map based on the FieldType enum name
            var kind = field.FieldType.ToString();
            switch (kind)
            {
                case "ShortText":
                case "LongText": return "string";
                case "Choices":
                    // Multi-select choices collapse to string[]; single choice to string
                    return "string";
                case "YesNo": return "bool";
                case "Number": return "decimal?";
                case "DateTime": return "DateTime?";
                case "Multimedia":
                case "RelatedMedia": return "IList<Image>";
                case "RelatedData":
                    return string.IsNullOrEmpty(field.RelatedDataType) ? "IList<object>" : "IList<" + SimpleName(field.RelatedDataType) + ">";
                case "Classification":
                    return "IList<Guid>";
                case "Address": return "Address";
                case "Multilingual": return "Lstring";
                default: return kind;
            }
        }

        private static string NormalizeClrType(string raw)
        {
            // Trim assembly-qualified suffix
            var comma = raw.IndexOf(',');
            return comma > 0 ? raw.Substring(0, comma).Trim() : raw;
        }

        private static string SimpleName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return fullName;
            }

            var dot = fullName.LastIndexOf('.');
            return dot >= 0 ? fullName.Substring(dot + 1) : fullName;
        }

        /// <summary>
        /// The property that links a DynamicModuleType to its parent type varies by Sitefinity
        /// version: commonly <c>ParentTypeId</c>, sometimes <c>ParentModuleTypeId</c> or surfaced via
        /// a configuration collection. Probe the common candidates via reflection.
        /// </summary>
        private static Guid? TryReadParentTypeId(object dynType)
        {
            if (dynType == null)
            {
                return null;
            }

            string[] candidates = { "ParentTypeId", "ParentModuleTypeId", "ContainerTypeId" };
            foreach (var name in candidates)
            {
                try
                {
                    var prop = dynType.GetType().GetProperty(name,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                    if (prop == null)
                    {
                        continue;
                    }

                    var value = prop.GetValue(dynType, null);
                    if (value == null) continue;

                    // Boxed Guid? unboxes as Guid (or null handled above); handle both just in case.
                    if (value is Guid)
                    {
                        var g = (Guid)value;
                        if (g != Guid.Empty) return g;
                    }
                    else if (value is Guid?)
                    {
                        var ng = (Guid?)value;
                        if (ng.HasValue && ng.Value != Guid.Empty) return ng.Value;
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        /// <summary>
        /// GET /RestApi/mcp/page-routes — CMS page routes via cached SiteMap (elevated context).
        /// </summary>
        public McpPageRoutesResponse Get(ListPageRoutes request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

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
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

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
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            if (string.IsNullOrWhiteSpace(request.PageIdentifier))
            {
                throw HttpError.BadRequest("PageIdentifier is required.");
            }

            var response = new McpPageDetailsResponse();

            try
            {
                var pageManager = PageManager.GetManager();
                pageManager.Provider.SuppressSecurityChecks = true;

                try
                {
                    var node = ResolvePageNode(pageManager, request.PageIdentifier, response.Warnings);
                    if (node == null)
                    {
                        throw HttpError.NotFound("Page not found: " + request.PageIdentifier);
                    }

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
                    {
                        url = "/" + (node.UrlName ?? string.Empty);
                    }

                    if (url.StartsWith("~/"))
                    {
                        url = url.Substring(1);
                    }

                    if (!url.StartsWith("/"))
                    {
                        url = "/" + url;
                    }

                    response.Url = url;

                    // Depth — derived from URL path segments (consistent with list_page_routes)
                    // rather than walking node.Parent, which lazy-loads each ancestor.
                    var depth = 0;
                    if (!string.IsNullOrEmpty(url) && url != "/")
                    {
                        depth = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    }
                    response.Depth = depth;

                    // Page data (template, description, widgets)
                    var pageData = node.GetPageData();
                    if (pageData != null)
                    {
                        response.PageDataId = pageData.Id.ToString();

                        if (pageData.Template != null)
                        {
                            response.TemplateName = pageData.Template.Name ?? string.Empty;
                        }

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
                                        Id = control.Id.ToString(),
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
                                            {
                                                val = val.Substring(0, 500) + "... (truncated)";
                                            }

                                            widget.Properties[prop.Name] = val;
                                        }

                                        // For MVC widgets, extract controller name as friendly name
                                        string controllerName;
                                        if (widget.Properties.TryGetValue("ControllerName", out controllerName)
                                            && !string.IsNullOrEmpty(controllerName))
                                        {
                                            widget.FriendlyName = controllerName;
                                        }

                                        // Extract Level 2 Settings children
                                        ExtractSettingsProperties(control.Properties, widget.SettingsProperties, 500);
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

        /// <summary>
        /// GET /RestApi/mcp/widgets/{WidgetId}/properties — Full widget details with both property levels.
        /// Requires PageIdentifier query param to locate the widget via the proven pageData.Controls path.
        /// </summary>
        public McpWidgetPropertiesResponse Get(GetWidgetProperties request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            if (string.IsNullOrWhiteSpace(request.WidgetId))
            {
                throw HttpError.BadRequest("WidgetId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.PageIdentifier))
            {
                throw HttpError.BadRequest("PageIdentifier is required. Use sitefinity_get_page_details first to find the page.");
            }

            Guid widgetGuid;
            if (!Guid.TryParse(request.WidgetId, out widgetGuid))
            {
                throw HttpError.BadRequest("WidgetId must be a valid GUID.");
            }

            var response = new McpWidgetPropertiesResponse();

            try
            {
                var pageManager = PageManager.GetManager();
                pageManager.Provider.SuppressSecurityChecks = true;

                try
                {
                    // Resolve the page using the same proven path as GetPageDetails
                    var node = ResolvePageNode(pageManager, request.PageIdentifier, response.Warnings);
                    if (node == null)
                    {
                        throw HttpError.NotFound("Page not found: " + request.PageIdentifier);
                    }

                    var pageData = node.GetPageData();
                    if (pageData == null || pageData.Controls == null)
                    {
                        throw HttpError.NotFound("Page has no controls: " + request.PageIdentifier);
                    }

                    // Find the specific widget within the page's controls
                    var control = pageData.Controls.FirstOrDefault(c => c.Id == widgetGuid);
                    if (control == null)
                    {
                        throw HttpError.NotFound("Widget " + request.WidgetId + " not found on page " + request.PageIdentifier);
                    }

                    response.WidgetId = control.Id.ToString();
                    response.ObjectType = control.ObjectType ?? string.Empty;
                    response.PlaceHolder = control.PlaceHolder ?? string.Empty;
                    response.Caption = control.Caption ?? string.Empty;
                    response.IsLayoutControl = control.IsLayoutControl;

                    // Derive friendly name
                    var widgetName = ExtractWidgetName(response.ObjectType);
                    response.FriendlyName = widgetName;

                    // Extract Level 1 properties
                    if (control.Properties != null)
                    {
                        foreach (var prop in control.Properties)
                        {
                            var val = prop.Value ?? string.Empty;
                            if (val.Length > 2000)
                            {
                                val = val.Substring(0, 2000) + "... (truncated)";
                            }

                            response.Properties[prop.Name] = val;
                        }

                        // For MVC widgets, extract controller name as friendly name
                        string controllerName;
                        if (response.Properties.TryGetValue("ControllerName", out controllerName)
                            && !string.IsNullOrEmpty(controllerName))
                        {
                            response.FriendlyName = controllerName;
                        }

                        // Extract Level 2 Settings children (higher truncation limit)
                        ExtractSettingsProperties(control.Properties, response.SettingsProperties, 2000);
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
                throw new HttpError(HttpStatusCode.InternalServerError, "Error reading widget properties: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/templates — All available CMS page templates.
        /// </summary>
        public McpTemplatesResponse Get(ListTemplates request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            var response = new McpTemplatesResponse();

            try
            {
                var pageManager = PageManager.GetManager();
                pageManager.Provider.SuppressSecurityChecks = true;

                try
                {
                    var templates = pageManager.GetTemplates().ToList();

                    foreach (var tmpl in templates)
                    {
                        try
                        {
                            var isBackend = TryReadBoolProperty(tmpl, "IsBackend");

                            if (!request.IncludeBackend && isBackend)
                            {
                                continue;
                            }

                            // Theme column on PageTemplate stores the MVC resource package
                            // ("Bootstrap4", "FoundationBasedPackage", etc.). Web Forms /
                            // Hybrid templates leave it null — fall back to the Name prefix
                            // (e.g. "Bootstrap4.default" → "Bootstrap4") when it is set that way.
                            var resourcePackage = TryReadStringProperty(tmpl, "Theme");
                            if (string.IsNullOrEmpty(resourcePackage) && !string.IsNullOrEmpty(tmpl.Name))
                            {
                                var dot = tmpl.Name.IndexOf('.');

                                if (dot > 0)
                                {
                                    resourcePackage = tmpl.Name.Substring(0, dot);
                                }
                            }

                            response.Templates.Add(new McpPageTemplateInfo
                            {
                                Id = tmpl.Id.ToString(),
                                Title = tmpl.Title ?? string.Empty,
                                Name = tmpl.Name ?? string.Empty,
                                Framework = tmpl.Framework.ToString(),
                                ParentTemplateId = ReadParentTemplateId(tmpl),
                                Culture = tmpl.Culture ?? string.Empty,
                                ResourcePackage = resourcePackage ?? string.Empty,
                                IsBackend = isBackend,
                            });
                        }
                        catch (Exception) { /* skip bad template */ }
                    }

                    response.Templates = response.Templates
                        .OrderBy(t => t.Framework)
                        .ThenBy(t => t.Title)
                        .ToList();
                }
                finally
                {
                    pageManager.Provider.SuppressSecurityChecks = false;
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error listing templates: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/taxonomies — All classifications + a sample of top-level taxa per taxonomy.
        /// </summary>
        public McpTaxonomiesResponse Get(ListTaxonomies request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            const int taxaPerTaxonomyCap = 50;
            var response = new McpTaxonomiesResponse();

            try
            {
                var taxonomyManager = TaxonomyManager.GetManager();
                var taxonomies = taxonomyManager.GetTaxonomies<Taxonomy>().ToList();

                foreach (var taxonomy in taxonomies)
                {
                    try
                    {
                        var type = taxonomy is HierarchicalTaxonomy ? "Hierarchical" : "Flat";
                        var taxaCount = 0;
                        var topTaxa = new List<McpTaxonInfo>();

                        if (taxonomy is HierarchicalTaxonomy)
                        {
                            var ht = taxonomyManager.GetTaxa<HierarchicalTaxon>()
                                .Where(t => t.Taxonomy.Id == taxonomy.Id);
                            taxaCount = ht.Count();
                            foreach (var taxon in ht.Where(t => t.Parent == null).Take(taxaPerTaxonomyCap))
                            {
                                topTaxa.Add(new McpTaxonInfo
                                {
                                    Id = taxon.Id.ToString(),
                                    Name = taxon.Name ?? string.Empty,
                                    Title = taxon.Title != null ? taxon.Title.ToString() : (taxon.Name ?? string.Empty),
                                    ParentId = taxon.Parent != null ? taxon.Parent.Id.ToString() : null,
                                });
                            }
                        }
                        else
                        {
                            var flat = taxonomyManager.GetTaxa<FlatTaxon>()
                                .Where(t => t.Taxonomy.Id == taxonomy.Id)
                                .Take(taxaPerTaxonomyCap);
                            taxaCount = taxonomyManager.GetTaxa<FlatTaxon>()
                                .Count(t => t.Taxonomy.Id == taxonomy.Id);
                            foreach (var taxon in flat)
                            {
                                topTaxa.Add(new McpTaxonInfo
                                {
                                    Id = taxon.Id.ToString(),
                                    Name = taxon.Name ?? string.Empty,
                                    Title = taxon.Title != null ? taxon.Title.ToString() : (taxon.Name ?? string.Empty),
                                    ParentId = null,
                                });
                            }
                        }

                        response.Taxonomies.Add(new McpTaxonomyInfo
                        {
                            Id = taxonomy.Id.ToString(),
                            Name = taxonomy.Name ?? string.Empty,
                            Title = taxonomy.Title != null ? taxonomy.Title.ToString() : (taxonomy.Name ?? string.Empty),
                            TaxonomyType = type,
                            TaxaCount = taxaCount,
                        });

                        response.Taxa[taxonomy.Id.ToString()] = topTaxa;
                    }
                    catch (Exception ex)
                    {
                        response.Warnings.Add("Skipped taxonomy '" + (taxonomy.Name ?? taxonomy.Id.ToString()) + "': " + ex.Message);
                    }
                }

                response.Taxonomies = response.Taxonomies.OrderBy(t => t.Title).ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error listing taxonomies: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/page-widget-tree — Placeholder tree with widgets in sibling render order,
        /// merged Level 1 + Level 2 properties (Level 2 wins), and pre-created empty columns for layout controls.
        /// </summary>
        public McpPageWidgetTreeResponse Get(GetPageWidgetTree request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Metadata);

            if (string.IsNullOrWhiteSpace(request.PageIdentifier))
            {
                throw HttpError.BadRequest("PageIdentifier is required.");
            }

            var response = new McpPageWidgetTreeResponse();

            try
            {
                var pageManager = PageManager.GetManager();
                pageManager.Provider.SuppressSecurityChecks = true;

                try
                {
                    var node = ResolvePageNode(pageManager, request.PageIdentifier, response.Warnings);
                    if (node == null)
                    {
                        throw HttpError.NotFound("Page not found: " + request.PageIdentifier);
                    }

                    response.PageId = node.Id.ToString();
                    response.PageTitle = node.Title ?? node.Name ?? string.Empty;

                    var url = node.GetFullUrl() ?? string.Empty;

                    if (url.StartsWith("~/"))
                    {
                        url = url.Substring(1);
                    }

                    if (!url.StartsWith("/"))
                    {
                        url = "/" + url;
                    }

                    response.PageUrl = url;

                    var pageData = node.GetPageData();
                    if (pageData == null)
                    {
                        response.Warnings.Add("No page data found (page may be a group node or redirect).");
                        return response;
                    }

                    if (pageData.Template != null)
                    {
                        response.TemplateId = pageData.Template.Id.ToString();
                    }

                    var published = string.Equals(node.ApprovalWorkflowState, "Published", StringComparison.OrdinalIgnoreCase);
                    if (!published)
                    {
                        response.Warnings.Add("Page is not currently published.");
                    }

                    if (pageData.Controls == null)
                    {
                        return response;
                    }

                    // 1. Build raw widget list, grouped by placeholder, with merged properties
                    var rawByPlaceholder = new Dictionary<string, List<McpWidgetNode>>(StringComparer.OrdinalIgnoreCase);

                    foreach (var control in pageData.Controls)
                    {
                        try
                        {
                            var widget = new McpWidgetNode
                            {
                                Id = control.Id.ToString(),
                                ObjectType = control.ObjectType ?? string.Empty,
                                PlaceHolder = control.PlaceHolder ?? string.Empty,
                                Caption = control.Caption ?? string.Empty,
                                IsLayoutControl = control.IsLayoutControl,
                                SiblingId = control.SiblingId.ToString(),
                            };

                            widget.FriendlyName = ExtractWidgetName(widget.ObjectType);

                            // Merge Level 1 + Level 2, Level 2 wins
                            if (control.Properties != null)
                            {
                                foreach (var prop in control.Properties)
                                {
                                    if (string.Equals(prop.Name, "Settings", StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue; // container, not a value
                                    }

                                    var val = prop.Value ?? string.Empty;
                                    if (val.Length > 500)
                                    {
                                        val = val.Substring(0, 500) + "... (truncated)";
                                    }

                                    widget.Properties[prop.Name] = val;
                                }

                                string controllerName;
                                if (widget.Properties.TryGetValue("ControllerName", out controllerName)
                                    && !string.IsNullOrEmpty(controllerName))
                                {
                                    widget.ControllerName = controllerName;
                                    widget.FriendlyName = controllerName;
                                }

                                // Level 2 wins
                                ExtractSettingsProperties(control.Properties, widget.Properties, 500);
                            }

                            var ph = widget.PlaceHolder ?? string.Empty;
                            if (!rawByPlaceholder.ContainsKey(ph))
                            {
                                rawByPlaceholder[ph] = new List<McpWidgetNode>();
                            }

                            rawByPlaceholder[ph].Add(widget);
                        }
                        catch (Exception) { /* skip */ }
                    }

                    // 2. Sort each placeholder by SiblingId chain (cycle-guarded)
                    var orderedByPlaceholder = new Dictionary<string, List<McpWidgetNode>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in rawByPlaceholder)
                    {
                        orderedByPlaceholder[kvp.Key] = SortBySibling(kvp.Value, response.Warnings, kvp.Key);
                    }

                    // Apply RenderOrder after sort
                    foreach (var kvp in orderedByPlaceholder)
                    {
                        for (var i = 0; i < kvp.Value.Count; i++)
                        {
                            kvp.Value[i].RenderOrder = i;
                        }
                    }

                    // 3. Build the tree — top-level = placeholders that aren't a "_ColNN" of another control
                    var colPattern = new Regex(@"^(?<parent>.+)_Col\d{2}$", RegexOptions.Compiled);
                    var topLevelPlaceholders = orderedByPlaceholder.Keys
                        .Where(k => !colPattern.IsMatch(k))
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var phName in topLevelPlaceholders)
                    {
                        var node2 = BuildPlaceholder(phName, orderedByPlaceholder, colPattern,
                            request.IncludeLayoutControls, response.Warnings);
                        response.Placeholders.Add(node2);
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
                throw new HttpError(HttpStatusCode.InternalServerError, "Error building widget tree: " + ex.Message);
            }

            return response;
        }

        // ── Private Helpers ──────────────────────────────────────────

        private McpPlaceholderNode BuildPlaceholder(
            string placeholderName,
            Dictionary<string, List<McpWidgetNode>> allWidgets,
            Regex colPattern,
            bool includeLayoutControls,
            List<string> warnings)
        {
            var ph = new McpPlaceholderNode { Name = placeholderName };
            List<McpWidgetNode> widgets;
            if (!allWidgets.TryGetValue(placeholderName, out widgets))
            {
                widgets = new List<McpWidgetNode>();
            }

            foreach (var widget in widgets)
            {
                // Derive expected child placeholders for layout controls
                if (widget.IsLayoutControl)
                {
                    var expectedColumns = ExpectedColumnsFromCaption(widget.Caption);
                    var childNames = new List<string>();
                    for (var i = 0; i < expectedColumns; i++)
                    {
                        childNames.Add(widget.Id + "_Col" + i.ToString("00"));
                    }

                    // Also include any actual child placeholders that exist (in case caption lies)
                    foreach (var key in allWidgets.Keys)
                    {
                        var m = colPattern.Match(key);
                        if (m.Success && string.Equals(m.Groups["parent"].Value, widget.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!childNames.Contains(key, StringComparer.OrdinalIgnoreCase))
                            {
                                childNames.Add(key);
                            }
                        }
                    }

                    childNames = childNames
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var childName in childNames)
                    {
                        var childNode = BuildPlaceholder(childName, allWidgets, colPattern, includeLayoutControls, warnings);
                        widget.Children.Add(childNode);
                    }

                    if (includeLayoutControls)
                    {
                        ph.Widgets.Add(widget);
                    }
                    else
                    {
                        // Flatten layout — merge children's widgets up into this placeholder
                        foreach (var child in widget.Children)
                        {
                            foreach (var w in child.Widgets)
                            {
                                ph.Widgets.Add(w);
                            }
                        }
                    }
                }
                else
                {
                    ph.Widgets.Add(widget);
                }
            }

            return ph;
        }

        /// <summary>
        /// Sorts a placeholder's widgets by following the SiblingId linked list.
        /// Widgets not reached by the chain are appended at the end (broken-chain fallback).
        /// Cycle-guarded at list.Count + 1 iterations.
        /// </summary>
        private List<McpWidgetNode> SortBySibling(List<McpWidgetNode> widgets, List<string> warnings, string placeholderName)
        {
            if (widgets == null || widgets.Count == 0)
            {
                return widgets ?? new List<McpWidgetNode>();
            }

            var byId = widgets.ToDictionary(w => w.Id, StringComparer.OrdinalIgnoreCase);
            var bySibling = widgets
                .Where(w => !string.IsNullOrEmpty(w.SiblingId) && w.SiblingId != Guid.Empty.ToString())
                .GroupBy(w => w.SiblingId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            // Head = widget whose SiblingId is empty/zero — first in chain
            var head = widgets.FirstOrDefault(w =>
                string.IsNullOrEmpty(w.SiblingId) || w.SiblingId == Guid.Empty.ToString());

            var sorted = new List<McpWidgetNode>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var guard = widgets.Count + 1;
            var current = head;
            while (current != null && guard-- > 0)
            {
                if (!visited.Add(current.Id))
                {
                    warnings.Add("Sibling chain cycle detected in placeholder '" + placeholderName + "'; truncating.");
                    break;
                }
                sorted.Add(current);

                McpWidgetNode next;
                if (bySibling.TryGetValue(current.Id, out next))
                {
                    current = next;
                }
                else
                {
                    current = null;
                }
            }

            if (guard < 0)
            {
                warnings.Add("Sibling chain in placeholder '" + placeholderName + "' exceeded guard; truncating.");
            }

            // Append unreached widgets in original order
            if (sorted.Count < widgets.Count)
            {
                var missing = widgets.Where(w => !visited.Contains(w.Id)).ToList();
                if (missing.Count > 0)
                {
                    warnings.Add("Broken sibling chain in placeholder '" + placeholderName
                        + "' — appended " + missing.Count + " unreached widget(s) in ORM order.");
                    sorted.AddRange(missing);
                }
            }

            return sorted;
        }

        private static int ExpectedColumnsFromCaption(string caption)
        {
            if (string.IsNullOrEmpty(caption))
            {
                return 0;
            }

            // e.g. "grid-8+4" -> 2, "grid-4+4+4" -> 3
            var m = Regex.Match(caption, @"^grid-(?<parts>\d+(?:\+\d+)+)$", RegexOptions.IgnoreCase);

            if (!m.Success)
            {
                return 0;
            }

            return m.Groups["parts"].Value.Split('+').Length;
        }

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
                    {
                        return node;
                    }
                }
                catch (Exception) { /* not found by guid, continue */ }
            }

            // Load all frontend page nodes for searching
            var backendRootId = SiteInitializer.BackendRootNodeId;
            var allNodes = pageManager.GetPageNodes()
                .Where(p => p.RootNodeId != backendRootId && !p.IsDeleted)
                .ToList();

            // node.GetFullUrl() walks the parent chain and is comparatively expensive; matching it
            // against every node is the costliest pass. Only do it when the identifier actually looks
            // like a URL path — a bare slug or title is resolved by the cheap in-memory passes below,
            // avoiding a full GetFullUrl sweep on large sites.
            var looksLikeUrl = identifier.IndexOf('/') >= 0;

            // 2. URL path — run first for URL-like identifiers so an explicit path resolves by URL
            // (preserves the prior behavior, where the URL pass took precedence for path inputs).
            if (looksLikeUrl)
            {
                var byUrl = MatchByFullUrl(allNodes, identifier);
                if (byUrl != null)
                {
                    return byUrl;
                }
            }

            // 3. UrlName slug. A slug is unique only per-parent, so two pages at different depths can
            // share one (e.g. top-level "/team" and nested "/about/team"). The old order ran the URL
            // pass first and thus resolved the shallower page; preserve that by preferring the
            // candidate with the fewest ancestors when a slug collides.
            var slug = identifier.Trim('/');
            var slugMatches = new List<PageNode>();
            foreach (var node in allNodes)
            {
                try
                {
                    if (string.Equals(node.UrlName, slug, StringComparison.OrdinalIgnoreCase))
                    {
                        slugMatches.Add(node);
                    }
                }
                catch (Exception) { /* skip */ }
            }

            if (slugMatches.Count == 1)
            {
                return slugMatches[0];
            }

            if (slugMatches.Count > 1)
            {
                return slugMatches.OrderBy(NodeDepth).First();
            }

            // 4. Exact title match
            foreach (var node in allNodes)
            {
                try
                {
                    var title = node.Title ?? node.Name ?? string.Empty;
                    if (string.Equals(title, identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return node;
                    }
                }
                catch (Exception) { /* skip */ }
            }

            // 5. URL path fallback for a bare single-segment identifier that is actually a top-level
            // page's full URL but didn't match by slug/title. Runs before partial-title (preserving
            // the old URL-over-partial-title precedence) and only when the cheap passes found nothing,
            // so the sweep cost is paid only on an otherwise-unresolved lookup.
            if (!looksLikeUrl)
            {
                var byUrl = MatchByFullUrl(allNodes, identifier);
                if (byUrl != null)
                {
                    return byUrl;
                }
            }

            // 6. Partial title match
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
        /// Matches a node by its full URL (the expensive pass — <c>PageNode.GetFullUrl()</c>
        /// walks the parent chain per node). Returns the first node whose normalized full URL equals
        /// the normalized identifier, or null.
        /// </summary>
        private PageNode MatchByFullUrl(List<PageNode> nodes, string identifier)
        {
            var normalizedUrl = identifier.Trim().TrimEnd('/');
            if (!normalizedUrl.StartsWith("/"))
            {
                normalizedUrl = "/" + normalizedUrl;
            }

            foreach (var node in nodes)
            {
                try
                {
                    var nodeUrl = node.GetFullUrl() ?? string.Empty;
                    if (nodeUrl.StartsWith("~/"))
                    {
                        nodeUrl = nodeUrl.Substring(1);
                    }

                    if (!nodeUrl.StartsWith("/"))
                    {
                        nodeUrl = "/" + nodeUrl;
                    }

                    nodeUrl = nodeUrl.TrimEnd('/');

                    if (string.Equals(nodeUrl, normalizedUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return node;
                    }
                }
                catch (Exception) { /* skip */ }
            }

            return null;
        }

        /// <summary>
        /// Counts a node's ancestors by walking the Parent chain. Used only to break slug collisions
        /// across a handful of candidate nodes (not all nodes), so the per-ancestor lazy loads are
        /// bounded. Guarded against runaway chains.
        /// </summary>
        private static int NodeDepth(PageNode node)
        {
            var depth = 0;

            try
            {
                var parent = node.Parent;
                while (parent != null && depth < 64)
                {
                    depth++;
                    parent = parent.Parent;
                }
            }
            catch (Exception) { /* best-effort */ }

            return depth;
        }

        /// <summary>
        /// Extracts Level 2 (Settings children) properties from a control's property collection.
        /// The "Settings" Level 1 property is a container whose ChildProperties hold the actual
        /// widget configuration values (designer fields, content, etc.).
        /// </summary>
        private void ExtractSettingsProperties(
            IEnumerable<ControlProperty> properties,
            Dictionary<string, string> target,
            int truncateLength)
        {
            foreach (var prop in properties)
            {
                if (string.Equals(prop.Name, "Settings", StringComparison.OrdinalIgnoreCase)
                    && prop.ChildProperties != null)
                {
                    foreach (var child in prop.ChildProperties)
                    {
                        var val = child.Value ?? string.Empty;
                        if (val.Length > truncateLength)
                        {
                            val = val.Substring(0, truncateLength) + "... (truncated)";
                        }

                        target[child.Name] = val;
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Extracts a short widget name from a fully-qualified ObjectType string.
        /// </summary>
        private string ExtractWidgetName(string objectType)
        {
            if (string.IsNullOrEmpty(objectType))
            {
                return string.Empty;
            }

            // ObjectType is typically "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy" or similar
            var lastDot = objectType.LastIndexOf('.');
            return lastDot >= 0 ? objectType.Substring(lastDot + 1) : objectType;
        }

        private List<McpPageRoute> GetPageRoutes(out List<string> warnings)
        {
            var pageRoutes = new List<McpPageRoute>();
            var warningsList = new List<string>();

            // Elevate so MCP API key requests (no Sitefinity user session) bypass security trimming.
            // Query the PageManager directly instead of walking FrontendSiteMap — one SQL round-trip,
            // no lazy sitemap build, no per-node security callbacks. Orders of magnitude faster on
            // large sites where FrontendSiteMap traversal was timing out at 30s.
            SystemManager.RunWithElevatedPrivilege(d =>
            {
                try
                {
                    var pm = PageManager.GetManager();

                    // Frontend root for the current site — excludes backend administration pages.
                    var frontendRootId = SystemManager.CurrentContext.CurrentSite.SiteMapRootNodeId;
                    if (frontendRootId == Guid.Empty)
                    {
                        warningsList.Add("Current site has no frontend sitemap root node id.");
                        return;
                    }

                    // Direct entity query — materialize once, avoid N+1 parent walks.
                    var nodes = pm.GetPageNodes()
                        .Where(n => n.RootNodeId == frontendRootId)
                        .Where(n => n.NodeType == NodeType.Standard)
                        .ToList();

                    foreach (var node in nodes)
                    {
                        if (node.Id == frontendRootId)
                        {
                            continue; // skip the root itself
                        }

                        try
                        {
                            string url;
                            try
                            {
                                url = node.GetFullUrl(null, false) ?? string.Empty;
                            }
                            catch (Exception)
                            {
                                // Fallback: build a minimal path from the slug if GetFullUrl throws
                                // on this node (rare, e.g. orphaned nodes missing a parent chain).
                                url = node.UrlName != null ? "/" + node.UrlName.ToString() : string.Empty;
                            }

                            if (!string.IsNullOrEmpty(url) && !url.StartsWith("/"))
                            {
                                url = "/" + url;
                            }

                            // Slug — the node's own URL segment (the last part of the path).
                            string slug = node.UrlName != null ? node.UrlName.ToString() : string.Empty;

                            // Additional URLs — PageNode.Urls contains the primary URL plus any
                            // legacy/alternate URLs that 301-redirect to the primary. We only want
                            // the alternates (RedirectToDefault == true). Stored per-culture;
                            // de-dupe since older Sitefinity versions can emit both absolute and
                            // relative forms of the same URL.
                            var additional = new List<string>();
                            try
                            {
                                if (node.Urls != null)
                                {
                                    foreach (var u in node.Urls)
                                    {
                                        if (u == null || !u.RedirectToDefault)
                                        {
                                            continue;
                                        }

                                        var altUrl = u.Url ?? string.Empty;
                                        if (string.IsNullOrEmpty(altUrl))
                                        {
                                            continue;
                                        }

                                        if (!altUrl.StartsWith("/"))
                                        {
                                            altUrl = "/" + altUrl;
                                        }

                                        if (!additional.Contains(altUrl))
                                        {
                                            additional.Add(altUrl);
                                        }
                                    }
                                }
                            }
                            catch (Exception)
                            {
                                // Non-fatal — a page can still report its primary URL without alternates.
                            }

                            // Depth derived from URL path segments — avoids version-specific
                            // PageNode.Level which isn't present on all Sitefinity builds.
                            int depth = 0;
                            if (!string.IsNullOrEmpty(url) && url != "/")
                            {
                                depth = url.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
                            }

                            pageRoutes.Add(new McpPageRoute
                            {
                                Title = node.Title != null ? node.Title.ToString() : string.Empty,
                                Url = url,
                                Slug = slug,
                                AdditionalUrls = additional,
                                NodeType = "Standard",
                                IsPublished = true,
                                Depth = depth,
                                HasUrlEvaluation = false,
                                UrlEvaluationMode = string.Empty
                            });
                        }
                        catch (Exception ex)
                        {
                            warningsList.Add("Skipped page node " + node.Id + ": " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    warningsList.Add("Failed to enumerate page nodes: " + ex.Message);
                }
            });

            pageRoutes = pageRoutes.OrderBy(p => p.Url).ToList();
            warnings = warningsList;
            return pageRoutes;
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
                    {
                        apiPath = "/RestApi" + apiPath;
                    }

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

        /// <summary>
        /// Loads DynamicModuleField entities for a specific DynamicModuleType using multiple
        /// strategies in order of reliability. Sitefinity versions vary in whether
        /// DynamicModuleType.Fields is hydrated on a fetched entity, so we try:
        /// 1. Direct query of DynamicModuleField by ParentTypeId via manager.GetItems
        /// 2. GetDynamicModuleFields() convenience method via reflection
        /// 3. The lazy Fields navigation property as a last resort
        /// </summary>
        private static List<DynamicModuleField> LoadFieldsForType(ModuleBuilderManager manager, DynamicModuleType targetType)
        {
            // Strategy 1 — direct query of the field entity
            try
            {
                var viaGetItems = manager.GetItems(typeof(DynamicModuleField),
                        "ParentTypeId = " + targetType.Id.ToString("D"), string.Empty, 0, 0)
                    .Cast<DynamicModuleField>()
                    .ToList();

                if (viaGetItems.Count > 0)
                {
                    return viaGetItems;
                }
            }
            catch (Exception)
            {
            }

            // Strategy 2 — reflection-probe for any convenience method returning fields
            try
            {
                var managerType = manager.GetType();
                var method = managerType.GetMethod("GetDynamicModuleFields",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (method != null)
                {
                    var all = method.Invoke(manager, null) as System.Collections.IEnumerable;
                    if (all != null)
                    {
                        var matched = new List<DynamicModuleField>();
                        foreach (var obj in all)
                        {
                            var f = obj as DynamicModuleField;
                            if (f != null && f.ParentTypeId == targetType.Id)
                            {
                                matched.Add(f);
                            }
                        }

                        if (matched.Count > 0)
                        {
                            return matched;
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            // Strategy 3 — lazy navigation
            try
            {
                if (targetType.Fields != null)
                {
                    return targetType.Fields.ToList();
                }
            }
            catch (Exception)
            {
            }

            return new List<DynamicModuleField>();
        }

        /// <summary>
        /// Some managers expose Provider.SuppressSecurityChecks; set it defensively via reflection
        /// so unauthenticated MCP context can read metadata.
        /// </summary>
        private static bool TrySetSuppressSecurityChecks(object manager, bool value)
        {
            try
            {
                var providerProp = manager.GetType().GetProperty("Provider",
                    BindingFlags.Public | BindingFlags.Instance);
                if (providerProp == null)
                {
                    return false;
                }

                var provider = providerProp.GetValue(manager, null);
                if (provider == null)
                {
                    return false;
                }

                var suppressProp = provider.GetType().GetProperty("SuppressSecurityChecks",
                    BindingFlags.Public | BindingFlags.Instance);
                if (suppressProp == null || !suppressProp.CanWrite)
                {
                    return false;
                }

                suppressProp.SetValue(provider, value, null);
                return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        private static bool TryReadBoolProperty(object obj, string propertyName)
        {
            if (obj == null)
            {
                return false;
            }

            try
            {
                var prop = obj.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null)
                {
                    return false;
                }

                var value = prop.GetValue(obj, null);

                if (value is bool)
                {
                    return (bool)value;
                }

                bool parsed;
                if (value != null && bool.TryParse(value.ToString(), out parsed))
                {
                    return parsed;
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        /// <summary>
        /// Returns a template's parent id as a string, preferring a scalar FK property
        /// (<c>ParentTemplateId</c>) so we don't lazy-load the parent PageTemplate entity per row.
        /// Falls back to the <c>ParentTemplate</c> navigation property when no scalar is exposed.
        /// <see cref="Guid.Empty"/> / blank is treated as "no parent" (null).
        /// </summary>
        private static string ReadParentTemplateId(Telerik.Sitefinity.Pages.Model.PageTemplate tmpl)
        {
            if (tmpl == null)
            {
                return null;
            }

            // Scalar FK first — avoids the per-template lazy load of the parent entity.
            var scalar = TryReadStringProperty(tmpl, "ParentTemplateId");
            if (!string.IsNullOrEmpty(scalar)
                && !string.Equals(scalar, Guid.Empty.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return scalar;
            }

            // Fallback: navigation property (lazy-loads, but only when no scalar exists).
            try
            {
                if (tmpl.ParentTemplate != null && tmpl.ParentTemplate.Id != Guid.Empty)
                {
                    return tmpl.ParentTemplate.Id.ToString();
                }
            }
            catch (Exception) { /* best-effort */ }

            return null;
        }

        private static string TryReadStringProperty(object obj, string propertyName)
        {
            if (obj == null)
            {
                return null;
            }

            try
            {
                var prop = obj.GetType().GetProperty(propertyName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null)
                {
                    return null;
                }

                var value = prop.GetValue(obj, null);
                return value != null ? value.ToString() : null;
            }
            catch (Exception)
            {
            }

            return null;
        }
    }
}

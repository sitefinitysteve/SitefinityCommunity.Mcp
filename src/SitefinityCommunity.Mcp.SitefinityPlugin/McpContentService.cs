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
using Telerik.Sitefinity.Data;
using Telerik.Sitefinity.DynamicModules;
using Telerik.Sitefinity.DynamicModules.Builder;
using Telerik.Sitefinity.DynamicModules.Builder.Model;
using Telerik.Sitefinity.DynamicModules.Model;
using Telerik.Sitefinity.GenericContent.Model;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Live-query content endpoint — returns a paged list of content items for any Sitefinity type
    /// (News, Blogs, Events, Module Builder dynamic types, etc.) so an LLM can reference real Ids
    /// and titles when generating widget configs.
    /// </summary>
    [McpApiKey]
    public class McpContentService : Service
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 500;

        /// <summary>
        /// GET /RestApi/mcp/content?TypeFullName=...&amp;Take=...&amp;Skip=...
        /// </summary>
        public McpContentListResponse Get(ListContent request)
        {
            if (string.IsNullOrWhiteSpace(request.TypeFullName))
            {
                throw HttpError.BadRequest("TypeFullName is required.");
            }

            var take = request.Take > 0 ? Math.Min(request.Take, MaxTake) : DefaultTake;
            var skip = request.Skip > 0 ? request.Skip : 0;

            var response = new McpContentListResponse
            {
                TypeFullName = request.TypeFullName,
                Take = take,
                Skip = skip,
            };

            try
            {
                var type = ResolveType(request.TypeFullName);
                if (type == null)
                {
                    // Try dynamic modules — the type may be a generated CLR type we don't have loaded
                    var list = ListDynamicItems(request.TypeFullName, take, skip, response);
                    response.Items = list;
                    return response;
                }

                // Is it a dynamic module type?
                if (typeof(DynamicContent).IsAssignableFrom(type))
                {
                    response.Items = ListDynamicItems(request.TypeFullName, take, skip, response);
                    return response;
                }

                // Standard Content type — use a managers-provider lookup via ManagerBase
                response.Items = ListStandardContent(type, take, skip, response);
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error listing content: " + ex.Message);
            }

            return response;
        }

        private Type ResolveType(string typeFullName)
        {
            // Look across all loaded assemblies — System.Type.GetType only finds mscorlib/entry types.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(typeFullName, throwOnError: false, ignoreCase: false);

                    if (type != null)
                    {
                        return type;
                    }
                }
                catch (Exception) { /* skip broken assemblies */ }
            }

            return null;
        }

        private List<McpContentItemInfo> ListStandardContent(Type type, int take, int skip, McpContentListResponse response)
        {
            var items = new List<McpContentItemInfo>();

            try
            {
                var manager = GetMappedManagerFor(type);
                if (manager == null)
                {
                    response.Warnings.Add("No manager found for type " + type.FullName);
                    return items;
                }

                // Use IQueryable<Content> via GetItem method on the provider
                var getItemsMethod = manager.GetType().GetMethod("GetItems", new Type[] { });
                if (getItemsMethod == null)
                {
                    // fallback: many managers expose GetItems(Type, filter, order, skip, take, count)
                    var paged = manager.GetType().GetMethod("GetItems",
                        new Type[] { typeof(Type), typeof(string), typeof(string), typeof(int), typeof(int), typeof(int).MakeByRefType() });
                    if (paged != null)
                    {
                        int totalRef = 0;
                        var pagedArgs = new object[] { type, string.Empty, string.Empty, skip, take, totalRef };
                        var pagedResult = paged.Invoke(manager, pagedArgs) as System.Collections.IEnumerable;
                        response.TotalCount = Convert.ToInt32(pagedArgs[5]);
                        if (pagedResult != null)
                        {
                            foreach (var item in pagedResult)
                            {
                                items.Add(MapStandardItem(item, type.FullName));
                            }
                        }

                        return items;
                    }

                    response.Warnings.Add("Manager does not expose GetItems for " + type.FullName);
                    return items;
                }

                var all = getItemsMethod.Invoke(manager, null) as System.Collections.IEnumerable;
                if (all == null)
                {
                    return items;
                }

                // Project down to IQueryable<T> so we can filter by Status=Live and page
                var queryable = all as IQueryable<Content>;
                if (queryable != null)
                {
                    var live = queryable.Where(c => c.Status == ContentLifecycleStatus.Live);
                    response.TotalCount = live.Count();

                    foreach (var item in live.Skip(skip).Take(take))
                    {
                        items.Add(MapStandardItem(item, type.FullName));
                    }
                }
                else
                {
                    // Fallback: iterate everything (bounded by take)
                    var count = 0;
                    var taken = 0;
                    foreach (var item in all)
                    {
                        count++;

                        if (count <= skip)
                        {
                            continue;
                        }

                        if (taken >= take)
                        {
                            continue;
                        }

                        items.Add(MapStandardItem(item, type.FullName));
                        taken++;
                    }
                    response.TotalCount = count;
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Standard content listing failed: " + ex.Message);
            }

            return items;
        }

        private McpContentItemInfo MapStandardItem(object item, string fallbackType)
        {
            var info = new McpContentItemInfo { ContentType = fallbackType };

            if (item == null)
            {
                return info;
            }

            var t = item.GetType();
            info.Id = GetStringProp(t, item, "Id");
            info.Title = GetStringProp(t, item, "Title");
            info.UrlName = GetStringProp(t, item, "UrlName");
            info.Status = GetStringProp(t, item, "Status");
            info.DateCreated = GetDateProp(t, item, "DateCreated");
            info.LastModified = GetDateProp(t, item, "LastModified");
            return info;
        }

        private List<McpContentItemInfo> ListDynamicItems(string typeFullName, int take, int skip, McpContentListResponse response)
        {
            var items = new List<McpContentItemInfo>();

            try
            {
                var providerManager = DynamicModuleManager.GetManager();

                // Resolve module type via ModuleBuilder
                var mbManager = ModuleBuilderManager.GetManager();
                var moduleType = mbManager.GetItems(typeof(DynamicModuleType), string.Empty, string.Empty, 0, 0)
                    .Cast<DynamicModuleType>()
                    .FirstOrDefault(t => string.Equals(t.GetFullTypeName(), typeFullName, StringComparison.OrdinalIgnoreCase));

                if (moduleType == null)
                {
                    response.Warnings.Add("Dynamic module type not found: " + typeFullName);
                    return items;
                }

                var type = ResolveType(typeFullName);
                if (type == null)
                {
                    response.Warnings.Add("Could not resolve CLR type: " + typeFullName);
                    return items;
                }

                var allItems = providerManager.GetDataItems(type)
                    .Where(d => d.Status == ContentLifecycleStatus.Live);

                response.TotalCount = allItems.Count();

                foreach (var item in allItems.Skip(skip).Take(take))
                {
                    var title = TryGetStringField(item, "Title")
                                ?? TryGetStringField(item, "Name")
                                ?? item.Id.ToString();

                    items.Add(new McpContentItemInfo
                    {
                        Id = item.Id.ToString(),
                        Title = title,
                        UrlName = item.UrlName ?? string.Empty,
                        Status = item.Status.ToString(),
                        DateCreated = item.DateCreated,
                        LastModified = item.LastModified,
                        ContentType = typeFullName,
                    });
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Dynamic content listing failed: " + ex.Message);
            }

            return items;
        }

        private static string TryGetStringField(DynamicContent item, string field)
        {
            if (item == null || string.IsNullOrEmpty(field))
            {
                return null;
            }

            try
            {
                // Invoke GetValue via reflection to sidestep ServiceStack's
                // AppMetadataUtils.GetValue<T>(MetadataPropertyType, string) extension-method
                // collision, which shadows DataItemBase.GetValue(string) in this compilation unit.
                var val = InvokeGetValue(item, field);

                if (val == null)
                {
                    return null;
                }

                var s = val.ToString();
                return string.IsNullOrEmpty(s) ? null : s;
            }
            catch (Exception) { return null; }
        }

        private static object InvokeGetValue(object item, string fieldName)
        {
            if (item == null)
            {
                return null;
            }

            var t = item.GetType();

            // Walk up the type hierarchy so we pick up the inherited method from DataItemBase.
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                var method = cur.GetMethod("GetValue",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                    null, new[] { typeof(string) }, null);

                if (method != null)
                {
                    return method.Invoke(item, new object[] { fieldName });
                }
            }

            // Property fallback (for concrete-typed items)
            var prop = t.GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return prop != null ? prop.GetValue(item, null) : null;
        }

        private static string GetStringProp(Type t, object obj, string name)
        {
            try
            {
                var p = t.GetProperty(name);

                if (p == null)
                {
                    return string.Empty;
                }

                var v = p.GetValue(obj, null);
                return v == null ? string.Empty : v.ToString();
            }
            catch (Exception) { return string.Empty; }
        }

        private static IManager GetMappedManagerFor(Type contentType)
        {
            // ManagerBase.GetMappedManager(Type) exists in most Sitefinity versions but the exact
            // signature / presence varies — look up by reflection so the plugin builds against any
            // host version.
            try
            {
                var mbType = typeof(ManagerBase);
                var byType = mbType.GetMethod("GetMappedManager", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);

                if (byType != null)
                {
                    return byType.Invoke(null, new object[] { contentType }) as IManager;
                }

                var byName = mbType.GetMethod("GetMappedManager", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);

                if (byName != null)
                {
                    return byName.Invoke(null, new object[] { contentType.FullName }) as IManager;
                }
            }
            catch (Exception) { /* fall through */ }

            return null;
        }

        private static DateTime? GetDateProp(Type t, object obj, string name)
        {
            try
            {
                var p = t.GetProperty(name);

                if (p == null)
                {
                    return null;
                }

                var v = p.GetValue(obj, null);
                return v as DateTime?;
            }
            catch (Exception) { return null; }
        }
    }
}

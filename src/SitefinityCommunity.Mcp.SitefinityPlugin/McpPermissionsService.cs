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
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Security;
using Telerik.Sitefinity.Services;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Inspects the effective permissions on a securable Sitefinity object (a page node or a content
    /// item): per role, which actions are granted vs denied across each permission set, and whether the
    /// object inherits from its parent. Answers "why can't this role see/edit this?".
    ///
    /// The <c>Permission</c> action accessors vary across Sitefinity versions, so granted/denied actions
    /// are read by probing several candidate member names reflectively rather than binding to one.
    /// Read-only.
    /// </summary>
    [McpApiKey]
    public class McpPermissionsService : Service
    {
        /// <summary>
        /// GET /RestApi/mcp/permissions?Identifier=...&amp;TypeFullName=...
        /// </summary>
        public McpPermissionsResponse Get(GetObjectPermissions request)
        {
            if (string.IsNullOrWhiteSpace(request.Identifier))
            {
                throw HttpError.BadRequest("Identifier is required (a page identifier or a content item Guid).");
            }

            var response = new McpPermissionsResponse { Target = request.Identifier };

            try
            {
                object securedObject;

                if (!string.IsNullOrWhiteSpace(request.TypeFullName))
                {
                    response.TargetKind = "content";
                    securedObject = ResolveContentItem(request.Identifier, request.TypeFullName, response);
                }
                else
                {
                    response.TargetKind = "page";
                    securedObject = ResolvePageNode(request.Identifier, response);
                }

                if (securedObject == null)
                {
                    throw HttpError.NotFound("Could not resolve a securable object for '" + request.Identifier + "'.");
                }

                response.TargetTitle = ReadTitle(securedObject);

                var roleNames = BuildRoleNameLookup(response);

                var directCount = ReadDirectPermissionCount(securedObject);
                response.InheritsPermissions = directCount == 0;

                var permissions = ReadActivePermissions(securedObject, response);
                foreach (var perm in permissions)
                {
                    var entry = BuildPermissionEntry(perm, roleNames);
                    if (entry != null)
                    {
                        response.Permissions.Add(entry);
                    }
                }

                response.Permissions = response.Permissions
                    .OrderBy(p => p.PermissionSetName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.RoleName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpError(HttpStatusCode.InternalServerError, "Error reading permissions: " + ex.Message);
            }

            return response;
        }

        // ── Object resolution ────────────────────────────────────────

        private object ResolvePageNode(string identifier, McpPermissionsResponse response)
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

            // URL match (only when it looks like a path), then slug, then title.
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
            if (byTitle != null)
            {
                return byTitle;
            }

            response.Warnings.Add("No page matched '" + identifier + "'.");
            return null;
        }

        private object ResolveContentItem(string identifier, string typeFullName, McpPermissionsResponse response)
        {
            Guid guid;
            if (!Guid.TryParse(identifier, out guid))
            {
                response.Warnings.Add("For a content item, Identifier must be a Guid.");
                return null;
            }

            var type = ResolveClrType(typeFullName);
            if (type == null)
            {
                response.Warnings.Add("Could not resolve CLR type: " + typeFullName);
                return null;
            }

            // Dynamic module items
            try
            {
                if (typeof(Telerik.Sitefinity.DynamicModules.Model.DynamicContent).IsAssignableFrom(type))
                {
                    var dmm = Telerik.Sitefinity.DynamicModules.DynamicModuleManager.GetManager();
                    TrySuppressSecurity(dmm);
                    var getDataItem = dmm.GetType().GetMethod("GetDataItem", new[] { typeof(Type), typeof(Guid) });
                    if (getDataItem != null)
                    {
                        var item = getDataItem.Invoke(dmm, new object[] { type, guid });
                        if (item != null)
                        {
                            return item;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Dynamic item lookup failed: " + ex.Message);
            }

            // Standard content via the type's mapped manager (reflective GetItem(Type, Guid))
            try
            {
                var manager = GetMappedManagerFor(type);
                if (manager != null)
                {
                    TrySuppressSecurity(manager);
                    var getItem = manager.GetType().GetMethod("GetItem", new[] { typeof(Type), typeof(Guid) });
                    if (getItem != null)
                    {
                        var item = getItem.Invoke(manager, new object[] { type, guid });
                        if (item != null)
                        {
                            return item;
                        }
                    }

                    response.Warnings.Add("Manager for " + typeFullName + " does not expose GetItem(Type, Guid).");
                }
                else
                {
                    response.Warnings.Add("No manager found for type " + typeFullName + ".");
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Content item lookup failed: " + ex.Message);
            }

            return null;
        }

        // ── Permission reading (reflection — version tolerant) ───────

        private IEnumerable<object> ReadActivePermissions(object securedObject, McpPermissionsResponse response)
        {
            // ISecuredObject.GetActivePermissions() returns the effective (incl. inherited) set.
            foreach (var methodName in new[] { "GetActivePermissions", "GetPermissions" })
            {
                try
                {
                    var method = securedObject.GetType().GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                    if (method != null)
                    {
                        var result = method.Invoke(securedObject, null) as IEnumerable;
                        if (result != null)
                        {
                            return result.Cast<object>().ToList();
                        }
                    }
                }
                catch (Exception) { /* try next */ }
            }

            response.Warnings.Add("Could not read active permissions (GetActivePermissions not available on this object).");
            return Enumerable.Empty<object>();
        }

        private static int ReadDirectPermissionCount(object securedObject)
        {
            try
            {
                var prop = securedObject.GetType().GetProperty("Permissions", BindingFlags.Public | BindingFlags.Instance);
                var value = prop != null ? prop.GetValue(securedObject, null) as IEnumerable : null;

                if (value != null)
                {
                    return value.Cast<object>().Count();
                }
            }
            catch (Exception) { /* best-effort */ }

            return -1; // unknown
        }

        private static McpPermissionEntry BuildPermissionEntry(object perm, Dictionary<Guid, string> roleNames)
        {
            if (perm == null)
            {
                return null;
            }

            var setName = ReadStringMember(perm, "SetName") ?? string.Empty;
            var principalId = ReadGuidMember(perm, "PrincipalId");

            var entry = new McpPermissionEntry
            {
                PermissionSetName = setName,
                RoleId = principalId == Guid.Empty ? string.Empty : principalId.ToString(),
                GrantedActions = ReadActionList(perm, new[] { "GrantedActions", "GrantedPermissions", "AllowedActions", "Grant", "GrantedActionsList" }),
                DeniedActions = ReadActionList(perm, new[] { "DeniedActions", "DeniedPermissions", "Deny", "DeniedActionsList" }),
            };

            string roleName;
            if (principalId != Guid.Empty && roleNames.TryGetValue(principalId, out roleName))
            {
                entry.RoleName = roleName;
            }
            else
            {
                entry.RoleName = principalId == Guid.Empty ? string.Empty : "(unresolved principal " + principalId + ")";
            }

            return entry;
        }

        /// <summary>
        /// Reads an action set from a Permission, accepting either a delimited string ("View,Modify")
        /// or an IEnumerable of action names — whichever the host version exposes.
        /// </summary>
        private static List<string> ReadActionList(object perm, string[] candidateMembers)
        {
            foreach (var name in candidateMembers)
            {
                try
                {
                    var prop = perm.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null)
                    {
                        continue;
                    }

                    var value = prop.GetValue(perm, null);
                    if (value == null)
                    {
                        continue;
                    }

                    if (value is string s)
                    {
                        if (string.IsNullOrWhiteSpace(s))
                        {
                            continue;
                        }

                        return s.Split(new[] { ',', ';', ' ', '|' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Trim())
                            .Where(x => x.Length > 0)
                            .ToList();
                    }

                    if (value is IEnumerable en)
                    {
                        var list = en.Cast<object>()
                            .Where(o => o != null)
                            .Select(o => o.ToString())
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToList();

                        if (list.Count > 0)
                        {
                            return list;
                        }
                    }
                }
                catch (Exception) { /* try next candidate */ }
            }

            return new List<string>();
        }

        // ── Role name resolution ─────────────────────────────────────

        private static Dictionary<Guid, string> BuildRoleNameLookup(McpPermissionsResponse response)
        {
            var lookup = new Dictionary<Guid, string>();

            // Default provider (custom roles) + AppRoles (built-in: Administrators, Everyone, ...).
            foreach (var providerName in new string[] { null, "AppRoles" })
            {
                try
                {
                    var manager = providerName == null
                        ? RoleManager.GetManager()
                        : RoleManager.GetManager(providerName);

                    foreach (var role in manager.GetRoles())
                    {
                        try
                        {
                            if (role != null && !lookup.ContainsKey(role.Id))
                            {
                                lookup[role.Id] = role.Name ?? role.Id.ToString();
                            }
                        }
                        catch (Exception) { /* skip role */ }
                    }
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Could not enumerate roles" +
                        (providerName != null ? " (" + providerName + ")" : string.Empty) + ": " + ex.Message);
                }
            }

            return lookup;
        }

        // ── Generic reflection helpers ───────────────────────────────

        private static string ReadTitle(object obj)
        {
            foreach (var name in new[] { "Title", "Name", "UrlName" })
            {
                var val = ReadStringMember(obj, name);
                if (!string.IsNullOrEmpty(val))
                {
                    return val;
                }
            }

            return string.Empty;
        }

        private static string ReadStringMember(object obj, string name)
        {
            try
            {
                var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                var val = prop != null ? prop.GetValue(obj, null) : null;
                return val == null ? null : val.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Guid ReadGuidMember(object obj, string name)
        {
            try
            {
                var prop = obj.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                var val = prop != null ? prop.GetValue(obj, null) : null;

                if (val is Guid g)
                {
                    return g;
                }

                Guid parsed;
                if (val != null && Guid.TryParse(val.ToString(), out parsed))
                {
                    return parsed;
                }
            }
            catch (Exception)
            {
            }

            return Guid.Empty;
        }

        private static Type ResolveClrType(string typeFullName)
        {
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
                catch (Exception) { /* skip */ }
            }

            return null;
        }

        private static Telerik.Sitefinity.Data.IManager GetMappedManagerFor(Type contentType)
        {
            try
            {
                var mbType = typeof(Telerik.Sitefinity.Data.ManagerBase);
                var byType = mbType.GetMethod("GetMappedManager", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Type) }, null);
                if (byType != null)
                {
                    return byType.Invoke(null, new object[] { contentType }) as Telerik.Sitefinity.Data.IManager;
                }

                var byName = mbType.GetMethod("GetMappedManager", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (byName != null)
                {
                    return byName.Invoke(null, new object[] { contentType.FullName }) as Telerik.Sitefinity.Data.IManager;
                }
            }
            catch (Exception) { /* fall through */ }

            return null;
        }

        private static void TrySuppressSecurity(object manager)
        {
            try
            {
                var providerProp = manager.GetType().GetProperty("Provider");
                var provider = providerProp != null ? providerProp.GetValue(manager, null) : null;
                var suppress = provider != null ? provider.GetType().GetProperty("SuppressSecurityChecks") : null;

                if (suppress != null && suppress.CanWrite)
                {
                    suppress.SetValue(provider, true, null);
                }
            }
            catch (Exception) { /* best-effort */ }
        }
    }
}

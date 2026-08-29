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
using Telerik.Sitefinity;
using Telerik.Sitefinity.Abstractions;
using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Modules.Pages;
using Telerik.Sitefinity.Pages.Model;
using Telerik.Sitefinity.Security;
using Telerik.Sitefinity.Security.Configuration;
using Telerik.Sitefinity.Security.Model;
using Telerik.Sitefinity.Services;

// Two distinct "Permission" types are in play and must not be confused:
//   ModelPermission  = the runtime grant/deny record on an object (Grant/Deny Int32 bitmasks).
//   ConfigPermission = the configuration element that DEFINES a permission set and its named actions.
using ModelPermission = Telerik.Sitefinity.Security.Model.Permission;
using ConfigPermission = Telerik.Sitefinity.Security.Configuration.Permission;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Inspects the effective permissions on a securable Sitefinity object (a page node or a content
    /// item) and answers the questions people actually ask: is it public? what can each role actually
    /// do (after deny-wins resolution)? does it inherit, and from where? — and, on request, a direct
    /// "can &lt;principal&gt; &lt;action&gt; this?" yes/no.
    ///
    /// Grants/denies are stored as <c>Grant</c>/<c>Deny</c> Int32 bitmasks on each
    /// <see cref="ModelPermission"/>; they are decoded against the set's configured
    /// <see cref="SecurityAction"/> vocabulary (name + bit value + action type). Read-only.
    /// </summary>
    [McpApiKey]
    public class McpPermissionsService : Service
    {
        /// <summary>
        /// GET /RestApi/mcp/permissions?Identifier=...&amp;TypeFullName=...&amp;Action=...&amp;Principal=...
        /// </summary>
        public McpPermissionsResponse Get(GetObjectPermissions request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Permissions);

            if (string.IsNullOrWhiteSpace(request.Identifier))
            {
                throw HttpError.BadRequest("Identifier is required (a page identifier or a content item Guid).");
            }

            var response = new McpPermissionsResponse { Target = request.Identifier };

            try
            {
                PageNode pageNode = null;
                object securedObject;

                if (!string.IsNullOrWhiteSpace(request.TypeFullName))
                {
                    response.TargetKind = "content";
                    securedObject = ResolveContentItem(request.Identifier, request.TypeFullName, response);
                }
                else
                {
                    response.TargetKind = "page";
                    pageNode = ResolvePageNode(request.Identifier, response);
                    securedObject = pageNode;
                }

                if (securedObject == null)
                {
                    throw HttpError.NotFound("Could not resolve a securable object for '" + request.Identifier + "'.");
                }

                var secured = securedObject as ISecuredObject;
                if (secured == null)
                {
                    response.Warnings.Add("Resolved object is not securable (does not implement ISecuredObject).");
                    return response;
                }

                response.TargetTitle = ReadTitle(securedObject);
                ReadInheritance(secured, pageNode, response);

                AnalyzePermissions(secured, response);

                response.IsAuthenticatedAccessible = response.IsAuthenticatedAccessible || response.IsPublic;
                response.Summary = BuildSummary(response);

                if (!string.IsNullOrWhiteSpace(request.Action) || !string.IsNullOrWhiteSpace(request.Principal))
                {
                    response.Answer = BuildAnswer(request, response);
                }

                response.Principals = response.Principals
                    .OrderBy(p => p.PermissionSet, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.PrincipalName, StringComparer.OrdinalIgnoreCase)
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

        // ── Inheritance ──────────────────────────────────────────────

        private static void ReadInheritance(ISecuredObject secured, PageNode pageNode, McpPermissionsResponse response)
        {
            try
            {
                response.InheritsPermissions = secured.InheritsPermissions;
                response.CanInheritPermissions = secured.CanInheritPermissions;
            }
            catch (Exception) { /* best-effort */ }

            try
            {
                response.HasLocalOverrides = secured.Permissions != null && secured.Permissions.Count > 0;
            }
            catch (Exception) { /* best-effort */ }

            try
            {
                response.SupportedPermissionSets = secured.SupportedPermissionSets != null
                    ? secured.SupportedPermissionSets.ToList()
                    : new List<string>();
            }
            catch (Exception) { /* best-effort */ }

            // For a page that inherits, name the parent the permissions flow from.
            if (response.InheritsPermissions && pageNode != null)
            {
                try
                {
                    var parent = pageNode.Parent;
                    if (parent != null)
                    {
                        response.InheritedFrom = parent.Title ?? parent.Name ?? parent.Id.ToString();
                    }
                }
                catch (Exception) { /* best-effort */ }
            }
        }

        // ── Permission analysis (bitmask decode) ─────────────────────

        private void AnalyzePermissions(ISecuredObject secured, McpPermissionsResponse response)
        {
            List<ModelPermission> active;
            try
            {
                active = secured.GetActivePermissions().ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not read active permissions: " + ex.Message);
                return;
            }

            SecurityConfig securityConfig = null;
            try
            {
                securityConfig = Config.Get<SecurityConfig>();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not load SecurityConfig: " + ex.Message);
            }

            // Resolve the well-known special principals once for public/authenticated detection.
            var anonymousId = TryReadRoleId(() => SecurityManager.AnonymousRole);
            var authenticatedId = TryReadRoleId(() => SecurityManager.AuthenticatedRole);
            var ownerId = TryReadRoleId(() => SecurityManager.OwnerRole);

            foreach (var group in active.GroupBy(p => p.SetName ?? string.Empty))
            {
                var setName = group.Key;
                var setView = new McpPermissionSetView { SetName = setName };

                var actions = LoadSetActions(securityConfig, setName, setView.Warnings);
                setView.AvailableActions = actions.Select(a => a.Name).ToList();

                var viewActionNames = new HashSet<string>(
                    actions.Where(a => string.Equals(a.Type, "View", StringComparison.OrdinalIgnoreCase)).Select(a => a.Name),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var perm in group)
                {
                    var access = BuildPrincipalAccess(perm, actions, setName, anonymousId, authenticatedId, ownerId);
                    setView.Principals.Add(access);
                    response.Principals.Add(access);

                    // Public / authenticated visibility — keyed off the special roles having an effective View.
                    var viewEffective = access.EffectiveActions.Any(a => viewActionNames.Contains(a));
                    if (viewEffective)
                    {
                        if (string.Equals(access.PrincipalName, "Everyone", StringComparison.OrdinalIgnoreCase))
                        {
                            response.IsPublic = true;
                        }
                        else if (string.Equals(access.PrincipalName, "Authenticated", StringComparison.OrdinalIgnoreCase))
                        {
                            response.IsAuthenticatedAccessible = true;
                        }
                    }
                }

                setView.Principals = setView.Principals
                    .OrderBy(p => p.PrincipalName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                response.Sets.Add(setView);
            }
        }

        /// <summary>
        /// Decodes one runtime permission against the set's action vocabulary: bit-tests Grant/Deny per
        /// action and resolves effective access (granted AND not denied). Classifies the principal and
        /// flags administrative roles.
        /// </summary>
        private McpPrincipalAccess BuildPrincipalAccess(
            ModelPermission perm, List<SecurityActionInfo> actions, string setName,
            Guid anonymousId, Guid authenticatedId, Guid ownerId)
        {
            var access = new McpPrincipalAccess { PermissionSet = setName };

            ClassifyPrincipal(access, perm.PrincipalId, anonymousId, authenticatedId, ownerId);

            foreach (var action in actions)
            {
                var granted = (perm.Grant & action.Value) != 0;
                var denied = (perm.Deny & action.Value) != 0;

                if (granted)
                {
                    access.GrantedActions.Add(action.Name);
                }

                if (denied)
                {
                    access.DeniedActions.Add(action.Name);
                }

                // Deny always wins over grant.
                if (granted && !denied)
                {
                    access.EffectiveActions.Add(action.Name);
                }
            }

            if (access.IsAdministrative)
            {
                access.Note = "Administrative role — has full control regardless of the explicit grants shown.";
            }
            else if (actions.Count == 0)
            {
                // No configured vocabulary for this set — surface the raw bitmasks so nothing is lost.
                access.Note = "Action vocabulary unavailable for set '" + setName + "'; raw grant=" + perm.Grant + ", deny=" + perm.Deny + ".";
            }

            return access;
        }

        private static void ClassifyPrincipal(McpPrincipalAccess access, Guid principalId, Guid anonymousId, Guid authenticatedId, Guid ownerId)
        {
            access.PrincipalId = principalId == Guid.Empty ? string.Empty : principalId.ToString();

            try
            {
                access.IsAdministrative = principalId != Guid.Empty && SecurityManager.IsAdministrativeRole(principalId);
            }
            catch (Exception) { /* best-effort */ }

            // Well-known special roles first (their friendly names anchor the public/authenticated checks).
            if (principalId != Guid.Empty && principalId == anonymousId)
            {
                access.PrincipalType = "SpecialRole";
                access.PrincipalName = "Everyone";
                return;
            }

            if (principalId != Guid.Empty && principalId == authenticatedId)
            {
                access.PrincipalType = "SpecialRole";
                access.PrincipalName = "Authenticated";
                return;
            }

            if (principalId != Guid.Empty && principalId == ownerId)
            {
                access.PrincipalType = "SpecialRole";
                access.PrincipalName = "Owner";
                return;
            }

            string name = null;
            try
            {
                name = SecurityManager.GetPrincipalName(principalId);
            }
            catch (Exception) { /* best-effort */ }

            access.PrincipalName = !string.IsNullOrEmpty(name)
                ? name
                : (principalId == Guid.Empty ? "(none)" : principalId.ToString());

            try
            {
                if (SecurityManager.IsPrincipalRole(principalId))
                {
                    access.PrincipalType = "Role";
                }
                else if (SecurityManager.IsPrincipalUser(principalId))
                {
                    access.PrincipalType = "User";
                }
                else
                {
                    access.PrincipalType = "Unknown";
                }
            }
            catch (Exception)
            {
                access.PrincipalType = "Unknown";
            }
        }

        /// <summary>
        /// Loads a permission set's action vocabulary from SecurityConfig: each
        /// <see cref="SecurityAction"/> carries its name, action type, and the bit value used in the
        /// Grant/Deny masks. Empty (with a warning) when the set isn't configured.
        /// </summary>
        private static List<SecurityActionInfo> LoadSetActions(SecurityConfig securityConfig, string setName, List<string> warnings)
        {
            var list = new List<SecurityActionInfo>();

            if (securityConfig == null || string.IsNullOrEmpty(setName))
            {
                return list;
            }

            ConfigPermission setConfig = null;
            try
            {
                setConfig = securityConfig.Permissions[setName];
            }
            catch (Exception)
            {
                setConfig = null;
            }

            if (setConfig == null || setConfig.Actions == null)
            {
                warnings.Add("No configured action vocabulary for permission set '" + setName + "'.");
                return list;
            }

            try
            {
                foreach (SecurityAction action in setConfig.Actions)
                {
                    list.Add(new SecurityActionInfo
                    {
                        Name = action.Name,
                        Type = action.Type.ToString(),
                        Value = action.Value,
                    });
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not enumerate actions for set '" + setName + "': " + ex.Message);
            }

            return list;
        }

        // ── Direct answer ────────────────────────────────────────────

        private static McpAccessAnswer BuildAnswer(GetObjectPermissions request, McpPermissionsResponse response)
        {
            var answer = new McpAccessAnswer { Principal = request.Principal, Action = request.Action };

            // Action only → who can do it?
            if (string.IsNullOrWhiteSpace(request.Principal))
            {
                var whoCan = response.Principals
                    .Where(p => HasAction(p, request.Action))
                    .Select(p => p.PrincipalName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                answer.Allowed = whoCan.Count > 0;
                answer.Reason = whoCan.Count > 0
                    ? "'" + request.Action + "' is effectively granted to: " + string.Join(", ", whoCan) + "."
                    : "No principal has '" + request.Action + "' effectively granted on this object.";
                return answer;
            }

            var matches = response.Principals
                .Where(p => string.Equals(p.PrincipalName, request.Principal, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(p.PrincipalId, request.Principal, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matches.Count == 0)
            {
                answer.Allowed = false;
                answer.Reason = "No permission entry for principal '" + request.Principal +
                    "' on this object (default deny). It may still have access via an administrative role.";
                return answer;
            }

            // Principal only → list its effective actions.
            if (string.IsNullOrWhiteSpace(request.Action))
            {
                var effective = matches.SelectMany(p => p.EffectiveActions).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                answer.Allowed = effective.Count > 0;
                answer.Reason = effective.Count > 0
                    ? "Effective actions: " + string.Join(", ", effective) + "."
                    : "No effective actions (default deny).";
                return answer;
            }

            // Both → crisp yes/no with the reason.
            var admin = matches.FirstOrDefault(p => p.IsAdministrative);
            var granting = matches.FirstOrDefault(p => HasAction(p, request.Action));
            var denying = matches.FirstOrDefault(p => p.DeniedActions.Any(a => string.Equals(a, request.Action, StringComparison.OrdinalIgnoreCase)));

            if (admin != null)
            {
                answer.Allowed = true;
                answer.Reason = "'" + admin.PrincipalName + "' is an administrative role with full control.";
            }
            else if (granting != null)
            {
                answer.Allowed = true;
                answer.Reason = "'" + request.Action + "' is granted and not denied in set '" + granting.PermissionSet + "'.";
            }
            else if (denying != null)
            {
                answer.Allowed = false;
                answer.Reason = "'" + request.Action + "' is explicitly denied in set '" + denying.PermissionSet + "'.";
            }
            else
            {
                answer.Allowed = false;
                answer.Reason = "'" + request.Action + "' is not granted to '" + request.Principal + "' (default deny).";
            }

            return answer;
        }

        private static bool HasAction(McpPrincipalAccess access, string action)
        {
            return access.EffectiveActions.Any(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));
        }

        private static string BuildSummary(McpPermissionsResponse response)
        {
            var parts = new List<string>();

            if (response.IsPublic)
            {
                parts.Add("Publicly viewable (Everyone)");
            }
            else if (response.IsAuthenticatedAccessible)
            {
                parts.Add("Viewable by any authenticated user");
            }
            else
            {
                parts.Add("Restricted (not public)");
            }

            if (response.InheritsPermissions)
            {
                parts.Add("inherits permissions" + (string.IsNullOrEmpty(response.InheritedFrom) ? string.Empty : " from '" + response.InheritedFrom + "'"));
            }
            else
            {
                parts.Add("has its own permissions");
            }

            parts.Add(response.Principals.Count + " principal(s) across " + response.Sets.Count + " set(s)");

            return string.Join("; ", parts) + ".";
        }

        // ── Object resolution ────────────────────────────────────────

        private PageNode ResolvePageNode(string identifier, McpPermissionsResponse response)
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

        // ── Reflection helpers ───────────────────────────────────────

        private static Guid TryReadRoleId(Func<RoleInfo> getter)
        {
            try
            {
                var role = getter();
                return role != null ? role.Id : Guid.Empty;
            }
            catch (Exception)
            {
                return Guid.Empty;
            }
        }

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

        /// <summary>Flattened description of one configured security action (name + type + bit value).</summary>
        private sealed class SecurityActionInfo
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public int Value { get; set; }
        }
    }
}

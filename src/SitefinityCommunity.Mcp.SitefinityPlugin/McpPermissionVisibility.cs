// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
//
// Deliberately has NO Sitefinity or ServiceStack references: the MCP test
// project links this file directly so the visibility rules are unit-tested
// without a Sitefinity runtime.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// One permission row reduced to what visibility needs: who it is for, and whether it grants
    /// and/or denies a View-type action.
    /// </summary>
    public struct McpViewPermissionRow
    {
        /// <summary>Creates a row.</summary>
        public McpViewPermissionRow(Guid principalId, bool grantsView, bool deniesView)
        {
            this.PrincipalId = principalId;
            this.GrantsView = grantsView;
            this.DeniesView = deniesView;
        }

        /// <summary>The principal (role or user) id the row applies to.</summary>
        public Guid PrincipalId { get; }

        /// <summary>True when the row grants a View-type action.</summary>
        public bool GrantsView { get; }

        /// <summary>True when the row denies a View-type action.</summary>
        public bool DeniesView { get; }
    }

    /// <summary>
    /// Ids of Sitefinity's built-in application roles. They are distinct roles with distinct ids:
    /// Everyone covers every visitor, Anonymous only signed-out visitors, Authenticated only signed-in users.
    /// </summary>
    public struct McpSpecialRoleIds
    {
        /// <summary>Creates the id set. Pass <c>Guid.Empty</c> for a role that could not be resolved.</summary>
        public McpSpecialRoleIds(Guid everyone, Guid anonymous, Guid authenticated, Guid owner)
        {
            this.Everyone = everyone;
            this.Anonymous = anonymous;
            this.Authenticated = authenticated;
            this.Owner = owner;
        }

        /// <summary>The Everyone role id.</summary>
        public Guid Everyone { get; }

        /// <summary>The Anonymous role id.</summary>
        public Guid Anonymous { get; }

        /// <summary>The Authenticated role id.</summary>
        public Guid Authenticated { get; }

        /// <summary>The Owner role id.</summary>
        public Guid Owner { get; }
    }

    /// <summary>
    /// Decides who can view an object from its permission rows. Decisions key on role ids only,
    /// never on display names, and a deny held by any principal the audience belongs to wins.
    /// </summary>
    public static class McpPermissionVisibility
    {
        /// <summary>
        /// An anonymous visitor is a member of Everyone and Anonymous. Public when at least one of those
        /// grants View and none of them denies it.
        /// </summary>
        public static bool IsPublic(IEnumerable<McpViewPermissionRow> rows, McpSpecialRoleIds ids)
        {
            return CanAudienceView(rows, ids.Everyone, ids.Anonymous);
        }

        /// <summary>
        /// Any signed-in user is a member of Everyone and Authenticated. Accessible when at least one of
        /// those grants View and none of them denies it. An Anonymous grant does not reach signed-in users.
        /// </summary>
        public static bool IsAuthenticatedAccessible(IEnumerable<McpViewPermissionRow> rows, McpSpecialRoleIds ids)
        {
            return CanAudienceView(rows, ids.Everyone, ids.Authenticated);
        }

        /// <summary>Classifies a principal id as one of the special roles, or returns null.</summary>
        public static string SpecialRoleName(Guid principalId, McpSpecialRoleIds ids)
        {
            if (principalId == Guid.Empty)
            {
                return null;
            }

            if (principalId == ids.Everyone)
            {
                return "Everyone";
            }

            if (principalId == ids.Anonymous)
            {
                return "Anonymous";
            }

            if (principalId == ids.Authenticated)
            {
                return "Authenticated";
            }

            if (principalId == ids.Owner)
            {
                return "Owner";
            }

            return null;
        }

        private static bool CanAudienceView(IEnumerable<McpViewPermissionRow> rows, Guid roleA, Guid roleB)
        {
            var granted = false;

            if (rows == null)
            {
                return false;
            }

            foreach (var row in rows)
            {
                if (row.PrincipalId == Guid.Empty)
                {
                    continue;
                }

                if (row.PrincipalId != roleA && row.PrincipalId != roleB)
                {
                    continue;
                }

                if (row.DeniesView)
                {
                    return false;
                }

                if (row.GrantsView)
                {
                    granted = true;
                }
            }

            return granted;
        }
    }
}

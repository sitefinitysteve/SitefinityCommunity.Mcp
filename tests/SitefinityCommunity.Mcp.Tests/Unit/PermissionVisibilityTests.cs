using SitefinityCommunity.Mcp.SitefinityPlugin;

namespace SitefinityCommunity.Mcp.Tests.Unit;

/// <summary>
/// The plugin's audience visibility rules (McpPermissionVisibility), compiled in from the plugin source.
/// Pure functions over (principalId, grantsView, deniesView) rows, so no Sitefinity runtime is needed.
/// </summary>
[Trait("Category", "Unit")]
public sealed class PermissionVisibilityTests
{
    private static readonly Guid Everyone = Guid.Parse("37d14d8a-0000-0000-0000-000000000001");
    private static readonly Guid Anonymous = Guid.Parse("543ab90e-0000-0000-0000-000000000002");
    private static readonly Guid Authenticated = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid Owner = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004");
    private static readonly Guid SomeUser = Guid.Parse("10893e54-0000-0000-0000-000000000005");

    private static readonly McpSpecialRoleIds Ids = new(Everyone, Anonymous, Authenticated, Owner);

    private static McpViewPermissionRow Grant(Guid id) => new(id, true, false);

    private static McpViewPermissionRow Deny(Guid id) => new(id, false, true);

    [Fact]
    public void EveryoneGrant_AnonymousDeny_IsNotPublic_ButSignedInUsersCanView()
    {
        var rows = new[] { Grant(Everyone), Deny(Anonymous) };

        Assert.False(McpPermissionVisibility.IsPublic(rows, Ids));
        Assert.True(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
    }

    [Fact]
    public void AuthenticatedDeny_IsNotAuthenticatedAccessible_EvenWhenEveryoneGrants()
    {
        var rows = new[] { Grant(Everyone), Deny(Authenticated) };

        Assert.False(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
        Assert.True(McpPermissionVisibility.IsPublic(rows, Ids));
    }

    [Fact]
    public void AnonymousOnlyGrant_IsPublic_ButNotAuthenticatedAccessible()
    {
        var rows = new[] { Grant(Anonymous) };

        Assert.True(McpPermissionVisibility.IsPublic(rows, Ids));
        Assert.False(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
    }

    [Fact]
    public void EveryoneGrant_Alone_IsPublicAndAuthenticatedAccessible()
    {
        var rows = new[] { Grant(Everyone) };

        Assert.True(McpPermissionVisibility.IsPublic(rows, Ids));
        Assert.True(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
    }

    [Fact]
    public void AuthenticatedOnlyGrant_IsNotPublic_ButAuthenticatedAccessible()
    {
        var rows = new[] { Grant(Authenticated) };

        Assert.False(McpPermissionVisibility.IsPublic(rows, Ids));
        Assert.True(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
    }

    [Fact]
    public void GrantsToOtherPrincipals_DoNotMakeAnythingVisible()
    {
        var rows = new[] { Grant(SomeUser), Grant(Owner) };

        Assert.False(McpPermissionVisibility.IsPublic(rows, Ids));
        Assert.False(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
    }

    [Fact]
    public void DenyOnSameRowAsGrant_Wins()
    {
        var rows = new[] { new McpViewPermissionRow(Everyone, true, true) };

        Assert.False(McpPermissionVisibility.IsPublic(rows, Ids));
        Assert.False(McpPermissionVisibility.IsAuthenticatedAccessible(rows, Ids));
    }

    [Fact]
    public void UnresolvedRoleIds_NeverMatchEmptyPrincipalRows()
    {
        var ids = new McpSpecialRoleIds(Guid.Empty, Anonymous, Authenticated, Owner);
        var rows = new[] { Grant(Guid.Empty) };

        Assert.False(McpPermissionVisibility.IsPublic(rows, ids));
        Assert.Null(McpPermissionVisibility.SpecialRoleName(Guid.Empty, ids));
    }

    [Theory]
    [InlineData("37d14d8a-0000-0000-0000-000000000001", "Everyone")]
    [InlineData("543ab90e-0000-0000-0000-000000000002", "Anonymous")]
    [InlineData("aaaaaaaa-0000-0000-0000-000000000003", "Authenticated")]
    [InlineData("bbbbbbbb-0000-0000-0000-000000000004", "Owner")]
    public void SpecialRoleName_ClassifiesById_AnonymousIsNotEveryone(string id, string expected)
    {
        Assert.Equal(expected, McpPermissionVisibility.SpecialRoleName(Guid.Parse(id), Ids));
    }

    [Fact]
    public void SpecialRoleName_ReturnsNullForAUser()
    {
        Assert.Null(McpPermissionVisibility.SpecialRoleName(SomeUser, Ids));
    }
}

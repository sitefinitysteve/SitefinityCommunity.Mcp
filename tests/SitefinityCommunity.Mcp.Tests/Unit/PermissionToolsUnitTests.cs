using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class PermissionToolsUnitTests
{
    private static (PermissionTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        var tools = new PermissionTools(mock);
        return (tools, mock);
    }

    [Fact]
    public async Task GetPermissions_ReturnsPerRoleActions()
    {
        var (tools, mock) = CreateTools();
        mock.GetPermissionsAsync("/about", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionsResponse
            {
                Target = "/about",
                TargetKind = "page",
                TargetTitle = "About",
                InheritsPermissions = false,
                Permissions =
                [
                    new PermissionEntry
                    {
                        PermissionSetName = "Pages",
                        RoleId = "22222222-2222-2222-2222-222222222222",
                        RoleName = "Administrators",
                        GrantedActions = ["View", "Modify", "Delete"],
                        DeniedActions = []
                    },
                    new PermissionEntry
                    {
                        PermissionSetName = "Pages",
                        RoleName = "Anonymous",
                        GrantedActions = [],
                        DeniedActions = ["View"]
                    }
                ]
            });

        var result = await tools.GetPermissions("/about");

        Assert.Contains("About", result);
        Assert.Contains("Administrators", result);
        Assert.Contains("Anonymous", result);
        Assert.Contains("Modify", result);
        Assert.Contains("\"InheritsPermissions\": false", result);
    }

    [Fact]
    public async Task GetPermissions_PassesTypeFullNameForContent()
    {
        var (tools, mock) = CreateTools();
        mock.GetPermissionsAsync("aaaa1111-2222-3333-4444-555555555555",
                "Telerik.Sitefinity.News.Model.NewsItem", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new PermissionsResponse { TargetKind = "content", TargetTitle = "Press Release" });

        var result = await tools.GetPermissions(
            "aaaa1111-2222-3333-4444-555555555555", "Telerik.Sitefinity.News.Model.NewsItem");

        Assert.Contains("content", result);
        Assert.Contains("Press Release", result);
        await mock.Received().GetPermissionsAsync(
            "aaaa1111-2222-3333-4444-555555555555", "Telerik.Sitefinity.News.Model.NewsItem",
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPermissions_RequiresIdentifier()
    {
        var (tools, _) = CreateTools();

        var result = await tools.GetPermissions("");

        Assert.Contains("Error:", result);
        Assert.Contains("identifier is required", result);
    }

    [Fact]
    public async Task GetPermissions_HandlesHttpError()
    {
        var (tools, mock) = CreateTools();
        mock.GetPermissionsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await tools.GetPermissions("/about");

        Assert.Contains("Error:", result);
        Assert.Contains("Ensure the Sitefinity plugin is installed", result);
    }
}

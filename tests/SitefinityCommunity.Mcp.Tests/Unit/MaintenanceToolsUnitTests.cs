using ModelContextProtocol;
using System.Text.Json;
using NSubstitute;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class MaintenanceToolsUnitTests
{
    private static (MaintenanceTools Tools, ISitefinityMetadataService Meta, IEnvironmentResolver Resolver) CreateTools(
        string envName, bool allowWrite)
    {
        var meta = Substitute.For<ISitefinityMetadataService>();
        var resolver = Substitute.For<IEnvironmentResolver>();
        var config = new EnvironmentConfig
        {
            Url = "https://example.com",
            SitefinityApiKey = "key",
            AllowWriteOperations = allowWrite
        };
        resolver.Resolve(Arg.Any<string?>()).Returns((envName, config));

        var tools = new MaintenanceTools(meta, resolver);
        return (tools, meta, resolver);
    }

    [Fact]
    public async Task ClearCache_Refused_WhenWriteDisabled()
    {
        var (tools, meta, _) = CreateTools("dev", allowWrite: false);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.ClearCache());

        Assert.Contains("Refused", ex.Message);
        Assert.Contains("allowWriteOperations", ex.Message);
        await meta.DidNotReceive().ClearCacheAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearCache_Refused_ForProdEvenWhenFlagSet()
    {
        var (tools, meta, _) = CreateTools("prod", allowWrite: true);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.ClearCache());

        Assert.Contains("Refused", ex.Message);
        Assert.Contains("prod-like", ex.Message);
        await meta.DidNotReceive().ClearCacheAsync(
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ClearCache_Succeeds_WhenAllowed()
    {
        var (tools, meta, _) = CreateTools("dev", allowWrite: true);
        meta.ClearCacheAsync("output", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new MaintenanceResponse
            {
                Operation = "clear-cache",
                Success = true,
                Message = "Cleared the whole Sitefinity cache via SystemManager.ClearWholeCache()."
            });

        var result = JsonSerializer.Serialize(await tools.ClearCache());

        Assert.Contains("clear-cache", result);
        Assert.Contains("\"Success\":true", result);
        await meta.Received().ClearCacheAsync("output", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecycleApp_Refused_WhenWriteDisabled()
    {
        var (tools, meta, _) = CreateTools("staging", allowWrite: false);

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.RecycleApp());

        Assert.Contains("Refused", ex.Message);
        await meta.DidNotReceive().RecycleApplicationAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecycleApp_Succeeds_WhenAllowed()
    {
        var (tools, meta, _) = CreateTools("dev", allowWrite: true);
        meta.RecycleApplicationAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new MaintenanceResponse
            {
                Operation = "recycle",
                Success = true,
                Message = "Application restart initiated."
            });

        var result = JsonSerializer.Serialize(await tools.RecycleApp());

        Assert.Contains("recycle", result);
        Assert.Contains("\"Success\":true", result);
        await meta.Received().RecycleApplicationAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }
}

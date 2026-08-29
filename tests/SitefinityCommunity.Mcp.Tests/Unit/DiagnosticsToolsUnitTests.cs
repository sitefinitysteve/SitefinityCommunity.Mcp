using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tests.Helpers;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

/// <summary>
/// The scheduled-task / search-index diagnostics pair: response shape, string timestamps, the 404
/// plugin-out-of-date mapping, the capability pre-block, and the plugin version handshake.
/// </summary>
[Trait("Category", "Unit")]
public sealed class DiagnosticsToolsUnitTests
{
    private static SitefinityMcpConfig BuildConfig() => new()
    {
        DefaultEnvironment = "dev",
        Environments = new Dictionary<string, EnvironmentConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["dev"] = new EnvironmentConfig
            {
                Url = "https://test.example.com",
                SitefinityApiKey = "test-key",
            },
        },
    };

    private static (DiagnosticsTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        return (new DiagnosticsTools(mock), mock);
    }

    private static (SitefinityMetadataService Service, MockHttpMessageHandler Handler) CreateService(
        string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(json, status);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        return (new SitefinityMetadataService(new EnvironmentResolver(BuildConfig()), factory), handler);
    }

    private static ApiKeyValidationService CreateValidator(string json)
    {
        var handler = new MockHttpMessageHandler(json);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        return new ApiKeyValidationService(
            new EnvironmentResolver(BuildConfig()), factory, NullLogger<ApiKeyValidationService>.Instance);
    }

    // ── Scheduled task status ────────────────────────────────────

    [Fact]
    public async Task GetScheduledTaskStatus_ReturnsRunningAndFailedSections()
    {
        var (tools, mock) = CreateTools();
        mock.GetScheduledTaskStatusAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ScheduledTaskStatusResponse
            {
                ServerTimeZoneId = "Eastern Standard Time",
                ServerUtcOffsetMinutes = -240,
                SnapshotUtc = "2026-08-28T15:00:00Z",
                SnapshotLocal = "2026-08-28T11:00:00",
                RunningNow =
                [
                    new RunningTaskInfo
                    {
                        Name = "Telerik.Sitefinity.Publishing.ReindexTask",
                        Title = "Docs Index",
                        ItemName = "Docs Index",
                        StartedUtc = "2026-08-28T14:30:00Z",
                        StartedLocal = "2026-08-28T10:30:00",
                        StartedSource = "LastExecutedTime",
                        RunningForSeconds = 1800,
                        IsSearchIndexRebuild = true,
                        IndexName = "docs-index",
                        Progress = 42,
                    },
                ],
                Failed =
                [
                    new FailedTaskInfo
                    {
                        Name = "Telerik.Sitefinity.Publishing.ReindexTask",
                        Title = "Full Site Index",
                        ItemName = "Full Site Index",
                        ScheduledForUtc = "2026-08-08T04:00:00Z",
                        ScheduledForLocal = "2026-08-08T00:00:00",
                        ExecutedOnUtc = "2026-08-08T04:00:11Z",
                        ExecutedOnLocal = "2026-08-08T00:00:11",
                        Status = "Failed",
                        StatusMessage = "Index rebuild aborted",
                        IsSearchIndexRebuild = true,
                        IndexName = "full-site-index",
                    },
                ],
                HistoryNote = "Use sitefinity_search_logs with the pattern \"Scheduler: Task executed\".",
            });

        var result = await tools.GetScheduledTaskStatus();

        Assert.Single(result.RunningNow);
        Assert.Single(result.Failed);

        // Regression: the admin's "Name" column (the task's CLR type) must be populated on both lists.
        Assert.Equal("Telerik.Sitefinity.Publishing.ReindexTask", result.RunningNow[0].Name);
        Assert.Equal("Telerik.Sitefinity.Publishing.ReindexTask", result.Failed[0].Name);
        Assert.True(result.RunningNow[0].IsSearchIndexRebuild);
        Assert.Equal(1800, result.RunningNow[0].RunningForSeconds);
        Assert.Equal("Failed", result.Failed[0].Status);

        var json = JsonSerializer.Serialize(result);

        // Regression guard: these must stay pre-formatted STRINGS. As DateTime properties, ServiceStack
        // would send /Date(ms)/ and the 10:30 server-local wall time would be re-rendered as 14:30 UTC.
        Assert.Contains("\"StartedLocal\":\"2026-08-28T10:30:00\"", json);
        Assert.Contains("\"StartedUtc\":\"2026-08-28T14:30:00Z\"", json);
        Assert.Contains("\"ExecutedOnLocal\":\"2026-08-08T00:00:11\"", json);
        Assert.Contains("Full Site Index", json);
    }

    [Fact]
    public async Task GetScheduledTaskStatus_PassesEnvironmentThrough()
    {
        var (tools, mock) = CreateTools();
        mock.GetScheduledTaskStatusAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new ScheduledTaskStatusResponse());

        await tools.GetScheduledTaskStatus("staging");

        await mock.Received(1).GetScheduledTaskStatusAsync("staging", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetScheduledTaskStatus_WrapsFailuresInMcpException()
    {
        var (tools, mock) = CreateTools();
        mock.GetScheduledTaskStatusAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("boom"));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.GetScheduledTaskStatus());

        Assert.Contains("boom", ex.Message);
    }

    // ── Search indexes ───────────────────────────────────────────

    [Fact]
    public async Task ListSearchIndexes_SurfacesStaleIndexAndFailedReindex()
    {
        var (tools, mock) = CreateTools();
        mock.GetSearchIndexesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SearchIndexesResponse
            {
                ServerTimeZoneId = "Eastern Standard Time",
                // Decorators (GuardedSearchServiceDecorator) are unwrapped to the real backend.
                SearchServiceType = "LuceneSearchService",
                TotalIndexes = 2,
                Indexes =
                [
                    new SearchIndexInfo
                    {
                        Name = "docs-index",
                        Title = "Docs Index",
                        TitleSource = "PublishingPointName",
                        Backend = "defaultLucene",
                        PublishingProvider = "SearchPublishingProvider",
                        IsActive = true,
                        Exists = true,
                        DocumentCount = null,
                        LastUpdatedUtc = "2026-08-27T02:11:00Z",
                        LastUpdatedLocal = "2026-08-26T22:11:00",
                        LastUpdatedSource = "IndexFolder",
                        IsRebuilding = true,
                        RebuildProgress = 42,
                        LastReindexStatus = "running",
                        ContentSources = ["Telerik.Sitefinity.News.Model.NewsItem"],
                    },
                    new SearchIndexInfo
                    {
                        Name = "full-site-index",
                        Title = "Full Site Index",
                        Backend = "defaultLucene",
                        PublishingProvider = "SearchPublishingProvider",
                        IsActive = true,
                        Exists = true,
                        LastReindexStatus = "failed",
                        LastReindexUtc = "2026-08-08T04:00:11Z",
                        LastReindexLocal = "2026-08-08T00:00:11",
                    },
                ],
                ProvidersScanned = ["SearchPublishingProvider", "OAPublishingProvider"],
                Warnings = ["Index 'docs-index': document count is not obtainable"],
            });

        var result = await tools.ListSearchIndexes();

        Assert.Equal(2, result.TotalIndexes);
        Assert.True(result.Indexes[0].IsRebuilding);
        Assert.Equal("failed", result.Indexes[1].LastReindexStatus);
        Assert.Null(result.Indexes[0].DocumentCount);

        // Catalog name AND display name both travel: task rows name an index the admin's way
        // ("Full Site Index"), search queries name it the catalog way ("full-site-index").
        Assert.Equal("full-site-index", result.Indexes[1].Name);
        Assert.Equal("Full Site Index", result.Indexes[1].Title);
        Assert.Equal("defaultLucene", result.Indexes[1].Backend);
        Assert.Equal("PublishingPointName", result.Indexes[0].TitleSource);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"LastReindexLocal\":\"2026-08-08T00:00:11\"", json);
        // Search indexes live under their own publishing provider, not the default one — the scanned
        // providers are reported so an empty list is diagnosable.
        Assert.Contains("SearchPublishingProvider", json);
        Assert.Contains("LuceneSearchService", json);
        Assert.Contains("NewsItem", json);
    }

    [Fact]
    public async Task ListSearchIndexes_ObscuredBackend_DegradesToNullsAndOneWarning()
    {
        var (tools, mock) = CreateTools();
        mock.GetSearchIndexesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SearchIndexesResponse
            {
                SearchServiceType = "GuardedSearchServiceDecorator",
                TotalIndexes = 1,
                Indexes =
                [
                    new SearchIndexInfo
                    {
                        Name = "docs-index",
                        // Derived from the catalog name: a pipe-type label like "SearchIndexPipe" must
                        // never surface as a title.
                        Title = "Docs Index",
                        TitleSource = "derived",
                        Backend = null,
                        Exists = true,
                        DocumentCount = null,
                        LastUpdatedUtc = "2026-08-10T23:33:29Z",
                        LastUpdatedSource = "LastPublicationDate",
                    },
                ],
                Warnings = ["Backend obscured by 'GuardedSearchServiceDecorator': ..."],
            });

        var result = await tools.ListSearchIndexes();
        var index = result.Indexes[0];

        // Backend reports null rather than the wrapper's type name, and one response-level warning
        // explains both gaps instead of a near-identical note per index.
        Assert.Null(index.Backend);
        Assert.Null(index.DocumentCount);
        Assert.Single(result.Warnings);
        Assert.Contains("obscured", result.Warnings[0]);

        // Freshness still falls back to the publishing point's last publication date.
        Assert.Equal("LastPublicationDate", index.LastUpdatedSource);
        Assert.Equal("derived", index.TitleSource);
        Assert.DoesNotContain("Pipe", index.Title);
    }

    [Fact]
    public async Task ListSearchIndexes_WrapsFailuresInMcpException()
    {
        var (tools, mock) = CreateTools();
        mock.GetSearchIndexesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("no route"));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.ListSearchIndexes());

        Assert.Contains("no route", ex.Message);
    }

    // ── Transport-level behaviour ────────────────────────────────

    [Fact]
    public async Task GetScheduledTaskStatusAsync_CallsTheScheduledTasksRoute()
    {
        var json = JsonSerializer.Serialize(new ScheduledTaskStatusResponse { ServerTimeZoneId = "UTC" });
        var (service, handler) = CreateService(json);

        var result = await service.GetScheduledTaskStatusAsync();

        Assert.Equal("UTC", result.ServerTimeZoneId);
        Assert.Contains("/RestApi/mcp/scheduled-tasks", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetSearchIndexesAsync_CallsTheSearchIndexesRoute()
    {
        var json = JsonSerializer.Serialize(new SearchIndexesResponse { TotalIndexes = 0 });
        var (service, handler) = CreateService(json);

        await service.GetSearchIndexesAsync();

        Assert.Contains("/RestApi/mcp/search-indexes", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GetScheduledTaskStatusAsync_MapsNotFoundToPluginOutOfDateWithSteps()
    {
        var (service, _) = CreateService("<html>404</html>", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetScheduledTaskStatusAsync());

        Assert.Contains("out of date", ex.Message);
        Assert.Contains("install-plugin.ps1", ex.Message);
        Assert.Contains("rebuild the Sitefinity solution", ex.Message);
    }

    [Fact]
    public async Task GetSearchIndexesAsync_MapsNotFoundToPluginOutOfDateWithSteps()
    {
        var (service, _) = CreateService("<html>404</html>", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetSearchIndexesAsync());

        Assert.Contains("out of date", ex.Message);
        Assert.Contains("install-plugin.ps1", ex.Message);
    }

    // ── Capability gate ──────────────────────────────────────────

    [Fact]
    public void CapabilityGate_MapsBothToolsToTasks()
    {
        Assert.Equal("Tasks", CapabilityGate.GetCapability("sitefinity_get_scheduled_task_status"));
        Assert.Equal("Tasks", CapabilityGate.GetCapability("sitefinity_list_search_indexes"));
    }

    [Fact]
    public void CapabilityGate_PreBlocksBothToolsWhenTasksDisabled()
    {
        var roster = new FeatureRoster { Tasks = false };

        var taskMessage = CapabilityGate.CheckTool("sitefinity_get_scheduled_task_status", roster);
        var indexMessage = CapabilityGate.CheckTool("sitefinity_list_search_indexes", roster);

        Assert.NotNull(taskMessage);
        Assert.NotNull(indexMessage);
        Assert.Contains("McpSettings > Scheduled Tasks", taskMessage);
        Assert.Contains("McpSettings > Scheduled Tasks", indexMessage);
    }

    [Fact]
    public void CapabilityGate_AllowsBothToolsWhenTasksEnabled()
    {
        var roster = new FeatureRoster();

        Assert.True(roster.Tasks);
        Assert.Null(CapabilityGate.CheckTool("sitefinity_get_scheduled_task_status", roster));
        Assert.Null(CapabilityGate.CheckTool("sitefinity_list_search_indexes", roster));
    }

    [Fact]
    public void CapabilityGate_OldPluginWithNoRosterAllowsBothTools()
    {
        Assert.Null(CapabilityGate.CheckTool("sitefinity_get_scheduled_task_status", null));
        Assert.Null(CapabilityGate.CheckTool("sitefinity_list_search_indexes", null));
    }

    // ── Plugin version handshake ─────────────────────────────────

    [Fact]
    public async Task Ping_WithPluginVersion_IsCaptured()
    {
        var validator = CreateValidator("{\"Status\":\"ok\",\"PluginVersion\":\"3.6.0\"}");

        Assert.Equal("3.6.0", await validator.GetPluginVersionAsync());
    }

    [Fact]
    public async Task Ping_WithoutPluginVersion_ReportsNull()
    {
        var validator = CreateValidator("{\"Status\":\"ok\"}");

        Assert.Null(await validator.GetPluginVersionAsync());
    }

    [Fact]
    public void Verdict_MatchingVersions_SaysSo()
    {
        var verdict = PluginVersionAdvisor.BuildVerdict("3.6.0", "3.6.0");

        Assert.Contains("match", verdict);
        Assert.Contains("3.6.0", verdict);
    }

    [Fact]
    public void Verdict_PluginOlder_GivesTheFourUpdateSteps()
    {
        var verdict = PluginVersionAdvisor.BuildVerdict("3.5.0", "3.6.0");

        Assert.Contains("older than the MCP server", verdict);
        Assert.Contains("install-plugin.ps1", verdict);
        Assert.Contains("rebuild the Sitefinity solution", verdict);
        Assert.Contains("recycle the app pool", verdict);
        Assert.Contains("tag v3.6.0", verdict);
    }

    [Fact]
    public void Verdict_ServerOlder_TellsYouToUpdateTheServer()
    {
        var verdict = PluginVersionAdvisor.BuildVerdict("3.7.0", "3.6.0");

        Assert.Contains("older than the site plugin", verdict);
        Assert.Contains("npm install -g sitefinity-comm-mcp@latest", verdict);
        Assert.Contains("restart your MCP client", verdict);
    }

    [Fact]
    public void Verdict_NoVersionReported_NamesThePreReportingBuilds()
    {
        var verdict = PluginVersionAdvisor.BuildVerdict(null, "3.6.0");

        Assert.Contains("3.5.0 or earlier", verdict);
        Assert.Contains("install-plugin.ps1", verdict);
    }

    [Fact]
    public void Compare_HandlesPreReleaseSuffixesAndMalformedInput()
    {
        Assert.Equal(0, PluginVersionAdvisor.Compare("3.6.0-beta.1", "3.6.0"));
        Assert.True(PluginVersionAdvisor.Compare("3.5.9", "3.6.0") < 0);
        Assert.True(PluginVersionAdvisor.Compare("3.10.0", "3.9.0") > 0);

        // Unparsable input must never produce a false "out of date" claim.
        Assert.Equal(0, PluginVersionAdvisor.Compare("not-a-version", "3.6.0"));
    }

    [Fact]
    public void ServerVersion_IsReadFromTheAssembly()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", PluginVersionAdvisor.ServerVersion);
    }
}

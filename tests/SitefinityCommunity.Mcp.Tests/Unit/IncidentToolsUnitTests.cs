using System.Net;
using System.Text.Json;
using ModelContextProtocol;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;
using SitefinityCommunity.Mcp.Tests.Helpers;
using SitefinityCommunity.Mcp.Tools;

namespace SitefinityCommunity.Mcp.Tests.Unit;

[Trait("Category", "Unit")]
public sealed class IncidentToolsUnitTests
{
    private static (IncidentTools Tools, ISitefinityMetadataService Mock) CreateTools()
    {
        var mock = Substitute.For<ISitefinityMetadataService>();
        return (new IncidentTools(mock), mock);
    }

    private static (SitefinityMetadataService Service, MockHttpMessageHandler Handler) CreateService(
        string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        var config = new SitefinityMcpConfig
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

        var handler = new MockHttpMessageHandler(json, status);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://test.example.com") };

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(httpClient);

        return (new SitefinityMetadataService(new EnvironmentResolver(config), factory), handler);
    }

    // ── Window mode ──────────────────────────────────────────────

    [Fact]
    public async Task InvestigateIncident_WindowMode_ReturnsCorrelatedSections()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse
            {
                Mode = "window",
                Window = new IncidentWindowResponse
                {
                    ServerTimeZoneId = "Eastern Standard Time",
                    ServerUtcOffsetMinutes = -240,
                    WindowMinutes = 15,
                    CenterUtc = "2026-08-27T15:00:00Z",
                    CenterLocal = "2026-08-27T11:00:00",
                    RequestedSources = ["sitefinity", "iis", "eventlog", "httperr"],
                    Sitefinity = new IncidentSitefinitySection
                    {
                        Available = true,
                        TotalMatched = 3,
                        MatchedCount = 3,
                        ReturnedCount = 3,
                        Entries =
                        [
                            new IncidentLogEntry
                            {
                                TimestampUtc = "2026-08-27T15:01:00Z",
                                TimestampLocal = "2026-08-27T11:01:00",
                                Severity = "Error",
                                Message = "OutOfMemoryException thrown",
                            },
                        ],
                    },
                    Iis = new IncidentIisSection
                    {
                        Available = true,
                        SiteId = 3,
                        TotalRequests = 412,
                        TotalServerErrors = 27,
                        ReturnedServerErrors = 25,
                        ServerErrorsTruncated = true,
                        StatusHistogram =
                        [
                            new IncidentCount { Key = "200.0", Count = 380 },
                            new IncidentCount { Key = "503.2", Count = 27 },
                        ],
                        RequestsPerMinute =
                        [
                            new IncidentMinuteCount { MinuteUtc = "2026-08-27 15:00", MinuteLocal = "2026-08-27 11:00", Count = 210 },
                        ],
                        ServerErrors =
                        [
                            new IisRequestEntry
                            {
                                Status = 503,
                                SubStatus = 2,
                                UriStem = "/api/orders",
                                UserName = "steve@medportal.ca",
                                ClientIp = "10.0.0.5",
                                TimeTakenMs = 30000,
                            },
                        ],
                    },
                    EventLog = new IncidentEventLogSection
                    {
                        Available = true,
                        Channels =
                        [
                            new EventLogChannel
                            {
                                LogName = "System",
                                Available = true,
                                TotalMatched = 4,
                                ReturnedCount = 4,
                                Entries =
                                [
                                    new EventLogEntryInfo
                                    {
                                        EventId = 5011,
                                        Level = "Error",
                                        ProviderName = "Microsoft-Windows-WAS",
                                        Message = "A process serving application pool 'Medportal' terminated unexpectedly.",
                                    },
                                ],
                            },
                        ],
                    },
                    HttpErr = new IncidentHttpErrSection
                    {
                        Available = true,
                        TotalMatched = 142,
                        MatchedCount = 142,
                        ReturnedCount = 25,
                        Truncated = true,
                        ReasonHistogram = [new IncidentCount { Key = "AppOffline", Count = 142 }],
                    },
                },
            });

        var result = await tools.InvestigateIncident("2026-08-27 11:00");

        Assert.Equal("window", result.Mode);
        Assert.NotNull(result.Window);
        Assert.Null(result.Candidates);
        Assert.Null(result.Search);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("Eastern Standard Time", json);
        Assert.Contains("OutOfMemoryException", json);
        Assert.Contains("503.2", json);
        Assert.Contains("5011", json);
        Assert.Contains("AppOffline", json);
        // Usernames and client IPs are deliberately retained.
        Assert.Contains("steve@medportal.ca", json);
        Assert.Contains("10.0.0.5", json);

        // Regression: these were DateTime properties, which ServiceStack serializes as /Date(ms)/ — an
        // instant — so the 11:00 server-local wall time came back re-rendered as 15:00 UTC. Preformatted
        // strings keep the two readings distinct all the way through.
        Assert.Contains("\"CenterLocal\":\"2026-08-27T11:00:00\"", json);
        Assert.Contains("\"CenterUtc\":\"2026-08-27T15:00:00Z\"", json);
        Assert.Contains("\"TimestampLocal\":\"2026-08-27T11:01:00\"", json);
    }

    [Fact]
    public async Task InvestigateIncident_WindowMode_UsesPerMinuteSeriesAndCarriesReferer()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse
            {
                Mode = "window",
                Window = new IncidentWindowResponse
                {
                    Iis = new IncidentIisSection
                    {
                        Available = true,
                        TotalRequests = 412,
                        RequestsPerMinute =
                        [
                            new IncidentMinuteCount { MinuteUtc = "2026-08-27 15:00", MinuteLocal = "2026-08-27 11:00", Count = 210 },
                        ],
                        ServerErrors =
                        [
                            new IisRequestEntry
                            {
                                Status = 500,
                                UriStem = "/appstatus",
                                Referer = "https://dev.medportal.ca/ug/macdot",
                            },
                        ],
                    },
                },
            });

        var result = await tools.InvestigateIncident("11:00");
        var iis = result.Window!.Iis!;

        // Window mode fills the minute series and leaves the hour series empty.
        Assert.Single(iis.RequestsPerMinute);
        Assert.Empty(iis.RequestsPerHour);

        // Referer is surfaced so a query that matched via the referer is explicable rather than
        // looking like a false positive.
        Assert.Equal("https://dev.medportal.ca/ug/macdot", iis.ServerErrors[0].Referer);
    }

    [Fact]
    public async Task InvestigateIncident_SearchMode_UsesPerHourSeries()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse
            {
                Mode = "search",
                Search = new IncidentSearchResponse
                {
                    Query = "macdot",
                    LookbackHours = 24,
                    Iis = new IncidentIisSection
                    {
                        Available = true,
                        TotalRequests = 91240,
                        // A 24h lookback is 1440 minute rows but only 24 hour rows — the difference
                        // between a 47 KB zero-match response and a readable one.
                        RequestsPerHour =
                        [
                            new IncidentMinuteCount { MinuteUtc = "2026-08-27 15:00", MinuteLocal = "2026-08-27 11:00", Count = 4102 },
                        ],
                    },
                },
            });

        var result = await tools.InvestigateIncident(query: "macdot", lookbackHours: 24);
        var iis = result.Search!.Iis!;

        Assert.Single(iis.RequestsPerHour);
        Assert.Empty(iis.RequestsPerMinute);
    }

    [Fact]
    public async Task InvestigateIncident_PassesArgumentsThrough()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse { Mode = "window", Window = new IncidentWindowResponse() });

        await tools.InvestigateIncident("11:00", 45, "steve@medportal.ca", 24, "iis,eventlog", "staging");

        await mock.Received(1).GetIncidentWindowAsync(
            "11:00", 45, 24, "steve@medportal.ca", "iis,eventlog", "staging", Arg.Any<CancellationToken>());
    }

    // ── Discovery mode ───────────────────────────────────────────

    [Fact]
    public async Task InvestigateIncident_NoTimeNoQuery_ReturnsCandidates()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse
            {
                Mode = "candidates",
                Candidates = new IncidentCandidatesResponse
                {
                    ServerTimeZoneId = "Eastern Standard Time",
                    LookbackHours = 72,
                    TotalSignals = 31,
                    TotalCandidates = 4,
                    ReturnedCount = 4,
                    ScannedSources = ["eventlog", "httperr", "sitefinity"],
                    Candidates =
                    [
                        new IncidentCandidate
                        {
                            TimestampUtc = "2026-08-27T15:01:00Z",
                            TimestampLocal = "2026-08-27T11:01:00",
                            Signal = "WAS 5011 worker process crash",
                            Source = "eventlog",
                            Detail = "7 signals spanning 3.2 min: 2 x HTTPERR 503 burst (AppOffline x142)",
                            SignalCount = 7,
                        },
                    ],
                    Warnings = ["Discovery scans only the cheap high-signal sources"],
                },
            });

        var result = await tools.InvestigateIncident();

        Assert.Equal("candidates", result.Mode);
        Assert.NotNull(result.Candidates);
        Assert.Null(result.Window);

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("WAS 5011 worker process crash", json);
        Assert.Contains("AppOffline", json);
        Assert.Contains("eventlog", json);
    }

    // ── Search mode ──────────────────────────────────────────────

    [Fact]
    public async Task InvestigateIncident_QueryWithoutTime_ReturnsSearch()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse
            {
                Mode = "search",
                Search = new IncidentSearchResponse
                {
                    Query = "steve@medportal.ca",
                    LookbackHours = 72,
                    ScannedSources = ["sitefinity", "iis", "eventlog", "httperr"],
                    Iis = new IncidentIisSection
                    {
                        Available = true,
                        TotalRequests = 91240,
                        MatchedCount = 63,
                        MatchedRequestsTruncated = true,
                        MatchedRequests =
                        [
                            new IisRequestEntry
                            {
                                Status = 200,
                                UriStem = "/account/profile",
                                UserName = "steve@medportal.ca",
                                ClientIp = "10.0.0.5",
                            },
                            new IisRequestEntry
                            {
                                Status = 500,
                                UriStem = "/api/orders",
                                UserName = "steve@medportal.ca",
                                ClientIp = "10.0.0.5",
                            },
                        ],
                    },
                    Sitefinity = new IncidentSitefinitySection { Available = true, TotalMatched = 900, MatchedCount = 2 },
                },
            });

        var result = await tools.InvestigateIncident(query: "steve@medportal.ca");

        Assert.Equal("search", result.Mode);
        Assert.NotNull(result.Search);
        Assert.Null(result.Window);
        Assert.Null(result.Candidates);
        Assert.Equal(63, result.Search!.Iis!.MatchedCount);

        var json = JsonSerializer.Serialize(result);
        // The full request trail is returned at ALL status codes, not just 5xx.
        Assert.Contains("/account/profile", json);
        Assert.Contains("/api/orders", json);
    }

    [Fact]
    public async Task InvestigateIncident_TimeAndQuery_FiltersWindowButKeepsAggregates()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IncidentResponse
            {
                Mode = "window",
                Window = new IncidentWindowResponse
                {
                    Query = "steve@medportal.ca",
                    Iis = new IncidentIisSection
                    {
                        Available = true,
                        // Aggregates stay unfiltered so traffic context survives the filter.
                        TotalRequests = 412,
                        StatusHistogram = [new IncidentCount { Key = "200.0", Count = 380 }],
                        MatchedCount = 4,
                        MatchedRequests =
                        [
                            new IisRequestEntry { Status = 200, UriStem = "/account", UserName = "steve@medportal.ca" },
                        ],
                    },
                },
            });

        var result = await tools.InvestigateIncident("11:00", query: "steve@medportal.ca");

        Assert.Equal("window", result.Mode);
        Assert.Equal(412, result.Window!.Iis!.TotalRequests);
        Assert.Equal(4, result.Window.Iis.MatchedCount);
        Assert.Single(result.Window.Iis.MatchedRequests);
    }

    // ── Error mapping ────────────────────────────────────────────

    [Fact]
    public async Task InvestigateIncident_WrapsFailuresInMcpException()
    {
        var (tools, mock) = CreateTools();
        mock.GetIncidentWindowAsync(
                Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException(
                "The /RestApi/mcp/incident-window endpoint returned 404 — the installed Sitefinity plugin is " +
                "out of date. Re-run install-plugin.ps1 against the Sitefinity project and rebuild the site."));

        var ex = await Assert.ThrowsAsync<McpException>(() => tools.InvestigateIncident("11:00"));

        Assert.Contains("out of date", ex.Message);
        Assert.Contains("install-plugin.ps1", ex.Message);
    }

    // ── Transport-level behaviour ────────────────────────────────

    [Fact]
    public async Task GetIncidentWindowAsync_MapsNotFoundToPluginOutOfDate()
    {
        var (service, _) = CreateService("<html>404</html>", HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetIncidentWindowAsync("2026-08-27 11:00"));

        Assert.Contains("out of date", ex.Message);
        Assert.Contains("install-plugin.ps1", ex.Message);
    }

    [Fact]
    public async Task GetIncidentWindowAsync_BuildsQueryStringFromArguments()
    {
        var json = JsonSerializer.Serialize(new IncidentResponse
        {
            Mode = "search",
            Search = new IncidentSearchResponse { Query = "order-4471" },
        });

        var (service, handler) = CreateService(json);

        var result = await service.GetIncidentWindowAsync(
            center: null, windowMinutes: 0, lookbackHours: 48, query: "order-4471", sources: "iis,sitefinity");

        Assert.Equal("search", result.Mode);
        Assert.Equal("order-4471", result.Search!.Query);

        var url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("/RestApi/mcp/incident-window", url);
        Assert.Contains("LookbackHours=48", url);
        Assert.Contains("Query=order-4471", url);
        Assert.Contains("Sources=iis%2Csitefinity", url);
        // No Center was supplied, so none should be sent.
        Assert.DoesNotContain("Center=", url);
    }

    [Fact]
    public async Task GetIncidentWindowAsync_SendsCenterAndWindowMinutes()
    {
        var json = JsonSerializer.Serialize(new IncidentResponse
        {
            Mode = "window",
            Window = new IncidentWindowResponse { ServerTimeZoneId = "Eastern Standard Time" },
        });

        var (service, handler) = CreateService(json);

        var result = await service.GetIncidentWindowAsync("2026-08-27 11:00", 30);

        Assert.Equal("window", result.Mode);
        Assert.Equal("Eastern Standard Time", result.Window!.ServerTimeZoneId);

        var url = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("Center=2026-08-27", url);
        Assert.Contains("WindowMinutes=30", url);
    }
}

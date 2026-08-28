// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Hosting;
using Telerik.Sitefinity.Configuration;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Incident forensics across four independent log sources, in two modes.
    ///
    /// <para>
    /// <b>Window mode</b> (a <c>Center</c> time was supplied) reconstructs one moment: Sitefinity's own
    /// logs, the site's IIS W3C access log, the Windows Application and System event logs, and the
    /// http.sys HTTPERR log, all filtered to the same window.
    /// </para>
    ///
    /// <para>
    /// <b>Discovery mode</b> (no <c>Center</c>) answers "when did it break?" — it scans only the cheap
    /// high-signal sources (event-log crash records, HTTPERR bursts, Sitefinity error bursts) over the
    /// lookback period and returns clustered candidate moments to feed back as a <c>Center</c>. The IIS
    /// W3C access logs are deliberately NOT scanned in discovery mode: they are far too large to sweep
    /// over multi-day ranges.
    /// </para>
    ///
    /// <para>
    /// Clock discipline is the point of this endpoint. Sitefinity logs server-local time, W3C access
    /// logs and HTTPERR are always UTC, and Event Log records are stored in UTC — so every entry is
    /// emitted with BOTH a UTC and a server-local timestamp, and the response states the server's time
    /// zone and the offset that applied at the queried instant (not "now", which would be wrong across
    /// a DST boundary).
    /// </para>
    ///
    /// <para>
    /// Read-only and strictly synchronous — no background jobs. Work is bounded by fixed caps, a
    /// line-scan ceiling, and a wall-clock budget; every source is wrapped so a missing folder or a
    /// denied ACL becomes a warning with an actionable fix rather than a failed response. IIS lines are
    /// never returned raw: only aggregates plus a capped list of 5xx and slowest requests. All outbound
    /// strings pass through <see cref="McpSecretRedactor"/>; <c>cs(Cookie)</c> and
    /// <c>cs(Authorization)</c> columns are never read at all.
    /// </para>
    /// </summary>
    [McpApiKey]
    public class McpSystemLogService : Service
    {
        private const int DefaultWindowMinutes = 15;
        private const int MinWindowMinutes = 1;
        private const int MaxWindowMinutes = 120;

        private const int DefaultLookbackHours = 72;
        private const int MinLookbackHours = 1;
        private const int MaxLookbackHours = 336;

        private const int SitefinityEntryCap = 20;
        private const int EventLogEntryCap = 25;
        private const int IisServerErrorCap = 25;
        private const int IisSlowestCap = 10;

        /// <summary>Cap on IIS requests returned when a Query filter is in play (the user's request trail).</summary>
        private const int IisMatchedCap = 50;

        private const int HttpErrEntryCap = 25;
        private const int CandidateCap = 20;

        private const int EventMessageMaxChars = 1000;

        /// <summary>Per-property clip used when an event has to be summarised from its raw properties.</summary>
        private const int EventPropertyMaxChars = 200;

        /// <summary>Signals closer together than this fold into a single candidate incident.</summary>
        private const int CandidateClusterMinutes = 10;

        /// <summary>Minimum HTTPERR records inside one minute before it counts as a burst.</summary>
        private const int HttpErrBurstThreshold = 5;

        /// <summary>Minimum Sitefinity errors inside one 5-minute bucket before it counts as a burst.</summary>
        private const int SitefinityBurstThreshold = 5;

        /// <summary>Hard ceiling on log lines examined per source, so a busy site cannot stall the request.</summary>
        private const long LineScanCeiling = 2000000L;

        /// <summary>
        /// Cap on how much of a single Sitefinity entry is accumulated. Everything this endpoint parses
        /// (Timestamp, Severity, Type, Message, Requested URL, the first stack frames) lives at the top of
        /// the block, so a runaway entry stops growing here instead of pulling megabytes into memory.
        /// </summary>
        private const int SitefinityBlockMaxChars = 32 * 1024;

        /// <summary>Most HTTPERR files scanned per pass — the NEWEST ones, which cover the end of the window.</summary>
        private const int HttpErrFileCap = 4;

        /// <summary>Ceiling on event records examined per channel during discovery.</summary>
        private const int DiscoveryEventCeiling = 5000;

        /// <summary>Whole-request wall-clock budget. Beyond this, collection stops and reports partial results.</summary>
        private const int TimeBudgetSeconds = 30;

        private const string SitefinitySeparator = "----------------------------------------";

        private static readonly string SitefinityLogsPath =
            HostingEnvironment.MapPath("~/App_Data/Sitefinity/Logs") ?? string.Empty;

        /// <summary>Admission gate: 1 while a scan is running, 0 when free. See <c>Get</c>.</summary>
        private static int ScanInProgress;

        /// <summary>
        /// Providers worth surfacing from the System channel. Anything else in System is kept only when
        /// it is an outright Error or Critical, which is where app-pool crashes actually show up
        /// (WAS 5009/5010/5011/5117, Service Control Manager, http.sys).
        /// </summary>
        private static readonly string[] SystemProviderAllowList =
        {
            "WAS",
            "Microsoft-Windows-WAS",
            "W3SVC",
            "Microsoft-Windows-IIS-W3SVC",
            "Microsoft-Windows-IIS-W3SVC-WP",
            "HTTP",
            "Microsoft-Windows-HttpEvent",
            "Service Control Manager",
            "Microsoft-Windows-Eventlog",
            "EventLog",
        };

        /// <summary>
        /// GET /RestApi/mcp/incident-window?Center=...&amp;WindowMinutes=15&amp;Sources=sitefinity,iis,eventlog,httperr
        /// <para>
        /// Omit <c>Center</c> for discovery mode:
        /// GET /RestApi/mcp/incident-window?LookbackHours=72
        /// </para>
        /// </summary>
        public McpIncidentResponse Get(GetIncidentWindow request)
        {
            // One scan at a time per app domain. A log sweep is IO-heavy and deliberately unthrottled
            // within its budget; two of them racing would double the disk pressure on a site that is, by
            // definition, already having a bad day.
            if (Interlocked.CompareExchange(ref ScanInProgress, 1, 0) != 0)
            {
                throw new HttpError((HttpStatusCode)429,
                    "another incident scan is already running; retry in a few seconds");
            }

            try
            {
                var budget = new ScanBudget(TimeBudgetSeconds);
                var query = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();

                if (!string.IsNullOrWhiteSpace(request.Center))
                {
                    return new McpIncidentResponse
                    {
                        Mode = "window",
                        Window = RunWindow(request, query, budget),
                    };
                }

                if (query != null)
                {
                    return new McpIncidentResponse
                    {
                        Mode = "search",
                        Search = RunSearch(request, query, budget),
                    };
                }

                return new McpIncidentResponse
                {
                    Mode = "candidates",
                    Candidates = RunDiscovery(request, budget),
                };
            }
            finally
            {
                Interlocked.Exchange(ref ScanInProgress, 0);
            }
        }

        // ── Window mode ──────────────────────────────────────────────

        private static McpIncidentWindowResponse RunWindow(GetIncidentWindow request, string query, ScanBudget budget)
        {
            var response = new McpIncidentWindowResponse();

            var windowMinutes = request.WindowMinutes <= 0 ? DefaultWindowMinutes : request.WindowMinutes;

            if (windowMinutes < MinWindowMinutes)
            {
                windowMinutes = MinWindowMinutes;
            }

            if (windowMinutes > MaxWindowMinutes)
            {
                response.Warnings.Add("WindowMinutes " + request.WindowMinutes + " exceeds the " +
                    MaxWindowMinutes + "-minute ceiling; clamped to " + MaxWindowMinutes + ".");
                windowMinutes = MaxWindowMinutes;
            }

            DateTimeOffset center;

            if (!DateTimeOffset.TryParse(request.Center.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out center))
            {
                throw HttpError.BadRequest(
                    "Could not parse Center '" + request.Center + "'. Use \"11:00\", \"2026-08-27 11:00\", " +
                    "or a full ISO 8601 value. Without an explicit offset the value is read as server-local time.");
            }

            var centerUtc = center.UtcDateTime;
            var startUtc = centerUtc.AddMinutes(-windowMinutes);
            var endUtc = centerUtc.AddMinutes(windowMinutes);

            // Header offset is evaluated AT the queried instant, so a window sitting in a different DST
            // period than "now" reports the offset that actually applied. Individual entries do NOT reuse
            // it — each one is converted through TimeZoneInfo.Local at its own instant, so a window that
            // straddles a DST transition stays correct on both sides of it.
            var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTimeOffset(centerUtc, TimeSpan.Zero));

            response.ServerTimeZoneId = TimeZoneInfo.Local.Id;
            response.ServerUtcOffsetMinutes = (int)offset.TotalMinutes;
            response.WindowMinutes = windowMinutes;
            response.CenterUtc = FormatUtc(centerUtc);
            response.CenterLocal = FormatLocal(ToLocal(centerUtc));
            response.WindowStartUtc = FormatUtc(startUtc);
            response.WindowEndUtc = FormatUtc(endUtc);
            response.WindowStartLocal = FormatLocal(ToLocal(startUtc));
            response.WindowEndLocal = FormatLocal(ToLocal(endUtc));

            var sources = ParseSources(request.Sources, response.Warnings);
            response.RequestedSources = sources.ToList();
            response.Query = query;

            if (sources.Contains("sitefinity") && !budget.Expired)
            {
                response.Sitefinity = CollectSitefinity(startUtc, endUtc, query, budget);
            }

            if (sources.Contains("eventlog") && !budget.Expired)
            {
                response.EventLog = CollectEventLog(startUtc, endUtc, query, budget);
            }

            if (sources.Contains("httperr") && !budget.Expired)
            {
                response.HttpErr = CollectHttpErr(startUtc, endUtc, query, budget);
            }

            // IIS last: it is the most expensive source, so if the budget runs out it should be the one
            // left partial rather than starving the crash evidence in the event logs.
            if (sources.Contains("iis") && !budget.Expired)
            {
                response.Iis = CollectIis(startUtc, endUtc, query, budget, false);
            }

            if (budget.Expired)
            {
                response.Warnings.Add(budget.Message);
            }

            return response;
        }

        // ── Search mode ──────────────────────────────────────────────

        /// <summary>
        /// Sweeps every source over the lookback period for a plain substring. Unlike discovery, the IIS
        /// access log IS scanned here — the line ceiling and the wall-clock budget bound the work, and a
        /// partial result carries a warning telling the caller to shorten the lookback.
        /// </summary>
        private static McpIncidentSearchResponse RunSearch(GetIncidentWindow request, string query, ScanBudget budget)
        {
            var response = new McpIncidentSearchResponse { Query = query };

            var lookbackHours = ClampLookbackHours(request.LookbackHours, response.Warnings);

            var endUtc = DateTime.UtcNow;
            var startUtc = endUtc.AddHours(-lookbackHours);
            var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTimeOffset(endUtc, TimeSpan.Zero));

            response.ServerTimeZoneId = TimeZoneInfo.Local.Id;
            response.ServerUtcOffsetMinutes = (int)offset.TotalMinutes;
            response.LookbackHours = lookbackHours;
            response.LookbackStartUtc = FormatUtc(startUtc);
            response.LookbackEndUtc = FormatUtc(endUtc);
            response.LookbackStartLocal = FormatLocal(ToLocal(startUtc));
            response.LookbackEndLocal = FormatLocal(ToLocal(endUtc));

            var sources = ParseSources(request.Sources, response.Warnings);
            response.ScannedSources = sources.ToList();

            if (sources.Contains("sitefinity") && !budget.Expired)
            {
                response.Sitefinity = CollectSitefinity(startUtc, endUtc, query, budget);
            }

            if (sources.Contains("eventlog") && !budget.Expired)
            {
                response.EventLog = CollectEventLog(startUtc, endUtc, query, budget);
            }

            if (sources.Contains("httperr") && !budget.Expired)
            {
                response.HttpErr = CollectHttpErr(startUtc, endUtc, query, budget);
            }

            if (sources.Contains("iis") && !budget.Expired)
            {
                // Hourly buckets here: a 14-day lookback would otherwise emit ~20,000 minute rows into a
                // result the caller is reading for its matches, not its traffic shape.
                response.Iis = CollectIis(startUtc, endUtc, query, budget, true);
            }

            if (budget.Expired)
            {
                response.Warnings.Add(budget.Message + " Shorten LookbackHours (currently " + lookbackHours +
                    ") or restrict Sources to complete the sweep.");
            }

            return response;
        }

        private static int ClampLookbackHours(int requested, List<string> warnings)
        {
            var hours = requested <= 0 ? DefaultLookbackHours : requested;

            if (hours < MinLookbackHours)
            {
                return MinLookbackHours;
            }

            if (hours > MaxLookbackHours)
            {
                warnings.Add("LookbackHours " + requested + " exceeds the " + MaxLookbackHours +
                    "-hour ceiling; clamped to " + MaxLookbackHours + ".");
                return MaxLookbackHours;
            }

            return hours;
        }

        // ── Discovery mode ───────────────────────────────────────────

        private static McpIncidentCandidatesResponse RunDiscovery(GetIncidentWindow request, ScanBudget budget)
        {
            var response = new McpIncidentCandidatesResponse();

            var lookbackHours = ClampLookbackHours(request.LookbackHours, response.Warnings);

            var endUtc = DateTime.UtcNow;
            var startUtc = endUtc.AddHours(-lookbackHours);
            var offset = TimeZoneInfo.Local.GetUtcOffset(new DateTimeOffset(endUtc, TimeSpan.Zero));

            response.ServerTimeZoneId = TimeZoneInfo.Local.Id;
            response.ServerUtcOffsetMinutes = (int)offset.TotalMinutes;
            response.LookbackHours = lookbackHours;
            response.LookbackStartUtc = FormatUtc(startUtc);
            response.LookbackEndUtc = FormatUtc(endUtc);
            response.LookbackStartLocal = FormatLocal(ToLocal(startUtc));
            response.LookbackEndLocal = FormatLocal(ToLocal(endUtc));

            response.Warnings.Add("Discovery scans only the cheap high-signal sources (event logs, HTTPERR " +
                "bursts, Sitefinity error bursts). IIS W3C access logs are NOT scanned here — they are too " +
                "large to sweep over a multi-day range. Call again with a Center time to get IIS detail.");

            var signals = new List<DiscoverySignal>();

            if (!budget.Expired)
            {
                response.ScannedSources.Add("eventlog");
                DiscoverEventSignals("Application", startUtc, endUtc, signals, response.Warnings, budget);
            }

            if (!budget.Expired)
            {
                DiscoverEventSignals("System", startUtc, endUtc, signals, response.Warnings, budget);
            }

            if (!budget.Expired)
            {
                response.ScannedSources.Add("httperr");
                DiscoverHttpErrSignals(startUtc, endUtc, signals, response.Warnings, budget);
            }

            if (!budget.Expired)
            {
                response.ScannedSources.Add("sitefinity");
                DiscoverSitefinitySignals(startUtc, endUtc, signals, response.Warnings, budget);
            }

            if (budget.Expired)
            {
                response.Warnings.Add(budget.Message);
            }

            response.TotalSignals = signals.Count;

            var clustered = ClusterSignals(signals);
            response.TotalCandidates = clustered.Count;

            var returned = clustered
                .OrderByDescending(c => c.TimestampUtc)
                .Take(CandidateCap)
                .ToList();

            response.Candidates = returned;
            response.ReturnedCount = returned.Count;
            response.Truncated = clustered.Count > returned.Count;

            if (returned.Count == 0 && !budget.Expired)
            {
                response.Warnings.Add("No crash-shaped signals were found in the last " + lookbackHours +
                    " hour(s). Either nothing crashed, or the sources are unreadable (check the warnings above).");
            }

            return response;
        }

        private static void DiscoverEventSignals(
            string logName,
            DateTime startUtc,
            DateTime endUtc,
            List<DiscoverySignal> signals,
            List<string> warnings,
            ScanBudget budget)
        {
            try
            {
                // Critical + Error only during discovery — warnings are noise when hunting for a crash.
                var xpath = "*[System[(Level=1 or Level=2) and TimeCreated[@SystemTime>='" +
                    FormatEventTime(startUtc) + "' and @SystemTime<='" + FormatEventTime(endUtc) + "']]]";

                var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };

                using (var reader = new EventLogReader(query))
                {
                    EventRecord record;
                    var examined = 0;

                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            examined++;

                            if (examined >= DiscoveryEventCeiling)
                            {
                                warnings.Add("Stopped after " + DiscoveryEventCeiling + " records in the '" +
                                    logName + "' log; shorten LookbackHours for a complete sweep.");
                                return;
                            }

                            if ((examined % 200) == 0 && budget.Expired)
                            {
                                return;
                            }

                            var signal = ClassifyEvent(logName, record);

                            if (signal != null)
                            {
                                signals.Add(signal);
                            }
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add(BuildEventLogAccessWarning(logName, null));
            }
            catch (EventLogNotFoundException)
            {
                warnings.Add("Event log channel '" + logName + "' was not found on this machine.");
            }
            catch (EventLogException ex)
            {
                var message = ex.Message ?? string.Empty;

                warnings.Add(message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
                    ? BuildEventLogAccessWarning(logName, message)
                    : "Could not scan the '" + logName + "' event log: " + message);
            }
            catch (Exception ex)
            {
                warnings.Add("Unexpected failure scanning the '" + logName + "' event log: " + ex.Message);
            }
        }

        /// <summary>
        /// Turns one event record into a ranked crash signal, or null when it is not worth surfacing as a
        /// candidate incident moment.
        /// </summary>
        private static DiscoverySignal ClassifyEvent(string logName, EventRecord record)
        {
            var provider = record.ProviderName ?? string.Empty;
            var id = record.Id;
            var stamp = record.TimeCreated.HasValue ? record.TimeCreated.Value.ToUniversalTime() : DateTime.UtcNow;

            if (string.Equals(logName, "System", StringComparison.OrdinalIgnoreCase))
            {
                if (provider.IndexOf("WAS", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new DiscoverySignal(stamp, "eventlog", 100, "WAS " + id + " " + DescribeWasEvent(id));
                }

                if (!IsAllowedSystemProvider(provider))
                {
                    return null;
                }

                return new DiscoverySignal(stamp, "eventlog", 40, provider + " " + id + " error");
            }

            if (id == 1000)
            {
                return new DiscoverySignal(stamp, "eventlog", 90,
                    "Application Error 1000 (" + FirstProperty(record, "faulting application") + ")");
            }

            if (id == 1026)
            {
                return new DiscoverySignal(stamp, "eventlog", 85, ".NET Runtime 1026 unhandled exception");
            }

            if (provider.IndexOf("ASP.NET", StringComparison.OrdinalIgnoreCase) >= 0
                || provider.IndexOf(".NET Runtime", StringComparison.OrdinalIgnoreCase) >= 0
                || provider.IndexOf("Application Error", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new DiscoverySignal(stamp, "eventlog", 50, provider + " " + id + " error");
            }

            return new DiscoverySignal(stamp, "eventlog", 30, provider + " " + id + " error");
        }

        private static string DescribeWasEvent(int id)
        {
            switch (id)
            {
                case 5009: return "worker process terminated unexpectedly";
                case 5010: return "worker process failed to ping (unresponsive)";
                case 5011: return "worker process crash";
                case 5117: return "app pool disabled by rapid-fail protection";
                case 5002: return "app pool disabled (rapid-fail protection)";
                case 5013: return "worker process shut down (idle timeout)";
                case 5079: return "app pool startup failure";
                default: return "app-pool event";
            }
        }

        private static string FirstProperty(EventRecord record, string fallback)
        {
            try
            {
                var properties = record.Properties;

                if (properties != null && properties.Count > 0 && properties[0].Value != null)
                {
                    var value = properties[0].Value.ToString();

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return McpSecretRedactor.Redact(value.Trim());
                    }
                }
            }
            catch (Exception)
            {
                // Property access can throw on partially-rendered records — the fallback is fine.
            }

            return fallback;
        }

        private static void DiscoverHttpErrSignals(
            DateTime startUtc,
            DateTime endUtc,
            List<DiscoverySignal> signals,
            List<string> warnings,
            ScanBudget budget)
        {
            var folder = HttpErrFolder();

            try
            {
                if (!Directory.Exists(folder))
                {
                    warnings.Add("HTTPERR folder '" + folder + "' not found; skipped in discovery.");
                    return;
                }

                var files = new DirectoryInfo(folder).GetFiles("httperr*.log")
                    .Where(f => f.LastWriteTimeUtc >= startUtc)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                // minute → (count, reason → count)
                var buckets = new Dictionary<DateTime, Dictionary<string, int>>();
                long lines = 0;

                foreach (var file in files)
                {
                    if (budget.Expired)
                    {
                        return;
                    }

                    try
                    {
                        BucketHttpErrFile(file, startUtc, endUtc, buckets, ref lines, budget);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        warnings.Add(BuildHttpErrAccessWarning(folder));
                        return;
                    }
                    catch (IOException ex)
                    {
                        warnings.Add("Could not read HTTPERR log '" + file.Name + "': " + ex.Message);
                    }
                }

                foreach (var bucket in buckets)
                {
                    var total = bucket.Value.Values.Sum();

                    if (total < HttpErrBurstThreshold)
                    {
                        continue;
                    }

                    var dominant = bucket.Value
                        .OrderByDescending(kvp => kvp.Value)
                        .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                        .First();

                    signals.Add(new DiscoverySignal(bucket.Key, "httperr", 70,
                        "HTTPERR 503 burst (" + dominant.Key + " x" + total + ")"));
                }
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add(BuildHttpErrAccessWarning(folder));
            }
            catch (Exception ex)
            {
                warnings.Add("HTTPERR discovery failed: " + ex.Message);
            }
        }

        private static void BucketHttpErrFile(
            FileInfo file,
            DateTime startUtc,
            DateTime endUtc,
            Dictionary<DateTime, Dictionary<string, int>> buckets,
            ref long lines,
            ScanBudget budget)
        {
            Dictionary<string, int> fields = null;

            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    lines++;

                    if (lines >= LineScanCeiling)
                    {
                        return;
                    }

                    if ((lines % 20000) == 0 && budget.Expired)
                    {
                        return;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line[0] == '#')
                    {
                        if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
                        {
                            fields = BuildFieldMap(line.Substring("#Fields:".Length));
                        }

                        continue;
                    }

                    if (fields == null)
                    {
                        continue;
                    }

                    var parts = line.Split(' ');

                    DateTime stampUtc;

                    if (!TryReadW3CTimestamp(parts, fields, out stampUtc)
                        || stampUtc < startUtc || stampUtc > endUtc)
                    {
                        continue;
                    }

                    var minute = TruncateToMinutes(stampUtc, 1);
                    var reason = ReadValue(parts, fields, "s-reason");

                    if (string.IsNullOrEmpty(reason))
                    {
                        reason = "(none)";
                    }

                    Dictionary<string, int> bucket;

                    if (!buckets.TryGetValue(minute, out bucket))
                    {
                        bucket = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        buckets[minute] = bucket;
                    }

                    Increment(bucket, reason);
                }
            }
        }

        private static void DiscoverSitefinitySignals(
            DateTime startUtc,
            DateTime endUtc,
            List<DiscoverySignal> signals,
            List<string> warnings,
            ScanBudget budget)
        {
            try
            {
                if (string.IsNullOrEmpty(SitefinityLogsPath) || !Directory.Exists(SitefinityLogsPath))
                {
                    warnings.Add("Sitefinity log folder not found at '" + SitefinityLogsPath + "'; skipped in discovery.");
                    return;
                }

                var files = new DirectoryInfo(SitefinityLogsPath).EnumerateFiles("Error*.log")
                    .Where(f => f.LastWriteTimeUtc >= startUtc)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                var buckets = new Dictionary<DateTime, int>();
                var state = new SitefinityScanState();

                foreach (var file in files)
                {
                    if (budget.Expired || state.StoppedEarly)
                    {
                        break;
                    }

                    try
                    {
                        BucketSitefinityFile(file, startUtc, endUtc, buckets, state, budget);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        warnings.Add("Access denied reading '" + file.Name + "': " + ex.Message);
                    }
                    catch (IOException)
                    {
                        warnings.Add("Could not read '" + file.Name + "' (locked by Sitefinity).");
                    }
                }

                if (state.CeilingHit)
                {
                    warnings.Add("Sitefinity discovery stopped at the " +
                        LineScanCeiling.ToString("N0", CultureInfo.InvariantCulture) +
                        "-line ceiling; error-burst candidates may be incomplete.");
                }

                foreach (var bucket in buckets)
                {
                    if (bucket.Value < SitefinityBurstThreshold)
                    {
                        continue;
                    }

                    signals.Add(new DiscoverySignal(bucket.Key, "sitefinity", 60,
                        "Sitefinity error burst (" + bucket.Value + " errors in 5 min)"));
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Sitefinity discovery failed: " + ex.Message);
            }
        }

        private static void BucketSitefinityFile(
            FileInfo file,
            DateTime startUtc,
            DateTime endUtc,
            Dictionary<DateTime, int> buckets,
            SitefinityScanState state,
            ScanBudget budget)
        {
            StreamSitefinityBlocks(file, state, budget, block =>
            {
                DateTime localStamp;

                if (!TryReadSitefinityTimestamp(block, out localStamp))
                {
                    return;
                }

                var utc = LocalToUtc(localStamp);

                if (utc < startUtc || utc > endUtc)
                {
                    return;
                }

                Increment(buckets, TruncateToMinutes(utc, 5));
            });
        }

        /// <summary>
        /// Folds signals that land within <see cref="CandidateClusterMinutes"/> of each other into one
        /// candidate. The strongest-ranked signal becomes the headline; everything else is summarised in
        /// the detail line so the count is not lost.
        /// </summary>
        private static List<McpIncidentCandidate> ClusterSignals(List<DiscoverySignal> signals)
        {
            var candidates = new List<McpIncidentCandidate>();

            if (signals.Count == 0)
            {
                return candidates;
            }

            var ordered = signals.OrderBy(s => s.TimestampUtc).ToList();
            var cluster = new List<DiscoverySignal> { ordered[0] };
            var clusterStart = ordered[0].TimestampUtc;

            for (var i = 1; i < ordered.Count; i++)
            {
                if ((ordered[i].TimestampUtc - clusterStart).TotalMinutes <= CandidateClusterMinutes)
                {
                    cluster.Add(ordered[i]);
                    continue;
                }

                candidates.Add(BuildCandidate(cluster));
                cluster = new List<DiscoverySignal> { ordered[i] };
                clusterStart = ordered[i].TimestampUtc;
            }

            candidates.Add(BuildCandidate(cluster));

            return candidates;
        }

        private static McpIncidentCandidate BuildCandidate(List<DiscoverySignal> cluster)
        {
            var headline = cluster
                .OrderByDescending(s => s.Rank)
                .ThenBy(s => s.TimestampUtc)
                .First();

            var others = cluster
                .Where(s => !ReferenceEquals(s, headline))
                .GroupBy(s => s.Text, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Count() + " x " + g.Key)
                .Take(8)
                .ToList();

            var span = cluster.Max(s => s.TimestampUtc) - cluster.Min(s => s.TimestampUtc);

            var detail = cluster.Count == 1
                ? "Single signal."
                : cluster.Count + " signals spanning " +
                  Math.Round(span.TotalMinutes, 1).ToString(CultureInfo.InvariantCulture) + " min: " +
                  string.Join("; ", others);

            return new McpIncidentCandidate
            {
                TimestampUtc = FormatUtc(headline.TimestampUtc),
                TimestampLocal = FormatLocal(ToLocal(headline.TimestampUtc)),
                Signal = McpSecretRedactor.Redact(headline.Text),
                Source = headline.Source,
                Detail = McpSecretRedactor.Redact(Shorten(detail, EventMessageMaxChars)),
                SignalCount = cluster.Count,
            };
        }

        // ── Source selection ─────────────────────────────────────────

        private static HashSet<string> ParseSources(string raw, List<string> warnings)
        {
            var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "sitefinity", "iis", "eventlog", "httperr" };

            if (string.IsNullOrWhiteSpace(raw))
            {
                return all;
            }

            var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = token.Trim();

                if (all.Contains(name))
                {
                    selected.Add(name.ToLowerInvariant());
                }
                else
                {
                    warnings.Add("Unknown source '" + name + "' ignored. Valid values: sitefinity, iis, eventlog, httperr.");
                }
            }

            if (selected.Count == 0)
            {
                warnings.Add("No valid sources were requested; collecting all four.");
                return all;
            }

            return selected;
        }

        // ── Sitefinity logs ──────────────────────────────────────────

        private static McpIncidentSitefinitySection CollectSitefinity(
            DateTime startUtc, DateTime endUtc, string query, ScanBudget budget)
        {
            var section = new McpIncidentSitefinitySection
            {
                LogsPath = SitefinityLogsPath,
                TimestampInterpretation =
                    "server-local (Sitefinity writes local time by default; a site configured to log UTC would be offset by ServerUtcOffsetMinutes)",
            };

            try
            {
                if (string.IsNullOrEmpty(SitefinityLogsPath) || !Directory.Exists(SitefinityLogsPath))
                {
                    section.Warnings.Add("Sitefinity log folder not found at '" + SitefinityLogsPath + "'.");
                    return section;
                }

                section.Available = true;

                // A file last written before the window opened cannot hold an entry inside it.
                var files = new DirectoryInfo(SitefinityLogsPath).EnumerateFiles("*.log")
                    .Where(f => f.LastWriteTimeUtc >= startUtc)
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .ToList();

                // Retention is bounded DURING the scan: two capped buckets plus counters, never a list of
                // every in-window entry. A site mid-incident can write tens of thousands of entries into
                // one window, and holding them all just to throw 99% away is how this endpoint would run
                // the app pool out of memory at exactly the wrong moment.
                var errors = new List<McpIncidentLogEntry>();
                var others = new List<McpIncidentLogEntry>();
                var totalMatched = 0;
                var matchedCount = 0;
                var state = new SitefinityScanState();

                foreach (var file in files)
                {
                    if (budget.Expired || state.StoppedEarly)
                    {
                        break;
                    }

                    section.FilesScanned.Add(file.Name);
                    var fileName = file.Name;

                    try
                    {
                        StreamSitefinityBlocks(file, state, budget, block =>
                        {
                            DateTime localStamp;

                            if (!TryReadSitefinityTimestamp(block, out localStamp))
                            {
                                return;
                            }

                            // The log carries local time; lift it back to UTC at ITS OWN instant so a
                            // window spanning a DST transition stays correct on both sides.
                            var utc = LocalToUtc(localStamp);

                            if (utc < startUtc || utc > endUtc)
                            {
                                return;
                            }

                            totalMatched++;

                            var entry = new McpIncidentLogEntry
                            {
                                TimestampUtc = FormatUtc(utc),
                                TimestampLocal = FormatLocal(localStamp),
                                FileName = fileName,
                                Severity = McpSecretRedactor.Redact(ReadField(block, "Severity:")),
                                Type = McpSecretRedactor.Redact(ReadField(block, "Type:")),
                                Message = McpSecretRedactor.Redact(Shorten(ReadMessage(block), EventMessageMaxChars)),
                                RequestedUrl = McpSecretRedactor.Redact(ReadField(block, "Requested URL:")),
                                StackTraceHead = McpSecretRedactor.Redact(ReadStackHead(block)),
                            };

                            // Query matching runs against the ALREADY-REDACTED entry, so a search can
                            // never confirm the value of something the redactor removed.
                            if (query != null && !Matches(query, entry.FileName, entry.Severity, entry.Type,
                                entry.Message, entry.RequestedUrl, entry.StackTraceHead))
                            {
                                return;
                            }

                            matchedCount++;

                            var bucket = IsErrorSeverity(entry.Severity) ? errors : others;

                            if (bucket.Count < SitefinityEntryCap)
                            {
                                bucket.Add(entry);
                            }
                        });
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        section.Warnings.Add("Access denied reading '" + file.Name + "': " + ex.Message);
                    }
                    catch (IOException)
                    {
                        // Locked by the running site — skip it rather than failing the whole response.
                        section.Warnings.Add("Could not read '" + file.Name + "' (locked by Sitefinity).");
                    }
                }

                if (state.CeilingHit)
                {
                    section.Warnings.Add("Stopped after " +
                        LineScanCeiling.ToString("N0", CultureInfo.InvariantCulture) +
                        " lines of Sitefinity log; narrow the window to cover the whole period.");
                }

                if (state.BudgetHit)
                {
                    section.Warnings.Add(budget.Message);
                }

                section.TotalMatched = totalMatched;
                section.MatchedCount = matchedCount;

                // Errors first, then chronological — the crash usually leads, the noise follows.
                // TimestampUtc is a fixed-width ISO 8601 UTC string, so an ordinal sort of it IS a
                // chronological sort; no parse-back needed.
                var ordered = errors.OrderBy(e => e.TimestampUtc, StringComparer.Ordinal)
                    .Concat(others.OrderBy(e => e.TimestampUtc, StringComparer.Ordinal))
                    .Take(SitefinityEntryCap)
                    .ToList();

                section.Entries = ordered;
                section.ReturnedCount = ordered.Count;
                section.Truncated = matchedCount > ordered.Count || state.StoppedEarly;
            }
            catch (Exception ex)
            {
                section.Warnings.Add("Sitefinity log collection failed: " + ex.Message);
            }

            return section;
        }

        /// <summary>
        /// Streams a Sitefinity log file one line at a time, accumulating the current 40-dash-delimited
        /// entry and handing each completed block to <paramref name="onBlock"/>. Memory stays flat
        /// regardless of file size: only the current block is held, and it stops growing at
        /// <see cref="SitefinityBlockMaxChars"/> — every field this endpoint reads sits at the top of the
        /// block, so a runaway entry is still parsed correctly, just not carried in full.
        /// <para>
        /// Lines are counted against the shared <see cref="LineScanCeiling"/> and the wall-clock budget is
        /// polled every 20,000 lines, both recorded on <paramref name="state"/> so the caller can report
        /// a partial result honestly.
        /// </para>
        /// </summary>
        private static void StreamSitefinityBlocks(
            FileInfo file, SitefinityScanState state, ScanBudget budget, Action<string> onBlock)
        {
            var block = new StringBuilder();
            var overflowed = false;

            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    state.LinesScanned++;

                    if (state.LinesScanned >= LineScanCeiling)
                    {
                        state.CeilingHit = true;
                        return;
                    }

                    if ((state.LinesScanned % 20000) == 0 && budget.Expired)
                    {
                        state.BudgetHit = true;
                        return;
                    }

                    if (IsSeparatorLine(line))
                    {
                        FlushBlock(block, onBlock);
                        overflowed = false;
                        continue;
                    }

                    if (overflowed)
                    {
                        continue;
                    }

                    if (block.Length + line.Length + 1 > SitefinityBlockMaxChars)
                    {
                        overflowed = true;
                        continue;
                    }

                    block.Append(line).Append('\n');
                }
            }

            FlushBlock(block, onBlock);
        }

        private static void FlushBlock(StringBuilder block, Action<string> onBlock)
        {
            if (block.Length > 0)
            {
                var text = block.ToString().Trim();

                if (text.Length > 0)
                {
                    onBlock(text);
                }
            }

            block.Length = 0;
        }

        /// <summary>
        /// True for the 40-dash line Sitefinity writes between entries. Matched structurally (a line of
        /// nothing but dashes, at least 40 of them) rather than by exact string, so a slightly different
        /// rule width in an older log still splits correctly.
        /// </summary>
        private static bool IsSeparatorLine(string line)
        {
            var trimmed = line.Trim();

            if (trimmed.Length < SitefinitySeparator.Length)
            {
                return false;
            }

            foreach (var c in trimmed)
            {
                if (c != '-')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadSitefinityTimestamp(string block, out DateTime value)
        {
            var raw = ReadField(block, "Timestamp:");

            if (string.IsNullOrEmpty(raw))
            {
                value = default(DateTime);
                return false;
            }

            DateTime parsed;

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                value = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
                return true;
            }

            if (DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed))
            {
                value = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
                return true;
            }

            value = default(DateTime);
            return false;
        }

        private static string ReadField(string block, string label)
        {
            foreach (var line in block.Split('\n'))
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(label.Length).Trim();
                }
            }

            return string.Empty;
        }

        private static string ReadMessage(string block)
        {
            var explicitMessage = ReadField(block, "Message:");

            if (!string.IsNullOrEmpty(explicitMessage))
            {
                return explicitMessage;
            }

            foreach (var line in block.Split('\n'))
            {
                var trimmed = line.Trim();

                if (trimmed.Length == 0 || LooksLikeFieldLine(trimmed))
                {
                    continue;
                }

                return trimmed;
            }

            return string.Empty;
        }

        private static bool LooksLikeFieldLine(string line)
        {
            var labels = new[]
            {
                "Timestamp:", "Severity:", "Type:", "Activity Id:", "Requested URL:",
                "Machine Name:", "Category:", "Priority:", "EventId:", "Title:",
            };

            foreach (var label in labels)
            {
                if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadStackHead(string block)
        {
            var frames = new List<string>();

            foreach (var line in block.Split('\n'))
            {
                var trimmed = line.Trim();

                if (trimmed.StartsWith("at ", StringComparison.Ordinal))
                {
                    frames.Add(trimmed);

                    if (frames.Count >= 5)
                    {
                        break;
                    }
                }
            }

            return frames.Count == 0 ? null : string.Join("\n", frames);
        }

        private static bool IsErrorSeverity(string severity)
        {
            if (string.IsNullOrEmpty(severity))
            {
                return false;
            }

            return severity.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0
                || severity.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) >= 0
                || severity.IndexOf("Fatal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ── IIS W3C access log ───────────────────────────────────────

        /// <summary>
        /// Collects the IIS access log for the range. <paramref name="bucketByHour"/> selects the rate
        /// series: hourly for search mode (lookbacks up to 14 days), per-minute for window mode (at most
        /// 240 rows). Exactly one of the two lists on the section is ever populated.
        /// </summary>
        private static McpIncidentIisSection CollectIis(
            DateTime startUtc, DateTime endUtc, string query, ScanBudget budget, bool bucketByHour)
        {
            var section = new McpIncidentIisSection
            {
                TimestampInterpretation = "UTC (W3C log timestamps are always UTC, regardless of the log-rollover setting)",
            };

            try
            {
                var siteId = ResolveSiteId();
                section.SiteId = siteId;

                var folder = ResolveIisLogFolder(siteId, section.Warnings);
                section.LogFolder = folder;

                if (string.IsNullOrEmpty(folder))
                {
                    return section;
                }

                if (!Directory.Exists(folder))
                {
                    section.Warnings.Add("IIS log folder '" + folder + "' does not exist. If W3C file logging is " +
                        "enabled at a different location, set 'IIS Log Path' in Sitefinity Admin > Advanced > McpSettings. " +
                        "If logging is ETW-only or disabled, no access log exists to read.");
                    return section;
                }

                var files = SelectIisFiles(folder, startUtc, endUtc, section.Warnings);

                if (files.Count == 0)
                {
                    section.Warnings.Add("No IIS log file covering the window was found in '" + folder + "'.");
                    return section;
                }

                section.Available = true;

                var minuteCounts = new Dictionary<DateTime, int>();
                var statusCounts = new Dictionary<string, int>();
                var serverErrors = new List<McpIisRequestEntry>();
                var slowest = new List<McpIisRequestEntry>();
                var matched = new List<McpIisRequestEntry>();

                foreach (var file in files)
                {
                    if (section.Truncated)
                    {
                        break;
                    }

                    if (budget.Expired)
                    {
                        section.Warnings.Add(budget.Message);
                        break;
                    }

                    section.FilesScanned.Add(file.Name);

                    try
                    {
                        ScanIisFile(file, startUtc, endUtc, query, section, budget, bucketByHour,
                            minuteCounts, statusCounts, serverErrors, slowest, matched);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        section.Warnings.Add(BuildIisAccessDeniedWarning(folder));
                    }
                    catch (IOException ex)
                    {
                        section.Warnings.Add("Could not read IIS log '" + file.Name + "': " + ex.Message);
                    }
                }

                var series = minuteCounts
                    .OrderBy(kvp => kvp.Key)
                    .Select(kvp => new McpIncidentMinuteCount
                    {
                        MinuteUtc = kvp.Key.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        MinuteLocal = ToLocal(kvp.Key).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                        Count = kvp.Value,
                    })
                    .ToList();

                // Exactly one of the two lists carries the series; the other stays empty so a reader can
                // tell at a glance which resolution it is looking at.
                if (bucketByHour)
                {
                    section.RequestsPerHour = series;
                }
                else
                {
                    section.RequestsPerMinute = series;
                }

                section.StatusHistogram = statusCounts
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                    .Select(kvp => new McpIncidentCount { Key = kvp.Key, Count = kvp.Value })
                    .ToList();

                section.ReturnedServerErrors = serverErrors.Count;
                section.ServerErrors = serverErrors;
                section.ServerErrorsTruncated = section.TotalServerErrors > serverErrors.Count;

                section.SlowestRequests = slowest
                    .OrderByDescending(e => e.TimeTakenMs)
                    .ToList();

                section.MatchedRequests = matched;
                section.MatchedRequestsTruncated = section.MatchedCount > matched.Count;
            }
            catch (UnauthorizedAccessException)
            {
                section.Warnings.Add(BuildIisAccessDeniedWarning(section.LogFolder));
            }
            catch (Exception ex)
            {
                section.Warnings.Add("IIS log collection failed: " + ex.Message);
            }

            return section;
        }

        private static void ScanIisFile(
            FileInfo file,
            DateTime startUtc,
            DateTime endUtc,
            string query,
            McpIncidentIisSection section,
            ScanBudget budget,
            bool bucketByHour,
            Dictionary<DateTime, int> minuteCounts,
            Dictionary<string, int> statusCounts,
            List<McpIisRequestEntry> serverErrors,
            List<McpIisRequestEntry> slowest,
            List<McpIisRequestEntry> matched)
        {
            Dictionary<string, int> fields = null;

            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    section.LinesScanned++;

                    if (section.LinesScanned >= LineScanCeiling)
                    {
                        section.Truncated = true;
                        section.Warnings.Add("Stopped after " + LineScanCeiling.ToString("N0", CultureInfo.InvariantCulture) +
                            " lines; narrow the window to cover the whole period.");
                        return;
                    }

                    if ((section.LinesScanned % 20000) == 0 && budget.Expired)
                    {
                        section.Truncated = true;
                        section.Warnings.Add(budget.Message);
                        return;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line[0] == '#')
                    {
                        // #Fields can reappear mid-file after an IIS restart or a config change, so the
                        // column map is rebuilt every time the directive is seen rather than once per file.
                        if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
                        {
                            fields = BuildFieldMap(line.Substring("#Fields:".Length));
                        }

                        continue;
                    }

                    if (fields == null)
                    {
                        section.MalformedLines++;
                        continue;
                    }

                    var parts = line.Split(' ');

                    DateTime stampUtc;

                    if (!TryReadW3CTimestamp(parts, fields, out stampUtc))
                    {
                        section.MalformedLines++;
                        continue;
                    }

                    if (stampUtc < startUtc || stampUtc > endUtc)
                    {
                        continue;
                    }

                    section.TotalRequests++;

                    Increment(minuteCounts, bucketByHour ? TruncateToHour(stampUtc) : TruncateToMinutes(stampUtc, 1));

                    var status = ReadInt(parts, fields, "sc-status", 0);
                    var subStatus = ReadInt(parts, fields, "sc-substatus", 0);
                    Increment(statusCounts, status.ToString(CultureInfo.InvariantCulture) + "." +
                        subStatus.ToString(CultureInfo.InvariantCulture));

                    var timeTaken = ReadInt(parts, fields, "time-taken", 0);
                    var isServerError = status >= 500;
                    var isSlowCandidate = slowest.Count < IisSlowestCap
                        || timeTaken > slowest[slowest.Count - 1].TimeTakenMs;

                    // With a query every in-window line has to be materialised so it can be redacted and
                    // then tested; without one, only the lines that can end up in a list are built.
                    if (query == null && !isServerError && !isSlowCandidate)
                    {
                        continue;
                    }

                    var entry = BuildIisEntry(parts, fields, stampUtc, status, subStatus, timeTaken);

                    if (query != null)
                    {
                        if (Matches(query, entry.UserName, entry.UriStem, entry.UriQuery, entry.ClientIp, entry.Referer))
                        {
                            section.MatchedCount++;

                            if (matched.Count < IisMatchedCap)
                            {
                                matched.Add(entry);
                            }
                        }

                        if (!isServerError && !isSlowCandidate)
                        {
                            continue;
                        }
                    }

                    if (isServerError)
                    {
                        section.TotalServerErrors++;

                        if (serverErrors.Count < IisServerErrorCap)
                        {
                            serverErrors.Add(entry);
                        }
                    }

                    if (isSlowCandidate)
                    {
                        slowest.Add(entry);
                        slowest.Sort((a, b) => b.TimeTakenMs.CompareTo(a.TimeTakenMs));

                        while (slowest.Count > IisSlowestCap)
                        {
                            slowest.RemoveAt(slowest.Count - 1);
                        }
                    }
                }
            }
        }

        private static McpIisRequestEntry BuildIisEntry(
            string[] parts,
            Dictionary<string, int> fields,
            DateTime stampUtc,
            int status,
            int subStatus,
            int timeTaken)
        {
            // cs(Cookie) and cs(Authorization) are deliberately never read — a redacted credential is
            // still a credential-shaped string in the LLM context. cs(Referer) IS read: the Query filter
            // matches on it, so omitting it would make a legitimate hit look like a false positive.
            return new McpIisRequestEntry
            {
                TimestampUtc = FormatUtc(stampUtc),
                TimestampLocal = FormatLocal(ToLocal(stampUtc)),
                Method = ReadValue(parts, fields, "cs-method"),
                UriStem = McpSecretRedactor.Redact(ReadValue(parts, fields, "cs-uri-stem")),
                UriQuery = RedactQueryString(ReadValue(parts, fields, "cs-uri-query")),
                Status = status,
                SubStatus = subStatus,
                Win32Status = ReadLong(parts, fields, "sc-win32-status", 0L),
                TimeTakenMs = timeTaken,
                UserName = McpSecretRedactor.Redact(ReadValue(parts, fields, "cs-username")),
                ClientIp = McpSecretRedactor.Redact(ReadValue(parts, fields, "c-ip")),
                Referer = RedactUriWithQuery(ReadValue(parts, fields, "cs(Referer)")),
            };
        }

        private static List<FileInfo> SelectIisFiles(string folder, DateTime startUtc, DateTime endUtc, List<string> warnings)
        {
            var dir = new DirectoryInfo(folder);
            var byDate = new List<FileInfo>();

            // A window straddling UTC midnight touches two daily files.
            for (var day = startUtc.Date; day <= endUtc.Date; day = day.AddDays(1))
            {
                var pattern = "u_ex" + day.ToString("yyMMdd", CultureInfo.InvariantCulture) + "*.log";

                foreach (var file in dir.GetFiles(pattern))
                {
                    if (!byDate.Any(f => string.Equals(f.FullName, file.FullName, StringComparison.OrdinalIgnoreCase)))
                    {
                        byDate.Add(file);
                    }
                }
            }

            if (byDate.Count > 0)
            {
                return byDate.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }

            // Non-default naming (hourly rollover, custom prefix, local-time rollover). Fall back to any
            // log still being written at or after the window opened. EnumerateFiles streams the directory
            // rather than materialising every FileInfo in a folder that may hold years of rolled logs.
            var fallback = dir.EnumerateFiles("*.log")
                .Where(f => f.LastWriteTimeUtc >= startUtc)
                .OrderBy(f => f.LastWriteTimeUtc)
                .Take(4)
                .ToList();

            if (fallback.Count > 0)
            {
                warnings.Add("No u_ex-named log matched the window's date; fell back to " + fallback.Count +
                    " recently-written file(s) in the folder.");
            }

            return fallback;
        }

        private static int ResolveSiteId()
        {
            try
            {
                // Typically "/LM/W3SVC/2/ROOT", but a virtual application appends its path.
                var applicationId = HostingEnvironment.ApplicationID ?? string.Empty;
                var segments = applicationId.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                for (var i = 0; i < segments.Length - 1; i++)
                {
                    if (string.Equals(segments[i], "W3SVC", StringComparison.OrdinalIgnoreCase))
                    {
                        int id;

                        if (int.TryParse(segments[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                        {
                            return id;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to 0 — the caller reports it and suggests the config override.
            }

            return 0;
        }

        private static string ResolveIisLogFolder(int siteId, List<string> warnings)
        {
            try
            {
                var config = Config.Get<McpConfig>();

                if (config != null && !string.IsNullOrWhiteSpace(config.IisLogPath))
                {
                    return config.IisLogPath.Trim();
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read the IIS Log Path setting: " + ex.Message);
            }

            if (siteId <= 0)
            {
                warnings.Add("Could not determine the IIS site id from HostingEnvironment.ApplicationID ('" +
                    (HostingEnvironment.ApplicationID ?? "<null>") + "'). Set 'IIS Log Path' in " +
                    "Sitefinity Admin > Advanced > McpSettings to point at this site's W3SVC log folder.");
                return null;
            }

            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive");

            if (string.IsNullOrEmpty(systemDrive))
            {
                systemDrive = "C:";
            }

            return Path.Combine(systemDrive + Path.DirectorySeparatorChar,
                Path.Combine("inetpub", Path.Combine("logs", Path.Combine("LogFiles",
                    "W3SVC" + siteId.ToString(CultureInfo.InvariantCulture)))));
        }

        private static string BuildIisAccessDeniedWarning(string folder)
        {
            return "Access denied reading the IIS log folder '" + (folder ?? "<unknown>") + "'. " +
                "Grant the app pool identity read access, e.g. run as administrator: " +
                "icacls \"" + (folder ?? "C:\\inetpub\\logs\\LogFiles") + "\" /grant \"IIS APPPOOL\\" +
                AppPoolName() + ":(OI)(CI)R\"";
        }

        private static string BuildHttpErrAccessWarning(string folder)
        {
            return "Access denied reading '" + folder + "'. HTTPERR is normally readable only by " +
                "administrators; grant the app pool identity read access to correlate http.sys 503s, e.g. " +
                "icacls \"" + folder + "\" /grant \"IIS APPPOOL\\" + AppPoolName() + ":(OI)(CI)R\"";
        }

        private static string BuildEventLogAccessWarning(string logName, string detail)
        {
            var suffix = string.IsNullOrEmpty(detail) ? string.Empty : " (" + detail + ")";

            return "Access denied reading the '" + logName + "' event log" + suffix + ". Add the app pool " +
                "identity (\"IIS APPPOOL\\" + AppPoolName() + "\") to the local \"Event Log Readers\" group, " +
                "then recycle the pool.";
        }

        private static string AppPoolName()
        {
            var pool = Environment.GetEnvironmentVariable("APP_POOL_ID");

            return string.IsNullOrEmpty(pool) ? "<your app pool name>" : pool;
        }

        // ── HTTPERR (http.sys) ───────────────────────────────────────

        private static string HttpErrFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Path.Combine("LogFiles", "HTTPERR"));
        }

        private static McpIncidentHttpErrSection CollectHttpErr(
            DateTime startUtc, DateTime endUtc, string query, ScanBudget budget)
        {
            var folder = HttpErrFolder();

            var section = new McpIncidentHttpErrSection
            {
                TimestampInterpretation = "UTC (http.sys writes W3C-style UTC timestamps)",
                LogFolder = folder,
            };

            try
            {
                if (!Directory.Exists(folder))
                {
                    section.Warnings.Add("HTTPERR folder '" + folder + "' not found. http.sys error logging may be disabled.");
                    return section;
                }

                var qualifying = new DirectoryInfo(folder).EnumerateFiles("httperr*.log")
                    .Where(f => f.LastWriteTimeUtc >= startUtc)
                    .ToList();

                // Take the NEWEST qualifying files, then read them oldest-first. http.sys rolls HTTPERR
                // fast during a 503 storm — exactly the event being investigated — so picking the oldest
                // few would throw away the end of the window, which is where the crash actually is.
                var files = qualifying
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(HttpErrFileCap)
                    .OrderBy(f => f.LastWriteTimeUtc)
                    .ToList();

                if (qualifying.Count > HttpErrFileCap)
                {
                    section.Warnings.Add(qualifying.Count + " HTTPERR files overlap the window; scanned the " +
                        HttpErrFileCap + " most recent. Narrow the window if you need the earlier ones.");
                }

                if (files.Count == 0)
                {
                    section.Warnings.Add("No HTTPERR log file was written at or after the window opened — " +
                        "http.sys recorded nothing in this period (which is itself a useful signal).");
                    section.Available = true;
                    return section;
                }

                section.Available = true;

                var reasons = new Dictionary<string, int>();

                foreach (var file in files)
                {
                    if (section.Truncated)
                    {
                        break;
                    }

                    if (budget.Expired)
                    {
                        section.Warnings.Add(budget.Message);
                        break;
                    }

                    section.FilesScanned.Add(file.Name);

                    try
                    {
                        ScanHttpErrFile(file, startUtc, endUtc, query, section, budget, reasons);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        section.Warnings.Add(BuildHttpErrAccessWarning(folder));
                    }
                    catch (IOException ex)
                    {
                        section.Warnings.Add("Could not read HTTPERR log '" + file.Name + "': " + ex.Message);
                    }
                }

                section.ReasonHistogram = reasons
                    .OrderByDescending(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key, StringComparer.Ordinal)
                    .Select(kvp => new McpIncidentCount { Key = kvp.Key, Count = kvp.Value })
                    .ToList();

                section.ReturnedCount = section.Entries.Count;
                section.Truncated = section.Truncated || section.MatchedCount > section.Entries.Count;
            }
            catch (UnauthorizedAccessException)
            {
                section.Warnings.Add(BuildHttpErrAccessWarning(folder));
            }
            catch (Exception ex)
            {
                section.Warnings.Add("HTTPERR collection failed: " + ex.Message);
            }

            return section;
        }

        private static void ScanHttpErrFile(
            FileInfo file,
            DateTime startUtc,
            DateTime endUtc,
            string query,
            McpIncidentHttpErrSection section,
            ScanBudget budget,
            Dictionary<string, int> reasons)
        {
            Dictionary<string, int> fields = null;

            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    section.LinesScanned++;

                    if (section.LinesScanned >= LineScanCeiling)
                    {
                        section.Truncated = true;
                        return;
                    }

                    if ((section.LinesScanned % 20000) == 0 && budget.Expired)
                    {
                        section.Truncated = true;
                        section.Warnings.Add(budget.Message);
                        return;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    if (line[0] == '#')
                    {
                        if (line.StartsWith("#Fields:", StringComparison.OrdinalIgnoreCase))
                        {
                            fields = BuildFieldMap(line.Substring("#Fields:".Length));
                        }

                        continue;
                    }

                    if (fields == null)
                    {
                        section.MalformedLines++;
                        continue;
                    }

                    var parts = line.Split(' ');

                    DateTime stampUtc;

                    if (!TryReadW3CTimestamp(parts, fields, out stampUtc))
                    {
                        section.MalformedLines++;
                        continue;
                    }

                    if (stampUtc < startUtc || stampUtc > endUtc)
                    {
                        continue;
                    }

                    section.TotalMatched++;

                    var reason = ReadValue(parts, fields, "s-reason");

                    if (string.IsNullOrEmpty(reason))
                    {
                        reason = "(none)";
                    }

                    Increment(reasons, reason);

                    var entry = new McpHttpErrEntry
                    {
                        TimestampUtc = FormatUtc(stampUtc),
                        TimestampLocal = FormatLocal(ToLocal(stampUtc)),
                        ClientIp = McpSecretRedactor.Redact(ReadValue(parts, fields, "c-ip")),
                        Method = ReadValue(parts, fields, "cs-method"),
                        Uri = RedactUriWithQuery(ReadValue(parts, fields, "cs-uri")),
                        Status = ReadInt(parts, fields, "sc-status", 0),
                        Reason = McpSecretRedactor.Redact(reason),
                        QueueName = McpSecretRedactor.Redact(ReadValue(parts, fields, "s-queuename")),
                    };

                    // The line is matched through its redacted reconstruction, never the raw text.
                    if (query != null && !Matches(query,
                        entry.ClientIp, entry.Method, entry.Uri, entry.Reason, entry.QueueName))
                    {
                        continue;
                    }

                    section.MatchedCount++;

                    if (section.Entries.Count >= HttpErrEntryCap)
                    {
                        continue;
                    }

                    section.Entries.Add(entry);
                }
            }
        }

        // ── Windows Event Log ────────────────────────────────────────

        private static McpIncidentEventLogSection CollectEventLog(
            DateTime startUtc, DateTime endUtc, string query, ScanBudget budget)
        {
            var section = new McpIncidentEventLogSection
            {
                TimestampInterpretation = "UTC (Event Log records are stored in UTC)",
            };

            // Security is deliberately excluded: an app pool identity cannot read it, and nothing an
            // outage investigation needs lives there.
            section.Channels.Add(ReadEventChannel("Application", startUtc, endUtc, false, query, budget));
            section.Channels.Add(ReadEventChannel("System", startUtc, endUtc, true, query, budget));

            section.Available = section.Channels.Any(c => c.Available);

            return section;
        }

        private static McpEventLogChannel ReadEventChannel(
            string logName,
            DateTime startUtc,
            DateTime endUtc,
            bool filterProviders,
            string query,
            ScanBudget budget)
        {
            var channel = new McpEventLogChannel { LogName = logName };

            try
            {
                var eventQuery = new EventLogQuery(logName, PathType.LogName, BuildEventXPath(startUtc, endUtc))
                {
                    ReverseDirection = false,
                };

                using (var reader = new EventLogReader(eventQuery))
                {
                    channel.Available = true;

                    EventRecord record;

                    while ((record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            channel.TotalMatched++;

                            if ((channel.TotalMatched % 200) == 0 && budget.Expired)
                            {
                                channel.Truncated = true;
                                channel.Warnings.Add(budget.Message);
                                break;
                            }

                            var provider = record.ProviderName ?? string.Empty;
                            var level = record.Level.HasValue ? (int)record.Level.Value : 0;

                            // System is noisy; keep only the providers that explain an app-pool outage,
                            // plus anything that is an outright Error or Critical.
                            if (filterProviders && level > 2 && !IsAllowedSystemProvider(provider))
                            {
                                continue;
                            }

                            var stampUtc = record.TimeCreated.HasValue
                                ? record.TimeCreated.Value.ToUniversalTime()
                                : startUtc;

                            var entry = new McpEventLogEntry
                            {
                                TimestampUtc = FormatUtc(stampUtc),
                                TimestampLocal = FormatLocal(ToLocal(stampUtc)),
                                LogName = logName,
                                EventId = record.Id,
                                Level = DescribeLevel(level),
                                ProviderName = McpSecretRedactor.Redact(provider),
                                Message = McpSecretRedactor.Redact(Shorten(DescribeEvent(record), EventMessageMaxChars)),
                            };

                            // Provider + rendered message, both already redacted.
                            if (query != null && !Matches(query, entry.ProviderName, entry.Message))
                            {
                                continue;
                            }

                            channel.MatchedCount++;

                            if (channel.Entries.Count >= EventLogEntryCap)
                            {
                                channel.Truncated = true;
                                continue;
                            }

                            channel.Entries.Add(entry);
                        }
                    }
                }

                channel.ReturnedCount = channel.Entries.Count;
            }
            catch (UnauthorizedAccessException)
            {
                channel.Warnings.Add(BuildEventLogAccessWarning(logName, null));
            }
            catch (EventLogNotFoundException)
            {
                channel.Warnings.Add("Event log channel '" + logName + "' was not found on this machine.");
            }
            catch (EventLogException ex)
            {
                // The reader raises this for denied access too, depending on the failure path.
                var message = ex.Message ?? string.Empty;

                channel.Warnings.Add(message.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
                    ? BuildEventLogAccessWarning(logName, message)
                    : "Could not read the '" + logName + "' event log: " + message);
            }
            catch (Exception ex)
            {
                channel.Warnings.Add("Unexpected failure reading the '" + logName + "' event log: " + ex.Message);
            }

            return channel;
        }

        /// <summary>
        /// Builds the structured XPath filter. Levels 1-3 are Critical/Error/Warning; the bounds are UTC
        /// ISO 8601 with a trailing Z, which is the only form the Event Log query engine accepts.
        /// </summary>
        private static string BuildEventXPath(DateTime startUtc, DateTime endUtc)
        {
            return "*[System[(Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime>='" +
                FormatEventTime(startUtc) + "' and @SystemTime<='" + FormatEventTime(endUtc) + "']]]";
        }

        private static string FormatEventTime(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static bool IsAllowedSystemProvider(string provider)
        {
            foreach (var allowed in SystemProviderAllowList)
            {
                if (string.Equals(provider, allowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Renders an event's description. <c>FormatDescription()</c> returns null when the publisher's
        /// message resource is missing and throws when the publisher metadata cannot be loaded at all, so
        /// both paths fall back to a summary of the raw event properties.
        /// </summary>
        private static string DescribeEvent(EventRecord record)
        {
            try
            {
                var described = record.FormatDescription();

                if (!string.IsNullOrWhiteSpace(described))
                {
                    return described.Replace("\r\n", " ").Replace('\n', ' ').Trim();
                }
            }
            catch (EventLogException)
            {
                // Publisher metadata unavailable — fall through to the property dump.
            }
            catch (Exception)
            {
                // Never let a rendering failure lose the whole event.
            }

            try
            {
                // Each value is clipped BEFORE joining — an event property can carry an entire embedded
                // payload, and the outer Shorten would only trim the result after the big string existed.
                var values = record.Properties
                    .Select(p => p.Value == null ? string.Empty : p.Value.ToString())
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => Shorten(v, EventPropertyMaxChars))
                    .ToList();

                return values.Count == 0
                    ? "(no description available)"
                    : "(unrendered) " + string.Join(" | ", values);
            }
            catch (Exception)
            {
                return "(no description available)";
            }
        }

        private static string DescribeLevel(int level)
        {
            switch (level)
            {
                case 1: return "Critical";
                case 2: return "Error";
                case 3: return "Warning";
                case 4: return "Information";
                case 5: return "Verbose";
                default: return "Level" + level.ToString(CultureInfo.InvariantCulture);
            }
        }

        // ── W3C parsing helpers ──────────────────────────────────────

        /// <summary>
        /// Maps each field name in a <c>#Fields:</c> directive to its column index. Names are kept
        /// verbatim (e.g. <c>cs-uri-stem</c>, <c>sc-win32-status</c>) and matched case-insensitively.
        /// </summary>
        private static Dictionary<string, int> BuildFieldMap(string fieldList)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var names = fieldList.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < names.Length; i++)
            {
                map[names[i]] = i;
            }

            return map;
        }

        private static bool TryReadW3CTimestamp(string[] parts, Dictionary<string, int> fields, out DateTime utc)
        {
            utc = default(DateTime);

            var date = ReadValue(parts, fields, "date");
            var time = ReadValue(parts, fields, "time");

            if (string.IsNullOrEmpty(date) || string.IsNullOrEmpty(time))
            {
                return false;
            }

            DateTime parsed;

            if (!DateTime.TryParseExact(date + " " + time, new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff" },
                CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
            {
                return false;
            }

            utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        /// <summary>Reads one column, translating the W3C null marker <c>-</c> into an empty string.</summary>
        private static string ReadValue(string[] parts, Dictionary<string, int> fields, string name)
        {
            int index;

            if (!fields.TryGetValue(name, out index) || index >= parts.Length)
            {
                return string.Empty;
            }

            var value = parts[index];

            return value == "-" ? string.Empty : value;
        }

        private static int ReadInt(string[] parts, Dictionary<string, int> fields, string name, int fallback)
        {
            int value;

            return int.TryParse(ReadValue(parts, fields, name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static long ReadLong(string[] parts, Dictionary<string, int> fields, string name, long fallback)
        {
            long value;

            return long.TryParse(ReadValue(parts, fields, name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        // ── Redaction helpers ────────────────────────────────────────

        /// <summary>
        /// Redacts a raw query string. Each <c>name=value</c> pair whose name hits the redactor's
        /// deny-list loses its value outright; whatever survives is then pattern-scanned, so a token
        /// sitting under an innocuous parameter name is still caught.
        /// </summary>
        private static string RedactQueryString(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                return string.Empty;
            }

            var pairs = query.Split('&');
            var rebuilt = new List<string>(pairs.Length);

            foreach (var pair in pairs)
            {
                var eq = pair.IndexOf('=');

                if (eq <= 0)
                {
                    rebuilt.Add(pair);
                    continue;
                }

                var name = pair.Substring(0, eq);

                rebuilt.Add(McpSecretRedactor.IsDeniedKey(name)
                    ? name + "=" + McpSecretRedactor.Placeholder
                    : pair);
            }

            return McpSecretRedactor.Redact(string.Join("&", rebuilt));
        }

        /// <summary>
        /// Case-insensitive plain-substring test across a set of fields. Every caller passes values that
        /// have ALREADY been through <see cref="McpSecretRedactor"/> — matching after redaction is what
        /// stops the filter being used as an oracle to confirm a secret the redactor removed.
        /// </summary>
        private static bool Matches(string query, params string[] fields)
        {
            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            foreach (var field in fields)
            {
                if (!string.IsNullOrEmpty(field)
                    && field.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Splits a full URI at its <c>?</c> and applies query-parameter redaction to the tail.</summary>
        private static string RedactUriWithQuery(string uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return string.Empty;
            }

            var mark = uri.IndexOf('?');

            if (mark < 0)
            {
                return McpSecretRedactor.Redact(uri);
            }

            return McpSecretRedactor.Redact(uri.Substring(0, mark)) + "?" +
                RedactQueryString(uri.Substring(mark + 1));
        }

        // ── Small shared helpers ─────────────────────────────────────

        /// <summary>
        /// Formats a UTC instant for the wire as <c>yyyy-MM-ddTHH:mm:ssZ</c>.
        /// <para>
        /// Timestamps cross the wire as STRINGS, not <c>DateTime</c>, and that is deliberate. ServiceStack
        /// serializes a DateTime property as <c>/Date(epoch)/</c> — an instant — which turns a carefully
        /// built local wall time back into a point on the timeline that the far side then re-renders in
        /// whatever zone it likes, reporting local times as UTC. Preformatting removes the round trip.
        /// </para>
        /// </summary>
        private static string FormatUtc(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats a server-local wall time as <c>yyyy-MM-ddTHH:mm:ss</c> — deliberately with NO zone
        /// suffix, because it is a wall-clock reading, not an instant. The response header's
        /// <c>ServerTimeZoneId</c> and <c>ServerUtcOffsetMinutes</c> say which zone that clock is in.
        /// </summary>
        private static string FormatLocal(DateTime local)
        {
            return local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Converts a UTC instant to server-local time through <see cref="TimeZoneInfo.Local"/> AT THAT
        /// INSTANT, rather than applying the window-centre offset to everything. A window that straddles a
        /// DST transition would otherwise report one side of it an hour out.
        /// </summary>
        private static DateTime ToLocal(DateTime utc)
        {
            var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            return DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZoneInfo.Local), DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Lifts a server-local timestamp (as Sitefinity writes them) back to UTC using the offset in
        /// force at that local time. During the ambiguous hour of a fall-back transition
        /// <c>GetUtcOffset</c> resolves to standard time — one of the two valid readings, chosen without
        /// throwing, which is the right trade for a log timestamp that carries no offset of its own.
        /// </summary>
        private static DateTime LocalToUtc(DateTime local)
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

            return DateTime.SpecifyKind(
                unspecified - TimeZoneInfo.Local.GetUtcOffset(unspecified), DateTimeKind.Utc);
        }

        private static DateTime TruncateToMinutes(DateTime utc, int minutes)
        {
            var minute = minutes <= 1 ? utc.Minute : (utc.Minute / minutes) * minutes;

            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, DateTimeKind.Utc);
        }

        private static DateTime TruncateToHour(DateTime utc)
        {
            return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
        }

        private static string Shorten(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max) + "… [truncated]";
        }

        private static void Increment<TKey>(Dictionary<TKey, int> counts, TKey key)
        {
            int existing;

            counts[key] = counts.TryGetValue(key, out existing) ? existing + 1 : 1;
        }

        /// <summary>
        /// Wall-clock guard for the whole collection pass. Checked between sources, between files, and
        /// periodically inside the line and event loops. There is deliberately no background job here —
        /// the endpoint stays a plain synchronous request, and this budget plus the fixed caps are the
        /// only protection it needs.
        /// </summary>
        private sealed class ScanBudget
        {
            private readonly Stopwatch _stopwatch;
            private readonly int _seconds;

            public ScanBudget(int seconds)
            {
                this._seconds = seconds;
                this._stopwatch = Stopwatch.StartNew();
            }

            public bool Expired
            {
                get { return this._stopwatch.Elapsed.TotalSeconds >= this._seconds; }
            }

            public string Message
            {
                get
                {
                    return "Time budget (" + this._seconds + "s) exceeded — results are partial; " +
                        "narrow the window or sources.";
                }
            }
        }

        /// <summary>
        /// Shared line budget for a Sitefinity log sweep. The counter spans every file in the pass, so a
        /// folder of rolled logs cannot dodge the ceiling by being split across many small files.
        /// </summary>
        private sealed class SitefinityScanState
        {
            public long LinesScanned { get; set; }

            /// <summary>Set when the shared line ceiling stopped the scan.</summary>
            public bool CeilingHit { get; set; }

            /// <summary>Set when the wall-clock budget stopped the scan.</summary>
            public bool BudgetHit { get; set; }

            public bool StoppedEarly
            {
                get { return this.CeilingHit || this.BudgetHit; }
            }
        }

        /// <summary>One raw crash-shaped signal found during discovery, before clustering.</summary>
        private sealed class DiscoverySignal
        {
            public DiscoverySignal(DateTime timestampUtc, string source, int rank, string text)
            {
                this.TimestampUtc = timestampUtc;
                this.Source = source;
                this.Rank = rank;
                this.Text = text;
            }

            public DateTime TimestampUtc { get; private set; }
            public string Source { get; private set; }

            /// <summary>Higher wins the headline slot when signals cluster together.</summary>
            public int Rank { get; private set; }

            public string Text { get; private set; }
        }
    }
}

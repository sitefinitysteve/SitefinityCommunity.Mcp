namespace SitefinityCommunity.Mcp.Models;

// Timestamps in this file are STRINGS, not DateTime, and that is deliberate — see the matching note in
// the plugin's McpSystemLogService.FormatUtc. ServiceStack serializes a DateTime DTO property as
// /Date(ms)/, an instant, which turns a server-local wall time into a point on the timeline that this
// side then re-renders as UTC. The plugin pre-formats both forms; these properties carry them verbatim.

/// <summary>
/// Envelope for the three shapes the incident endpoint can return. Exactly one of
/// <see cref="Window"/> / <see cref="Candidates"/> / <see cref="Search"/> is populated, per
/// <see cref="Mode"/>.
/// </summary>
public sealed class IncidentResponse
{
    /// <summary>
    /// "window" when a time was supplied, "search" when only a query was, "candidates" when neither
    /// was (discovery of when things broke).
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Populated when <see cref="Mode"/> is "window".</summary>
    public IncidentWindowResponse? Window { get; set; }

    /// <summary>Populated when <see cref="Mode"/> is "candidates".</summary>
    public IncidentCandidatesResponse? Candidates { get; set; }

    /// <summary>Populated when <see cref="Mode"/> is "search".</summary>
    public IncidentSearchResponse? Search { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// Discovery-mode result: candidate incident moments found over the lookback period, newest first.
/// Feed one of these timestamps back as the tool's <c>time</c> to reconstruct the full window.
/// </summary>
public sealed class IncidentCandidatesResponse
{
    public string ServerTimeZoneId { get; set; } = string.Empty;
    public int ServerUtcOffsetMinutes { get; set; }

    public int LookbackHours { get; set; }
    /// <summary>ISO 8601 UTC.</summary>
    public string LookbackStartUtc { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC.</summary>
    public string LookbackEndUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix.</summary>
    public string LookbackStartLocal { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix.</summary>
    public string LookbackEndLocal { get; set; } = string.Empty;

    /// <summary>Which cheap sources were actually scanned. The IIS access log is never one of them.</summary>
    public List<string> ScannedSources { get; set; } = [];

    /// <summary>Individual signals found before clustering.</summary>
    public int TotalSignals { get; set; }

    /// <summary>Clusters found before the cap.</summary>
    public int TotalCandidates { get; set; }
    public int ReturnedCount { get; set; }
    public bool Truncated { get; set; }

    public List<IncidentCandidate> Candidates { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>One clustered candidate incident moment.</summary>
public sealed class IncidentCandidate
{
    /// <summary>ISO 8601 UTC, e.g. "2026-08-27T15:01:12Z".</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix, e.g. "2026-08-27T11:01:12".</summary>
    public string TimestampLocal { get; set; } = string.Empty;

    /// <summary>Headline signal, e.g. "WAS 5011 worker process crash".</summary>
    public string? Signal { get; set; }

    /// <summary>"eventlog", "httperr", or "sitefinity".</summary>
    public string? Source { get; set; }

    /// <summary>Everything else that clustered with the headline, summarised.</summary>
    public string? Detail { get; set; }

    /// <summary>Total signals that merged into this candidate.</summary>
    public int SignalCount { get; set; }
}

/// <summary>
/// Search-mode result: every source swept over the lookback period for a plain substring. Uses the
/// same section shapes as window mode, so one set of fields reads either way.
/// </summary>
public sealed class IncidentSearchResponse
{
    /// <summary>Echo of the substring that was matched (case-insensitively, after redaction).</summary>
    public string? Query { get; set; }

    public string ServerTimeZoneId { get; set; } = string.Empty;
    public int ServerUtcOffsetMinutes { get; set; }

    public int LookbackHours { get; set; }
    /// <summary>ISO 8601 UTC.</summary>
    public string LookbackStartUtc { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC.</summary>
    public string LookbackEndUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix.</summary>
    public string LookbackStartLocal { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix.</summary>
    public string LookbackEndLocal { get; set; } = string.Empty;

    public List<string> ScannedSources { get; set; } = [];

    public IncidentSitefinitySection? Sitefinity { get; set; }
    public IncidentIisSection? Iis { get; set; }
    public IncidentEventLogSection? EventLog { get; set; }
    public IncidentHttpErrSection? HttpErr { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// A correlated view of four independent log sources across one time window: Sitefinity's own logs,
/// the site's IIS W3C access log, the Windows Application and System event logs, and the http.sys
/// HTTPERR log.
/// <para>
/// The sources disagree about clocks — Sitefinity writes server-local time, W3C and HTTPERR are always
/// UTC, Event Log records are stored in UTC — so every entry carries both a UTC and a server-local
/// timestamp, and the response states the offset that applied at the queried instant.
/// </para>
/// </summary>
public sealed class IncidentWindowResponse
{
    /// <summary>The server's time zone id, e.g. "Eastern Standard Time".</summary>
    public string ServerTimeZoneId { get; set; } = string.Empty;

    /// <summary>The server's UTC offset in minutes, evaluated at the queried instant (DST-correct).</summary>
    public int ServerUtcOffsetMinutes { get; set; }

    /// <summary>ISO 8601 UTC, e.g. "2026-08-27T15:00:00Z".</summary>
    public string CenterUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix, e.g. "2026-08-27T11:00:00".</summary>
    public string CenterLocal { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC.</summary>
    public string WindowStartUtc { get; set; } = string.Empty;

    /// <summary>ISO 8601 UTC.</summary>
    public string WindowEndUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix.</summary>
    public string WindowStartLocal { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix.</summary>
    public string WindowEndLocal { get; set; } = string.Empty;

    /// <summary>Half-width of the window in minutes, after clamping.</summary>
    public int WindowMinutes { get; set; }

    /// <summary>The sources that were actually collected.</summary>
    public List<string> RequestedSources { get; set; } = [];

    /// <summary>Echo of the substring filter that was applied to entries, if any.</summary>
    public string? Query { get; set; }

    public IncidentSitefinitySection? Sitefinity { get; set; }
    public IncidentIisSection? Iis { get; set; }
    public IncidentEventLogSection? EventLog { get; set; }
    public IncidentHttpErrSection? HttpErr { get; set; }

    /// <summary>Top-level problems (bad input, a source that could not be reached at all).</summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>Sitefinity's own Error/Trace log entries that fell inside the window.</summary>
public sealed class IncidentSitefinitySection
{
    public bool Available { get; set; }
    public string LogsPath { get; set; } = string.Empty;
    public List<string> FilesScanned { get; set; } = [];

    /// <summary>How the raw log timestamps were interpreted (Sitefinity writes server-local by default).</summary>
    public string TimestampInterpretation { get; set; } = string.Empty;

    /// <summary>Entries inside the window before any query filter or cap.</summary>
    public int TotalMatched { get; set; }

    /// <summary>
    /// Entries that also contained the query substring. Equals <see cref="TotalMatched"/> when no
    /// query was supplied. Matching runs after redaction.
    /// </summary>
    public int MatchedCount { get; set; }

    public int ReturnedCount { get; set; }
    public bool Truncated { get; set; }

    public List<IncidentLogEntry> Entries { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>One parsed Sitefinity log entry.</summary>
public sealed class IncidentLogEntry
{
    /// <summary>ISO 8601 UTC, e.g. "2026-08-27T15:01:12Z".</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix, e.g. "2026-08-27T11:01:12".</summary>
    public string TimestampLocal { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? Severity { get; set; }
    public string? Type { get; set; }
    public string? Message { get; set; }
    public string? RequestedUrl { get; set; }

    /// <summary>First few stack frames, when the entry carried a stack trace.</summary>
    public string? StackTraceHead { get; set; }
}

/// <summary>
/// Aggregated IIS W3C access-log activity. Raw log lines are never returned — only counts, a status
/// histogram, the 5xx responses, and the slowest requests.
/// </summary>
public sealed class IncidentIisSection
{
    public bool Available { get; set; }

    /// <summary>W3C log timestamps are always UTC, regardless of the log-rollover setting.</summary>
    public string TimestampInterpretation { get; set; } = string.Empty;

    public int SiteId { get; set; }
    public string? LogFolder { get; set; }
    public List<string> FilesScanned { get; set; } = [];

    public long LinesScanned { get; set; }

    /// <summary>Data lines that could not be parsed.</summary>
    public int MalformedLines { get; set; }

    /// <summary>True when the line-scan ceiling was hit and later lines were not examined.</summary>
    public bool Truncated { get; set; }

    public int TotalRequests { get; set; }

    /// <summary>
    /// Per-minute request counts — the shape of a traffic falloff at the moment of a crash. Populated in
    /// WINDOW mode only (at most 240 rows). Empty in search mode; see <see cref="RequestsPerHour"/>.
    /// </summary>
    public List<IncidentMinuteCount> RequestsPerMinute { get; set; } = [];

    /// <summary>
    /// Per-hour request counts. Populated in SEARCH mode only, where a lookback of up to 14 days would
    /// otherwise emit ~20,000 minute rows. Empty in window mode; see <see cref="RequestsPerMinute"/>.
    /// </summary>
    public List<IncidentMinuteCount> RequestsPerHour { get; set; } = [];

    /// <summary>Counts keyed by "status.substatus", e.g. "200.0", "500.0", "503.2".</summary>
    public List<IncidentCount> StatusHistogram { get; set; } = [];

    public int TotalServerErrors { get; set; }
    public int ReturnedServerErrors { get; set; }
    public bool ServerErrorsTruncated { get; set; }
    public List<IisRequestEntry> ServerErrors { get; set; } = [];

    public List<IisRequestEntry> SlowestRequests { get; set; } = [];

    /// <summary>
    /// Requests whose redacted username / URI / query / client IP / referer contained the query
    /// substring — ALL status codes, not just 5xx, because a user's full request trail is the point.
    /// Empty when no query was supplied. The aggregates above always cover the whole window,
    /// unfiltered, so traffic context survives the filter.
    /// </summary>
    public List<IisRequestEntry> MatchedRequests { get; set; } = [];

    /// <summary>
    /// Requests matching the query across the whole window, before the cap. Zero when no query was
    /// supplied — use <see cref="TotalRequests"/> for the unfiltered count.
    /// </summary>
    public int MatchedCount { get; set; }
    public bool MatchedRequestsTruncated { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// One bucket of the request-rate series, labelled in both clocks. Bucket width is a minute or an hour
/// depending on which list it came from.
/// </summary>
public sealed class IncidentMinuteCount
{
    /// <summary>Bucket start in UTC, e.g. "2026-08-27 15:01".</summary>
    public string MinuteUtc { get; set; } = string.Empty;

    /// <summary>Bucket start in server-local wall time, e.g. "2026-08-27 11:01".</summary>
    public string MinuteLocal { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>A histogram bucket.</summary>
public sealed class IncidentCount
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>
/// One IIS request. Client IP and <c>cs-username</c> are deliberately retained — they are the point of
/// correlating an outage to who was hitting what. Query strings arrive already redacted, and
/// <c>cs(Cookie)</c> / <c>cs(Authorization)</c> columns are never read by the plugin.
/// </summary>
public sealed class IisRequestEntry
{
    /// <summary>ISO 8601 UTC, e.g. "2026-08-27T15:01:12Z".</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix, e.g. "2026-08-27T11:01:12".</summary>
    public string TimestampLocal { get; set; } = string.Empty;
    public string? Method { get; set; }
    public string? UriStem { get; set; }
    public string? UriQuery { get; set; }
    public int Status { get; set; }
    public int SubStatus { get; set; }
    public long Win32Status { get; set; }
    public int TimeTakenMs { get; set; }
    public string? UserName { get; set; }
    public string? ClientIp { get; set; }

    /// <summary>
    /// The <c>cs(Referer)</c> column, redacted. Returned because a query can match on it — without the
    /// field, a referer hit reads as a false positive (a search for "macdot" legitimately matching
    /// /appstatus requests referred from a macdot page). Empty when the server does not log the column.
    /// </summary>
    public string? Referer { get; set; }
}

/// <summary>
/// http.sys error-log activity — the 503s that never reach the site's own IIS log because the app pool
/// was already down (AppOffline, QueueFull, Disabled, connection timers).
/// </summary>
public sealed class IncidentHttpErrSection
{
    public bool Available { get; set; }

    /// <summary>HTTPERR timestamps are always UTC.</summary>
    public string TimestampInterpretation { get; set; } = string.Empty;

    public string? LogFolder { get; set; }
    public List<string> FilesScanned { get; set; } = [];
    public long LinesScanned { get; set; }
    public int MalformedLines { get; set; }

    public int TotalMatched { get; set; }

    /// <summary>
    /// Records that also contained the query substring. Equals <see cref="TotalMatched"/> when no
    /// query was supplied. Matching runs after redaction.
    /// </summary>
    public int MatchedCount { get; set; }

    public int ReturnedCount { get; set; }
    public bool Truncated { get; set; }

    /// <summary>Counts keyed by the http.sys reason phrase, e.g. "AppOffline", "QueueFull".</summary>
    public List<IncidentCount> ReasonHistogram { get; set; } = [];

    public List<HttpErrEntry> Entries { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>One http.sys error-log record.</summary>
public sealed class HttpErrEntry
{
    /// <summary>ISO 8601 UTC, e.g. "2026-08-27T15:01:12Z".</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix, e.g. "2026-08-27T11:01:12".</summary>
    public string TimestampLocal { get; set; } = string.Empty;
    public string? ClientIp { get; set; }
    public string? Method { get; set; }
    public string? Uri { get; set; }
    public int Status { get; set; }

    /// <summary>http.sys reason, e.g. "AppOffline", "QueueFull", "Timer_ConnectionIdle".</summary>
    public string? Reason { get; set; }

    public string? QueueName { get; set; }
}

/// <summary>Windows Event Log entries from the Application and System channels. Security is never read.</summary>
public sealed class IncidentEventLogSection
{
    public bool Available { get; set; }

    /// <summary>Event Log records are stored in UTC and reported here in both forms.</summary>
    public string TimestampInterpretation { get; set; } = string.Empty;

    public List<EventLogChannel> Channels { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>One event-log channel's results.</summary>
public sealed class EventLogChannel
{
    public string LogName { get; set; } = string.Empty;
    public bool Available { get; set; }

    /// <summary>Records the XPath query returned, before provider filtering, the query filter, and the cap.</summary>
    public int TotalMatched { get; set; }

    /// <summary>
    /// Records that survived provider filtering AND contained the query substring. Matching runs after
    /// redaction.
    /// </summary>
    public int MatchedCount { get; set; }

    public int ReturnedCount { get; set; }
    public bool Truncated { get; set; }

    public List<EventLogEntryInfo> Entries { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>One Windows event.</summary>
public sealed class EventLogEntryInfo
{
    /// <summary>ISO 8601 UTC, e.g. "2026-08-27T15:01:12Z".</summary>
    public string TimestampUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time, NO zone suffix, e.g. "2026-08-27T11:01:12".</summary>
    public string TimestampLocal { get; set; } = string.Empty;
    public string? LogName { get; set; }
    public int EventId { get; set; }

    /// <summary>"Critical", "Error", "Warning", "Information", or the numeric level when unmapped.</summary>
    public string? Level { get; set; }

    public string? ProviderName { get; set; }

    /// <summary>Rendered description, redacted and truncated to 1000 characters.</summary>
    public string? Message { get; set; }
}

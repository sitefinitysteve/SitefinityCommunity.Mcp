namespace SitefinityCommunity.Mcp.Models;

// Timestamps in this file are STRINGS, not DateTime, and that is deliberate — see the matching note in
// Models/IncidentWindowResponse.cs and in the plugin's McpTasksService. ServiceStack serializes a
// DateTime DTO property as /Date(ms)/, an instant, which turns a server-local wall time into a point on
// the timeline that this side then re-renders as UTC. The plugin pre-formats both forms; these
// properties carry them verbatim.

/// <summary>
/// Snapshot of the Sitefinity scheduler: what is executing right now, and which task rows are marked
/// failed. Successful executions are deliberately not reported — the scheduler deletes those rows, so
/// the trace log is the real history.
/// </summary>
public sealed class ScheduledTaskStatusResponse
{
    /// <summary>Windows time zone id of the server, e.g. <c>Eastern Standard Time</c>.</summary>
    public string ServerTimeZoneId { get; set; } = string.Empty;

    /// <summary>Server UTC offset in minutes at the moment the snapshot was taken.</summary>
    public int ServerUtcOffsetMinutes { get; set; }

    /// <summary>ISO 8601 UTC instant the snapshot was taken.</summary>
    public string SnapshotUtc { get; set; } = string.Empty;

    /// <summary>Server-local wall time the snapshot was taken, with no zone suffix.</summary>
    public string SnapshotLocal { get; set; } = string.Empty;

    /// <summary>Tasks the scheduler currently has flagged as running. Capped at 25.</summary>
    public List<RunningTaskInfo> RunningNow { get; set; } = [];

    /// <summary>
    /// Task rows whose scheduler status is <c>Failed</c>, newest execution first. Capped at 10.
    /// A failed reindex row here is why a search index has silently stopped updating.
    /// </summary>
    public List<FailedTaskInfo> Failed { get; set; } = [];

    /// <summary>Where successful-execution history lives, since it is not reported here.</summary>
    public string? HistoryNote { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>One task the scheduler is executing right now.</summary>
public sealed class RunningTaskInfo
{
    public string? Id { get; set; }

    /// <summary>
    /// The task's name as the Sitefinity admin's Scheduled tasks screen shows it — its CLR type name,
    /// e.g. <c>Telerik.Sitefinity.Publishing.ReindexTask</c>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Human title, falling back to the name when the row has none.</summary>
    public string? Title { get; set; }

    /// <summary>What the task is operating on — for a reindex, the search index name.</summary>
    public string? ItemName { get; set; }

    public string? Description { get; set; }

    /// <summary>Scheduler key — for a reindex this usually carries the publishing point identity.</summary>
    public string? Key { get; set; }

    /// <summary>ISO 8601 UTC, e.g. <c>2026-08-28T15:01:12Z</c>.</summary>
    public string? StartedUtc { get; set; }

    /// <summary>Server-local wall time with no zone suffix, e.g. <c>2026-08-28T11:01:12</c>.</summary>
    public string? StartedLocal { get; set; }

    /// <summary>
    /// Which scheduler column the start time came from. Sitefinity has no dedicated "started" column,
    /// so the proxy used is stated rather than implied.
    /// </summary>
    public string? StartedSource { get; set; }

    /// <summary>Seconds elapsed since the task started. A large value on a short task means it is hung.</summary>
    public long RunningForSeconds { get; set; }

    /// <summary>Whether this task is rebuilding a search index.</summary>
    public bool IsSearchIndexRebuild { get; set; }

    /// <summary>Search index (catalog) being rebuilt, when one could be identified.</summary>
    public string? IndexName { get; set; }

    /// <summary>Percentage complete, when this Sitefinity version reports it.</summary>
    public int? Progress { get; set; }

    /// <summary>Scheduler status value, when this Sitefinity version reports it.</summary>
    public string? Status { get; set; }

    /// <summary>Scheduler status message, when this Sitefinity version reports it.</summary>
    public string? StatusMessage { get; set; }
}

/// <summary>One task the scheduler has marked as failed.</summary>
public sealed class FailedTaskInfo
{
    public string? Id { get; set; }

    /// <summary>
    /// The task's name as the admin's Scheduled tasks screen shows it — its CLR type name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Human title, falling back to the name when the row has none.</summary>
    public string? Title { get; set; }

    /// <summary>What the task was operating on — for a failed reindex, the search index name.</summary>
    public string? ItemName { get; set; }

    /// <summary>Scheduler key.</summary>
    public string? Key { get; set; }

    /// <summary>ISO 8601 UTC the task was scheduled for.</summary>
    public string? ScheduledForUtc { get; set; }

    /// <summary>Server-local wall time the task was scheduled for, with no zone suffix.</summary>
    public string? ScheduledForLocal { get; set; }

    /// <summary>ISO 8601 UTC the failed execution ran. Null when the row never recorded one.</summary>
    public string? ExecutedOnUtc { get; set; }

    /// <summary>Server-local wall time the failed execution ran, with no zone suffix.</summary>
    public string? ExecutedOnLocal { get; set; }

    /// <summary>Scheduler status value — <c>Failed</c> for every row in this list.</summary>
    public string? Status { get; set; }

    /// <summary>Scheduler status message. Usually the failure reason.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>Whether the failed task was rebuilding a search index.</summary>
    public bool IsSearchIndexRebuild { get; set; }

    /// <summary>Search index (catalog) the failed rebuild targeted, when one could be identified.</summary>
    public string? IndexName { get; set; }
}

namespace SitefinityCommunity.Mcp.Models;

// Timestamps in this file are STRINGS, not DateTime, and that is deliberate — see the matching note in
// Models/IncidentWindowResponse.cs and in the plugin's McpTasksService. ServiceStack serializes a
// DateTime DTO property as /Date(ms)/, an instant, which turns a server-local wall time into a point on
// the timeline that this side then re-renders as UTC. The plugin pre-formats both forms; these
// properties carry them verbatim.

/// <summary>
/// Every configured search index with the fullest state the publishing, search and scheduler data can
/// supply. Sites have a handful of indexes, so the list is never capped.
/// </summary>
public sealed class SearchIndexesResponse
{
    /// <summary>Windows time zone id of the server, e.g. <c>Eastern Standard Time</c>.</summary>
    public string ServerTimeZoneId { get; set; } = string.Empty;

    /// <summary>Server UTC offset in minutes at the moment the snapshot was taken.</summary>
    public int ServerUtcOffsetMinutes { get; set; }

    /// <summary>
    /// CLR type name of the concrete search service, e.g. <c>LuceneSearchService</c>. Sitefinity's
    /// decorators (<c>GuardedSearchServiceDecorator</c> and friends) are unwrapped, so this names the
    /// real backend rather than the wrapper.
    /// </summary>
    public string? SearchServiceType { get; set; }

    /// <summary>
    /// Publishing providers that were queried. Search indexes live under their own provider
    /// (<c>SearchPublishingProvider</c>), not the default one, so this names every provider searched —
    /// the first thing to check if the list comes back empty.
    /// </summary>
    public List<string> ProvidersScanned { get; set; } = [];

    public int TotalIndexes { get; set; }

    public List<SearchIndexInfo> Indexes { get; set; } = [];

    /// <summary>
    /// Per-index notes about anything the backend could not answer. Read these before concluding that
    /// a null document count or last-updated time means "empty" or "never".
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}

/// <summary>One configured search index — a search-index pipe on a publishing point.</summary>
public sealed class SearchIndexInfo
{
    /// <summary>Catalog name — the value a search widget or query targets, e.g. <c>docs-index</c>.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Display name as the admin's "Search indexes" screen shows it, e.g. <c>Docs Index</c> — the name
    /// scheduled-task rows use. Never blank.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Where <see cref="Title"/> came from: <c>PublishingPointTitle</c>, <c>PublishingPointName</c>,
    /// <c>PipeTitle</c>, <c>PipeUIName</c>, or <c>derived</c> when the title was humanized from the
    /// catalog name because nothing named the index in human terms.
    /// </summary>
    public string? TitleSource { get; set; }

    /// <summary>Publishing point that owns the index pipe.</summary>
    public string? PublishingPointName { get; set; }

    /// <summary>Publishing provider the point lives under, e.g. <c>SearchPublishingProvider</c>.</summary>
    public string? PublishingProvider { get; set; }

    /// <summary>
    /// Search provider serving this index, falling back to the concrete search service type when the
    /// pipe names none.
    /// </summary>
    public string? Backend { get; set; }

    /// <summary>Whether both the publishing point and its index pipe are active.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether the backend reports the index as existing. Null when it could not be asked — which is
    /// not the same as "missing".
    /// </summary>
    public bool? Exists { get; set; }

    /// <summary>
    /// Number of documents in the index, when the backend exposes a count. Null otherwise, with a
    /// matching entry in <see cref="SearchIndexesResponse.Warnings"/>.
    /// </summary>
    public long? DocumentCount { get; set; }

    /// <summary>ISO 8601 UTC of the last known index update, when obtainable.</summary>
    public string? LastUpdatedUtc { get; set; }

    /// <summary>Server-local wall time of the last known index update, with no zone suffix.</summary>
    public string? LastUpdatedLocal { get; set; }

    /// <summary>Where the last-updated value came from — <c>IndexFolder</c> or <c>LastPublicationDate</c>.</summary>
    public string? LastUpdatedSource { get; set; }

    /// <summary>Whether a scheduler task is rebuilding this index right now.</summary>
    public bool IsRebuilding { get; set; }

    /// <summary>The rebuilding task's type, when one was matched.</summary>
    public string? RebuildTaskType { get; set; }

    /// <summary>The rebuilding task's reported progress, when available.</summary>
    public int? RebuildProgress { get; set; }

    /// <summary>
    /// Outcome of the most recent reindex row the scheduler still holds: <c>running</c>,
    /// <c>failed</c>, <c>completed</c>, or <c>unknown</c> when no row survives. A <c>failed</c> value
    /// is the usual reason an index has silently gone stale.
    /// </summary>
    public string? LastReindexStatus { get; set; }

    /// <summary>ISO 8601 UTC the last known reindex ran, when a row records it.</summary>
    public string? LastReindexUtc { get; set; }

    /// <summary>Server-local wall time the last known reindex ran, with no zone suffix.</summary>
    public string? LastReindexLocal { get; set; }

    /// <summary>
    /// What feeds this index — the inbound pipes on its publishing point, named by content type where
    /// the pipe settings expose one.
    /// </summary>
    public List<string> ContentSources { get; set; } = [];
}

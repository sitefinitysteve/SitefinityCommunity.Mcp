using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// Live runtime diagnostics: what the Sitefinity scheduler is doing, and whether the search indexes
/// are healthy. The two tools are a pair — a hung or failed reindex task explains a stale index, and
/// a stale index sends you looking at the scheduler.
/// </summary>
[McpServerToolType]
public sealed class DiagnosticsTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public DiagnosticsTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_get_scheduled_task_status", Title = "Get Scheduled Task Status", ReadOnly = true, UseStructuredContent = true)]
    [Description("THE tool for \"is a scheduled task running right now?\", \"is a task hung?\", \"why is the CPU " +
                 "pinned?\", and \"which scheduled tasks have FAILED?\". Returns exactly two sections:\n\n" +
                 "1. RunningNow — every task the scheduler currently has flagged as running, with its type, what " +
                 "it is operating on, when it started (BOTH UTC and server-local, pre-formatted so the two " +
                 "readings can never be confused), and RunningForSeconds. A large RunningForSeconds on a task " +
                 "that normally takes seconds is a hung task. Search index rebuilds are flagged with " +
                 "IsSearchIndexRebuild and, where identifiable, the IndexName they are rebuilding.\n" +
                 "2. Failed — task rows the scheduler has marked Failed, newest first, with ScheduledFor and " +
                 "ExecutedOn timestamps and the status message. This is the section people never look at: a " +
                 "reindex that failed weeks ago sits here silently while search quietly serves stale results.\n\n" +
                 "It does NOT list successfully completed tasks — Sitefinity's scheduler deletes those rows, so " +
                 "they are not there to read. For execution history use sitefinity_search_logs with the pattern " +
                 "\"Scheduler: Task executed\". For search index health specifically (which index is stale, and " +
                 "why) use sitefinity_list_search_indexes. For the surrounding evidence when something broke — " +
                 "IIS, the Windows event log, HTTPERR — use sitefinity_investigate_incident.")]
    public async Task<ScheduledTaskStatusResponse> GetScheduledTaskStatus(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            return await this._metadataService.GetScheduledTaskStatusAsync(environment, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }

    [McpServerTool(Name = "sitefinity_list_search_indexes", Title = "List Search Indexes", ReadOnly = true, UseStructuredContent = true)]
    [Description("THE tool for \"why is search stale / missing results?\", \"is the search index healthy?\", and " +
                 "\"when was this index last built?\". Lists EVERY configured search index (sites have a " +
                 "handful, so nothing is capped) with: the catalog Name a search widget targets, the Backend " +
                 "serving it (Lucene, Azure Search, Elasticsearch, hybrid), whether the backend says it Exists, " +
                 "DocumentCount where the backend exposes one, LastUpdatedUtc/Local, whether a rebuild " +
                 "IsRebuilding right now, and — the usual answer — LastReindexStatus (running / failed / " +
                 "completed / unknown) with LastReindexUtc/Local, cross-referenced against the scheduler's own " +
                 "task rows. A LastReindexStatus of \"failed\" is why the index stopped updating. " +
                 "ContentSources names what feeds the index.\n\n" +
                 "Timestamps come pre-formatted in BOTH UTC and server-local form. Always read Warnings: they " +
                 "say per index which fields this backend could not answer, so a null DocumentCount means " +
                 "\"not obtainable\", never \"empty\". For what is running or failing right now across all " +
                 "scheduled work, see sitefinity_get_scheduled_task_status.")]
    public async Task<SearchIndexesResponse> ListSearchIndexes(
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            return await this._metadataService.GetSearchIndexesAsync(environment, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }
}

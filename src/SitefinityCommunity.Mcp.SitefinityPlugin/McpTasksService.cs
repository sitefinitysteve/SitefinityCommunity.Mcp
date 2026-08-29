// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Publishing;
using Telerik.Sitefinity.Publishing.Configuration;
using Telerik.Sitefinity.Publishing.Model;
using Telerik.Sitefinity.Scheduling;
using Telerik.Sitefinity.Scheduling.Model;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Live diagnostics for the two things that quietly break a Sitefinity site: a scheduled task that
    /// is running (or hung), and a search index that has stopped being updated because its reindex
    /// task failed weeks ago and nobody looked.
    ///
    /// <para>
    /// <b>GET /mcp/scheduled-tasks</b> returns exactly two sections — what the scheduler is executing
    /// right now, and which task rows are marked <c>Failed</c>. Both are bounded, status-filtered
    /// queries; the scheduled-task store is never enumerated, because on a busy site it holds a great
    /// many rows. Successful executions are not returned at all: the scheduler deletes those rows, and
    /// the trace log is the real history (search it for <c>Scheduler: Task executed</c>).
    /// </para>
    ///
    /// <para>
    /// <b>GET /mcp/search-indexes</b> enumerates the configured search indexes — each one is a
    /// search-index pipe on a publishing point — with the backend serving it, whether the backend says
    /// it exists, when it was last updated, whether a rebuild is running, and whether its last reindex
    /// FAILED. That last cross-reference is what turns "why is search stale?" into a one-call answer.
    /// </para>
    ///
    /// <para>
    /// <b>Timestamps on the wire are pre-formatted STRINGS.</b> ServiceStack serializes a
    /// <c>DateTime</c> DTO property as <c>/Date(ms)/</c> — an instant — which destroys a server-local
    /// wall time. UTC values are written as <c>yyyy-MM-ddTHH:mm:ssZ</c> and local values as
    /// <c>yyyy-MM-ddTHH:mm:ss</c> with no suffix; the response states the zone once. Never "fix" this
    /// by touching <c>JsConfig</c>: Sitefinity's own backend shares this ServiceStack instance, and a
    /// global date-format change breaks the admin site-wide.
    /// </para>
    ///
    /// <para>
    /// Every section is collected defensively. The scheduling and search APIs drift across Sitefinity
    /// versions and backends (Lucene, Azure Search, Elasticsearch, hybrid), so anything unobtainable
    /// becomes a <c>Warnings</c> entry and a null field — these endpoints do not fail with a 500 over a
    /// data problem. All outbound strings pass through <see cref="McpSecretRedactor"/>.
    /// </para>
    /// </summary>
    [McpApiKey]
    public class McpTasksService : Service
    {
        private const int MaxRunning = 25;
        private const int MaxFailed = 10;
        private const int MaxTextLength = 500;

        /// <summary>
        /// Task type-name fragments that identify a search index rebuild. Matched case-insensitively
        /// against the task's CLR type name, which is the only identity a task row carries that does
        /// not vary with a site's own naming.
        /// </summary>
        private static readonly string[] RebuildTaskFragments =
        {
            "reindex", "searchindex", "rebuildindex", "indexingtask", "searchindexing",
        };

        /// <summary>
        /// GET /RestApi/mcp/scheduled-tasks — running tasks and failed tasks.
        /// </summary>
        /// <param name="request">Empty request DTO.</param>
        public McpScheduledTaskStatusResponse Get(GetScheduledTasks request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Tasks);

            var nowUtc = DateTime.UtcNow;
            var response = new McpScheduledTaskStatusResponse
            {
                ServerTimeZoneId = TimeZoneInfo.Local.Id,
                ServerUtcOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(nowUtc).TotalMinutes,
                SnapshotUtc = FormatUtc(nowUtc),
                SnapshotLocal = FormatLocal(ToLocal(nowUtc)),
                HistoryNote = "Successfully completed tasks are not listed — Sitefinity's scheduler deletes " +
                    "those rows, so this table is not an execution history. Use sitefinity_search_logs with the " +
                    "pattern \"Scheduler: Task executed\" for what actually ran and when.",
            };

            // Knowing the configured index names is what turns "a ReindexTask is running" into "the
            // Docs Index is rebuilding". A failure here must not cost us the task lists.
            var pipes = CollectIndexPipes(response.Warnings);

            SchedulingManager manager;

            try
            {
                manager = SchedulingManager.GetManager();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not open the scheduling manager: " + ex.Message);
                return response;
            }

            this.CollectRunningTasks(manager, pipes, nowUtc, response);
            this.CollectFailedTasks(manager, pipes, response);

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/search-indexes — the configured search indexes and their health.
        /// </summary>
        /// <param name="request">Empty request DTO.</param>
        public McpSearchIndexesResponse Get(GetSearchIndexes request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Tasks);

            var nowUtc = DateTime.UtcNow;
            var response = new McpSearchIndexesResponse
            {
                ServerTimeZoneId = TimeZoneInfo.Local.Id,
                ServerUtcOffsetMinutes = (int)TimeZoneInfo.Local.GetUtcOffset(nowUtc).TotalMinutes,
            };

            List<string> providersScanned;
            var pipes = CollectIndexPipes(response.Warnings, out providersScanned);
            response.ProvidersScanned = providersScanned;

            var searchService = ResolveSearchService(response.Warnings);

            // Sitefinity wraps the real service in decorators (GuardedSearchServiceDecorator and
            // friends). The decorator answers the interface but not the concrete backend's own members
            // — GetCataloguePath among them — and its type name says nothing useful about the backend,
            // so unwrap to the innermost service and keep both.
            var backendService = UnwrapSearchService(searchService);
            var backendObscured = searchService != null && IsDecorator(backendService);

            if (backendService != null)
            {
                response.SearchServiceType = backendService.GetType().Name;
            }

            if (backendObscured)
            {
                // Say so once, plainly, instead of reporting the wrapper's type name as if it were the
                // backend. Everything that needs the concrete service is then skipped silently rather
                // than adding a near-identical note to every index.
                response.Warnings.Add("Backend obscured by '" + backendService.GetType().Name +
                    "': the concrete search service could not be reached, so Backend is null where the " +
                    "pipe names no provider, and document counts and index-folder freshness are " +
                    "unavailable. Last-updated still falls back to the publishing point's last " +
                    "publication date where one is recorded.");
            }

            var rebuilds = this.CollectRebuildTasks(pipes, nowUtc, response.Warnings);

            foreach (var pipe in pipes)
            {
                var info = new McpSearchIndexInfo
                {
                    Name = Clean(pipe.CatalogName),
                    Title = Clean(pipe.UIName),
                    PublishingPointName = Clean(pipe.PointName),
                    PublishingProvider = Clean(pipe.ProviderName),
                    TitleSource = pipe.TitleSource,
                    IsActive = pipe.IsActive,
                    ContentSources = pipe.ContentSources,
                };

                info.Backend = Clean(pipe.Backend) ?? (backendObscured ? null : response.SearchServiceType);

                // The wrapper still answers the interface, so existence is worth asking either way.
                info.Exists = TryIndexExists(searchService, pipe.CatalogName)
                    ?? TryIndexExists(backendService, pipe.CatalogName);

                if (!backendObscured)
                {
                    info.DocumentCount = TryDocumentCount(backendService, pipe.CatalogName)
                        ?? TryDocumentCount(searchService, pipe.CatalogName);
                }

                ApplyLastUpdated(info, pipe, backendObscured ? null : backendService, response.Warnings);
                ApplyRebuildState(info, pipe, rebuilds);

                AddUnobtainableWarnings(info, response.Warnings, backendObscured);

                response.Indexes.Add(info);
            }

            response.TotalIndexes = response.Indexes.Count;

            if (response.TotalIndexes == 0)
            {
                response.Warnings.Add("No search-index pipes were found in any publishing provider (" +
                    string.Join(", ", providersScanned.ToArray()) + "). Search indexes are created in " +
                    "Administration > Search indexes and normally live under the '" +
                    PublishingConfig.SearchProviderName + "' publishing provider, not the default one.");
            }

            return response;
        }

        /// <summary>
        /// Records, per index, exactly which fields this backend could not answer — so a null reads as
        /// "not obtainable here" rather than as a value of zero or never.
        /// </summary>
        /// <param name="info">Index entry that has been populated.</param>
        /// <param name="warnings">Warning sink.</param>
        /// <param name="backendObscured">
        /// Whether the concrete search service was unreachable behind a decorator, in which case the
        /// document-count and freshness notes are left to the single response-level warning.
        /// </param>
        private static void AddUnobtainableWarnings(
            McpSearchIndexInfo info, List<string> warnings, bool backendObscured)
        {
            var label = string.IsNullOrWhiteSpace(info.Name) ? "(unnamed index)" : info.Name;

            if (info.Exists == null)
            {
                warnings.Add("Index '" + label + "': the backend did not answer whether the index exists.");
            }

            if (backendObscured)
            {
                // One response-level warning already explains both gaps; repeating it per index would
                // bury the notes that are actually index-specific.
                return;
            }

            if (info.DocumentCount == null)
            {
                warnings.Add("Index '" + label + "': document count is not obtainable — Sitefinity's search " +
                    "interface has no portable count call and this backend exposes none.");
            }

            if (string.IsNullOrEmpty(info.LastUpdatedUtc))
            {
                warnings.Add("Index '" + label + "': last-updated time is not obtainable — the backend is not " +
                    "file-backed and the publishing point records no last publication date.");
            }
        }

        // ── Scheduler ────────────────────────────────────────────────

        /// <summary>
        /// Reads the running task set — a status-filtered query capped at <c>MaxRunning</c>, never a
        /// full enumeration. A query failure degrades to a warning rather than failing the endpoint.
        /// </summary>
        /// <param name="manager">Open scheduling manager.</param>
        /// <param name="pipes">Configured search index pipes, for rebuild identification.</param>
        /// <param name="nowUtc">Snapshot instant.</param>
        /// <param name="response">Response being populated.</param>
        private void CollectRunningTasks(
            SchedulingManager manager, List<IndexPipeRef> pipes, DateTime nowUtc,
            McpScheduledTaskStatusResponse response)
        {
            List<ScheduledTaskData> running;

            try
            {
                // IsRunning alone is NOT trustworthy: Sitefinity leaves it set on rows that later
                // failed or were left pending, so filtering on it alone reports months-old Failed and
                // Pending rows as "running" — and double-lists the failed ones, which also appear in
                // Failed. A row counts as running only when the scheduler ALSO says its status is
                // Started. When the status cannot be read at all, rows are excluded rather than
                // guessed at: a wrong "running" claim sends someone hunting a process that ended
                // months ago.
                running = manager.GetTaskData()
                    .Where(t => t.IsRunning && t.Status == TaskStatus.Started)
                    .Take(MaxRunning)
                    .ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not read running tasks, so RunningNow is empty rather than " +
                    "unfiltered — a row's IsRunning flag alone is not proof it is running: " + ex.Message);
                return;
            }

            foreach (var task in running)
            {
                try
                {
                    string startedSource;
                    var startedUtc = ResolveStart(task, out startedSource);
                    var isRebuild = IsRebuildTask(task);

                    response.RunningNow.Add(new McpRunningTaskInfo
                    {
                        Id = ReadId(task),
                        Name = Clean(task.TaskName),
                        Title = BuildTitle(task),
                        ItemName = BuildItemName(task),
                        Description = Clean(task.Description),
                        Key = Clean(task.Key),
                        StartedUtc = FormatUtc(startedUtc),
                        StartedLocal = FormatLocal(ToLocal(startedUtc)),
                        StartedSource = startedSource,
                        RunningForSeconds = (long)Math.Max(0, (nowUtc - startedUtc).TotalSeconds),
                        Progress = ReadNullableInt(task, "Progress"),
                        Status = Clean(ReadMemberAsString(task, "Status")),
                        StatusMessage = Clean(ReadMemberAsString(task, "StatusMessage")),
                        IsSearchIndexRebuild = isRebuild,
                        IndexName = isRebuild ? Clean(MatchIndexName(task, pipes)) : null,
                    });
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Skipped a running task that could not be read: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Reads the failed task set — a status-filtered, newest-first query capped at
        /// <c>MaxFailed</c>. These rows are what the admin's Scheduled tasks screen shows as
        /// <c>Failed</c>, and a failed ReindexTask is the usual explanation for a stale search index.
        /// </summary>
        /// <param name="manager">Open scheduling manager.</param>
        /// <param name="pipes">Configured search index pipes, for rebuild identification.</param>
        /// <param name="response">Response being populated.</param>
        private void CollectFailedTasks(
            SchedulingManager manager, List<IndexPipeRef> pipes, McpScheduledTaskStatusResponse response)
        {
            List<ScheduledTaskData> failed;

            try
            {
                failed = manager.GetTaskData()
                    .Where(t => t.Status == TaskStatus.Failed)
                    .OrderByDescending(t => t.LastExecutedTime)
                    .Take(MaxFailed)
                    .ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Could not read failed tasks: " + ex.Message);
                return;
            }

            foreach (var task in failed)
            {
                try
                {
                    var isRebuild = IsRebuildTask(task);
                    var scheduledUtc = AsUtc(task.ExecuteTime);

                    var info = new McpFailedTaskInfo
                    {
                        Id = ReadId(task),
                        Name = Clean(task.TaskName),
                        Title = BuildTitle(task),
                        ItemName = BuildItemName(task),
                        Key = Clean(task.Key),
                        ScheduledForUtc = FormatUtc(scheduledUtc),
                        ScheduledForLocal = FormatLocal(ToLocal(scheduledUtc)),
                        Status = Clean(ReadMemberAsString(task, "Status")),
                        StatusMessage = Clean(ReadMemberAsString(task, "StatusMessage")),
                        IsSearchIndexRebuild = isRebuild,
                        IndexName = isRebuild ? Clean(MatchIndexName(task, pipes)) : null,
                    };

                    if (task.LastExecutedTime.HasValue && task.LastExecutedTime.Value != default(DateTime))
                    {
                        var executedUtc = AsUtc(task.LastExecutedTime.Value);
                        info.ExecutedOnUtc = FormatUtc(executedUtc);
                        info.ExecutedOnLocal = FormatLocal(ToLocal(executedUtc));
                    }

                    response.Failed.Add(info);
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Skipped a failed task that could not be read: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Collects the reindex tasks that matter to the index inventory: the ones running right now
        /// and the ones marked failed. Reuses the same bounded, status-filtered queries as the
        /// scheduled-tasks endpoint, so the two endpoints can never disagree.
        /// </summary>
        /// <param name="pipes">Configured search index pipes.</param>
        /// <param name="nowUtc">Snapshot instant.</param>
        /// <param name="warnings">Warning sink.</param>
        private List<RebuildTaskRef> CollectRebuildTasks(
            List<IndexPipeRef> pipes, DateTime nowUtc, List<string> warnings)
        {
            var refs = new List<RebuildTaskRef>();
            var status = new McpScheduledTaskStatusResponse();

            SchedulingManager manager;

            try
            {
                manager = SchedulingManager.GetManager();
            }
            catch (Exception ex)
            {
                warnings.Add("Could not check the scheduler for reindex tasks: " + ex.Message);
                return refs;
            }

            this.CollectRunningTasks(manager, pipes, nowUtc, status);
            this.CollectFailedTasks(manager, pipes, status);

            foreach (var warning in status.Warnings)
            {
                warnings.Add(warning);
            }

            foreach (var running in status.RunningNow)
            {
                if (!running.IsSearchIndexRebuild)
                {
                    continue;
                }

                refs.Add(new RebuildTaskRef
                {
                    IndexName = running.IndexName,
                    ItemName = running.ItemName,
                    TaskType = running.Name,
                    Outcome = "running",
                    Progress = running.Progress,
                    RanOnUtc = running.StartedUtc,
                    RanOnLocal = running.StartedLocal,
                });
            }

            foreach (var failure in status.Failed)
            {
                if (!failure.IsSearchIndexRebuild)
                {
                    continue;
                }

                refs.Add(new RebuildTaskRef
                {
                    IndexName = failure.IndexName,
                    ItemName = failure.ItemName,
                    TaskType = failure.Name,
                    Outcome = "failed",
                    RanOnUtc = failure.ExecutedOnUtc,
                    RanOnLocal = failure.ExecutedOnLocal,
                });
            }

            this.CollectCompletedRebuilds(manager, pipes, refs, warnings);

            return refs;
        }

        /// <summary>
        /// Adds reindex rows that ran and are neither running nor failed — a completed rebuild, when
        /// the scheduler has not yet deleted the row. Filtered by task type name so it stays a bounded
        /// query; a provider that cannot translate the filter is skipped with a warning rather than
        /// falling back to a full scan.
        /// </summary>
        /// <param name="manager">Open scheduling manager.</param>
        /// <param name="pipes">Configured search index pipes.</param>
        /// <param name="refs">Reference list being built.</param>
        /// <param name="warnings">Warning sink.</param>
        private void CollectCompletedRebuilds(
            SchedulingManager manager, List<IndexPipeRef> pipes, List<RebuildTaskRef> refs, List<string> warnings)
        {
            List<ScheduledTaskData> completed;

            try
            {
                completed = manager.GetTaskData()
                    .Where(t => !t.IsRunning && t.Status != TaskStatus.Failed &&
                        t.LastExecutedTime != null && t.TaskName.Contains("Reindex"))
                    .OrderByDescending(t => t.LastExecutedTime)
                    .Take(MaxFailed)
                    .ToList();
            }
            catch (Exception ex)
            {
                warnings.Add("Could not read completed reindex tasks, so LastReindexStatus may read " +
                    "\"unknown\" for an index that did rebuild successfully: " + ex.Message);
                return;
            }

            foreach (var task in completed)
            {
                try
                {
                    if (!IsRebuildTask(task))
                    {
                        continue;
                    }

                    var reference = new RebuildTaskRef
                    {
                        IndexName = Clean(MatchIndexName(task, pipes)),
                        ItemName = BuildItemName(task),
                        TaskType = Clean(task.TaskName),
                        Outcome = "completed",
                    };

                    if (task.LastExecutedTime.HasValue && task.LastExecutedTime.Value != default(DateTime))
                    {
                        var ranUtc = AsUtc(task.LastExecutedTime.Value);
                        reference.RanOnUtc = FormatUtc(ranUtc);
                        reference.RanOnLocal = FormatLocal(ToLocal(ranUtc));
                    }

                    refs.Add(reference);
                }
                catch (Exception ex)
                {
                    warnings.Add("Skipped a completed reindex task that could not be read: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Flags an index as rebuilding, and records the outcome of the most recent reindex row the
        /// scheduler still holds for it. A running rebuild always wins the status; otherwise the
        /// newest row does, with a failure preferred over a completion at the same instant so a
        /// broken index is never reported as healthy.
        /// </summary>
        /// <param name="info">Index entry being populated.</param>
        /// <param name="pipe">Source pipe reference.</param>
        /// <param name="rebuilds">Reindex tasks found in the scheduler.</param>
        private static void ApplyRebuildState(
            McpSearchIndexInfo info, IndexPipeRef pipe, List<RebuildTaskRef> rebuilds)
        {
            info.LastReindexStatus = "unknown";
            RebuildTaskRef best = null;

            foreach (var rebuild in rebuilds)
            {
                if (!Identifies(rebuild, pipe))
                {
                    continue;
                }

                if (rebuild.Outcome == "running")
                {
                    info.IsRebuilding = true;
                    info.RebuildTaskType = rebuild.TaskType;
                    info.RebuildProgress = rebuild.Progress;
                }

                if (best == null || Outranks(rebuild, best))
                {
                    best = rebuild;
                }
            }

            if (best == null)
            {
                return;
            }

            info.LastReindexStatus = best.Outcome;
            info.LastReindexUtc = best.RanOnUtc;
            info.LastReindexLocal = best.RanOnLocal;
        }

        /// <summary>
        /// Whether one reindex row should be reported instead of another: running first, then the
        /// later execution, then a failure over a completion.
        /// </summary>
        /// <param name="candidate">Row being considered.</param>
        /// <param name="incumbent">Row currently chosen.</param>
        private static bool Outranks(RebuildTaskRef candidate, RebuildTaskRef incumbent)
        {
            if (candidate.Outcome == "running")
            {
                return true;
            }

            if (incumbent.Outcome == "running")
            {
                return false;
            }

            var candidateStamp = candidate.RanOnUtc ?? string.Empty;
            var incumbentStamp = incumbent.RanOnUtc ?? string.Empty;
            var comparison = string.CompareOrdinal(candidateStamp, incumbentStamp);

            if (comparison != 0)
            {
                return comparison > 0;
            }

            return candidate.Outcome == "failed" && incumbent.Outcome != "failed";
        }

        /// <summary>
        /// Whether a reindex task names this index — either through the catalog name resolved from the
        /// task row, or through the task's own item name matching the index's admin title.
        /// </summary>
        /// <param name="rebuild">Reindex task reference.</param>
        /// <param name="pipe">Index pipe to test against.</param>
        private static bool Identifies(RebuildTaskRef rebuild, IndexPipeRef pipe)
        {
            // A task row names the index the way the ADMIN does ("Docs Index"); the pipe names it the
            // way a search query does ("docs-index"). Comparing the two literally matches nothing,
            // which is why a failed reindex used to leave its index reading "unknown". The display
            // name is therefore compared first, then everything falls back to a normalized form that
            // collapses case, spaces, dashes and underscores.
            if (SameName(rebuild.ItemName, pipe.UIName))
            {
                return true;
            }

            return SameName(rebuild.IndexName, pipe.CatalogName)
                || SameName(rebuild.IndexName, pipe.UIName)
                || SameName(rebuild.ItemName, pipe.CatalogName);
        }

        /// <summary>
        /// Whether two index names refer to the same index — exact first, then normalized, so
        /// <c>Docs Index</c>, <c>docs-index</c> and <c>docs_index</c> all match.
        /// </summary>
        /// <param name="left">First name.</param>
        /// <param name="right">Second name.</param>
        private static bool SameName(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedLeft = NormalizeName(left);

            return normalizedLeft.Length > 0 && normalizedLeft == NormalizeName(right);
        }

        /// <summary>
        /// Reduces an index name to comparable form: lower-cased letters and digits only, so the
        /// separator style a name happens to use stops mattering.
        /// </summary>
        /// <param name="name">Raw display or catalog name.</param>
        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(name.Length);

            foreach (var character in name)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Sitefinity has no "started at" column. <c>LastExecutedTime</c> is stamped when the scheduler
        /// picks a task up, so it is the best proxy; <c>ExecuteTime</c> (the due time) and
        /// <c>LastModified</c> stand in when it is absent. The column used is reported alongside the
        /// value rather than silently implied.
        /// </summary>
        /// <param name="task">Running task row.</param>
        /// <param name="source">Receives the name of the column the value came from.</param>
        private static DateTime ResolveStart(ScheduledTaskData task, out string source)
        {
            if (task.LastExecutedTime.HasValue && task.LastExecutedTime.Value != default(DateTime))
            {
                source = "LastExecutedTime";
                return AsUtc(task.LastExecutedTime.Value);
            }

            if (task.ExecuteTime != default(DateTime))
            {
                source = "ExecuteTime";
                return AsUtc(task.ExecuteTime);
            }

            source = "LastModified";
            return AsUtc(ReadDateTime(task, "LastModified"));
        }

        /// <summary>
        /// Whether a task row is a search index rebuild, judged on its CLR type name.
        /// </summary>
        /// <param name="task">Task row to classify.</param>
        private static bool IsRebuildTask(ScheduledTaskData task)
        {
            var typeName = (task.TaskName ?? string.Empty).ToLowerInvariant();

            foreach (var fragment in RebuildTaskFragments)
            {
                if (typeName.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds which configured index a rebuild task is working on by looking for a known catalog or
        /// admin name inside the row's identifying text. Returns null when nothing matches — better
        /// than guessing at an index name.
        /// </summary>
        /// <param name="task">Rebuild task row.</param>
        /// <param name="pipes">Configured search index pipes.</param>
        private static string MatchIndexName(ScheduledTaskData task, List<IndexPipeRef> pipes)
        {
            if (pipes == null || pipes.Count == 0)
            {
                return null;
            }

            var raw = (task.Key ?? string.Empty) + " " + (task.Title ?? string.Empty) + " " +
                (task.Description ?? string.Empty) + " " + ReadMemberAsString(task, "TaskData");

            var haystack = raw.ToLowerInvariant();

            // Also search a normalized copy, so a row saying "Docs Index" still matches the catalog
            // name "docs-index". Separators are dropped from both sides, not translated between them.
            var normalizedHaystack = NormalizeName(raw);

            foreach (var pipe in pipes)
            {
                if (Contains(haystack, normalizedHaystack, pipe.UIName) ||
                    Contains(haystack, normalizedHaystack, pipe.CatalogName))
                {
                    return pipe.CatalogName;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether a task row's text names an index, tested literally and then in normalized form.
        /// </summary>
        /// <param name="haystack">Lower-cased row text.</param>
        /// <param name="normalizedHaystack">The same text with separators stripped.</param>
        /// <param name="needle">Index name to look for.</param>
        private static bool Contains(string haystack, string normalizedHaystack, string needle)
        {
            if (string.IsNullOrWhiteSpace(needle))
            {
                return false;
            }

            if (haystack.IndexOf(needle.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            var normalizedNeedle = NormalizeName(needle);

            return normalizedNeedle.Length > 0 &&
                normalizedHaystack.IndexOf(normalizedNeedle, StringComparison.Ordinal) >= 0;
        }

        // ── Search indexes ───────────────────────────────────────────

        /// <summary>
        /// Enumerates every search-index pipe configured on a publishing point — what the Sitefinity
        /// admin's "Search indexes" screen lists.
        /// </summary>
        /// <param name="warnings">Warning sink.</param>
        private static List<IndexPipeRef> CollectIndexPipes(List<string> warnings)
        {
            List<string> scanned;

            return CollectIndexPipes(warnings, out scanned);
        }

        /// <summary>
        /// Enumerates every search-index pipe configured on a publishing point — what the Sitefinity
        /// admin's "Search indexes" screen lists.
        /// <para>
        /// <b>Search indexes do not live under the default publishing provider.</b> Sitefinity keeps
        /// them under their own provider, named by <c>PublishingConfig.SearchProviderName</c>
        /// (<c>SearchPublishingProvider</c>), while <c>PublishingManager.GetManager()</c> opens
        /// <c>OAPublishingProvider</c> — which is why a default-provider scan finds nothing on a site
        /// that plainly has indexes. Every configured provider is therefore queried, search provider
        /// first, and the providers actually scanned are reported so an empty list is diagnosable
        /// rather than merely wrong.
        /// </para>
        /// <para>
        /// Pipes are read from <c>GetPipeSettings()</c> directly rather than by walking publishing
        /// points, so an index whose point cannot be traversed is still found; the owning point is
        /// then read back off the pipe.
        /// </para>
        /// </summary>
        /// <param name="warnings">Warning sink.</param>
        /// <param name="providersScanned">Receives the provider names that were queried.</param>
        private static List<IndexPipeRef> CollectIndexPipes(List<string> warnings, out List<string> providersScanned)
        {
            var pipes = new List<IndexPipeRef>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            providersScanned = ResolvePublishingProviderNames(warnings);

            foreach (var providerName in providersScanned)
            {
                CollectIndexPipesFromProvider(providerName, pipes, seen, warnings);
            }

            return pipes;
        }

        /// <summary>
        /// Every publishing provider worth querying: the dedicated search provider first (that is where
        /// index pipes live), then the default, then anything else the publishing configuration
        /// declares. Falls back to the two well-known names when the configuration cannot be read.
        /// </summary>
        /// <param name="warnings">Warning sink.</param>
        private static List<string> ResolvePublishingProviderNames(List<string> warnings)
        {
            var names = new List<string>();

            AddProviderName(names, PublishingConfig.SearchProviderName);
            AddProviderName(names, PublishingConfig.DefaultProviderName);

            try
            {
                var config = Config.Get<PublishingConfig>();

                if (config != null && config.ProviderSettings != null)
                {
                    foreach (var settings in config.ProviderSettings.Values)
                    {
                        AddProviderName(names, ReadMemberAsString(settings, "Name"));
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add("Could not enumerate the publishing providers from configuration; scanning the " +
                    "well-known ones only: " + ex.Message);
            }

            return names;
        }

        private static void AddProviderName(List<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || names.Contains(name))
            {
                return;
            }

            names.Add(name);
        }

        /// <summary>
        /// Reads the search-index pipes held by one publishing provider, de-duplicating against pipes
        /// already found under another provider. A provider named in configuration but not present on
        /// this site is reported as a warning, not an error — the provider list is deliberately
        /// speculative so that no site's indexes are missed.
        /// </summary>
        /// <param name="providerName">Publishing provider to open.</param>
        /// <param name="pipes">Accumulating result list.</param>
        /// <param name="seen">De-duplication keys.</param>
        /// <param name="warnings">Warning sink.</param>
        private static void CollectIndexPipesFromProvider(
            string providerName, List<IndexPipeRef> pipes, HashSet<string> seen, List<string> warnings)
        {
            List<SearchIndexPipeSettings> indexPipes;

            try
            {
                var manager = PublishingManager.GetManager(providerName);
                TrySuppressSecurity(manager);

                // Query the pipes directly: an index whose publishing point cannot be walked is still
                // found this way, and the point is read back off the pipe.
                indexPipes = manager.GetPipeSettings()
                    .ToList()
                    .OfType<SearchIndexPipeSettings>()
                    .ToList();
            }
            catch (Exception ex)
            {
                warnings.Add("Publishing provider '" + providerName + "' could not be queried for search " +
                    "index pipes: " + ex.Message);
                return;
            }

            foreach (var indexPipe in indexPipes)
            {
                try
                {
                    string titleSource;
                    var point = indexPipe.PublishingPoint;
                    var pointName = point == null ? null : point.Name;
                    var key = providerName + "|" + indexPipe.CatalogName + "|" + pointName;

                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    pipes.Add(new IndexPipeRef
                    {
                        CatalogName = indexPipe.CatalogName,
                        UIName = ResolveDisplayName(indexPipe, point, pointName, out titleSource),
                        TitleSource = titleSource,
                        PointName = pointName,
                        ProviderName = providerName,
                        Backend = indexPipe.SearchProviderName,
                        IsActive = (point == null || point.IsActive) && indexPipe.IsActive,
                        LastPublicationUtc = point == null ? null : point.LastPublicationDate,
                        ContentSources = point == null ? new List<string>() : ReadContentSources(point),
                    });
                }
                catch (Exception ex)
                {
                    warnings.Add("Skipped a search index pipe that could not be read: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// The index's display name as the admin's "Search indexes" screen shows it ("Docs Index"),
        /// as opposed to the catalog name a search query targets ("docs-index"). The pipe's own
        /// <c>UIName</c> is preferred; a publishing point's localized title and then its name stand in,
        /// and the catalog name is the last resort so this is never blank.
        /// </summary>
        /// <param name="indexPipe">Search index pipe.</param>
        /// <param name="point">Owning publishing point, or null.</param>
        /// <param name="pointName">The point's name, already read.</param>
        /// <param name="source">Receives which candidate supplied the title, or <c>derived</c>.</param>
        private static string ResolveDisplayName(
            SearchIndexPipeSettings indexPipe, PublishingPoint point, string pointName, out string source)
        {
            string pointTitle = null;

            if (point != null)
            {
                try
                {
                    pointTitle = point.GetTitle(CultureInfo.CurrentUICulture);
                }
                catch (Exception)
                {
                    // Localized title unavailable — the point's name is a fine substitute.
                }
            }

            // Order matters: the publishing point carries the name a person chose, while the pipe's
            // UIName is the PIPE TYPE's label ("SearchIndexPipe") on at least some versions — worse
            // than no title at all, so it is checked last and type-like values are rejected outright.
            var candidates = new[]
            {
                new KeyValuePair<string, string>("PublishingPointTitle", pointTitle),
                new KeyValuePair<string, string>("PublishingPointName", pointName),
                new KeyValuePair<string, string>("PipeTitle", indexPipe.Title),
                new KeyValuePair<string, string>("PipeUIName", indexPipe.UIName),
            };

            var catalogKey = NormalizeName(indexPipe.CatalogName);

            foreach (var candidate in candidates)
            {
                if (!IsUsableDisplayName(candidate.Value, indexPipe))
                {
                    continue;
                }

                // A candidate that is just the catalog name again adds nothing; the derived form of it
                // reads better and is honestly labelled as derived.
                if (catalogKey.Length > 0 && NormalizeName(candidate.Value) == catalogKey)
                {
                    break;
                }

                source = candidate.Key;
                return candidate.Value;
            }

            source = "derived";
            return DeriveDisplayName(indexPipe.CatalogName);
        }

        /// <summary>
        /// Rejects a display-name candidate that is really a type or pipe-plumbing name. Emitting
        /// "SearchIndexPipe" as an index's title is worse than emitting nothing, because it looks like
        /// a real answer.
        /// </summary>
        /// <param name="candidate">Proposed display name.</param>
        /// <param name="indexPipe">Pipe the name came from.</param>
        private static bool IsUsableDisplayName(string candidate, SearchIndexPipeSettings indexPipe)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }

            var normalized = NormalizeName(candidate);

            if (normalized.Length == 0 || normalized.IndexOf("pipe", StringComparison.Ordinal) >= 0)
            {
                return false;
            }

            if (normalized == NormalizeName(indexPipe.GetType().Name))
            {
                return false;
            }

            return normalized != NormalizeName(ReadMemberAsString(indexPipe, "PipeName"));
        }

        /// <summary>
        /// Turns a catalog name into a readable title — <c>docs-index</c> becomes <c>Docs Index</c> —
        /// for the case where nothing on the pipe or its point names the index the way a person would.
        /// </summary>
        /// <param name="catalogName">Catalog name to humanize.</param>
        private static string DeriveDisplayName(string catalogName)
        {
            if (string.IsNullOrWhiteSpace(catalogName))
            {
                return null;
            }

            var spaced = catalogName.Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Trim();

            while (spaced.IndexOf("  ", StringComparison.Ordinal) >= 0)
            {
                spaced = spaced.Replace("  ", " ");
            }

            if (spaced.Length == 0)
            {
                return null;
            }

            // Title-case the lower-cased form so an all-lower catalog name reads naturally; a name that
            // already carries capitals keeps them, since ToTitleCase leaves upper-case words alone.
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());
        }

        /// <summary>
        /// Turns off provider-level security checks for a read. An MCP request carries no Sitefinity
        /// identity, so an un-suppressed provider can legitimately return nothing at all.
        /// </summary>
        /// <param name="manager">Manager whose provider should stop filtering by permissions.</param>
        private static void TrySuppressSecurity(object manager)
        {
            try
            {
                var providerProp = manager.GetType().GetProperty("Provider");
                var provider = providerProp == null ? null : providerProp.GetValue(manager, null);
                var suppress = provider == null
                    ? null
                    : provider.GetType().GetProperty("SuppressSecurityChecks");

                if (suppress != null && suppress.CanWrite)
                {
                    suppress.SetValue(provider, true, null);
                }
            }
            catch (Exception)
            {
                // Best-effort: the read is attempted either way.
            }
        }

        /// <summary>
        /// Names what feeds a publishing point: its inbound pipes, identified by whichever of the
        /// version-variant naming properties this Sitefinity exposes. Best-effort — an unreadable pipe
        /// is simply not named rather than failing the index entry.
        /// </summary>
        /// <param name="point">Publishing point owning the index pipe.</param>
        private static List<string> ReadContentSources(PublishingPoint point)
        {
            var sources = new List<string>();

            try
            {
                foreach (var settings in point.PipeSettings)
                {
                    if (settings == null || !settings.IsInbound)
                    {
                        continue;
                    }

                    var name = FirstNonEmpty(
                        ReadMemberAsString(settings, "ItemType"),
                        ReadMemberAsString(settings, "ContentType"),
                        ReadMemberAsString(settings, "TypeName"),
                        settings.Title,
                        settings.PipeName);

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var cleaned = Clean(name);

                    if (!sources.Contains(cleaned))
                    {
                        sources.Add(cleaned);
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort: an index entry without its content sources is still useful.
            }

            return sources;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>
        /// Fills the last-updated fields, preferring the on-disk catalogue folder's write time (the
        /// truest signal that indexing actually happened) and falling back to the publishing point's
        /// last publication date.
        /// </summary>
        /// <param name="info">Index entry being populated.</param>
        /// <param name="pipe">Source pipe reference.</param>
        /// <param name="searchService">Resolved search service, or null.</param>
        /// <param name="warnings">Warning sink.</param>
        private static void ApplyLastUpdated(
            McpSearchIndexInfo info, IndexPipeRef pipe, object searchService, List<string> warnings)
        {
            var folderTime = TryIndexFolderTime(searchService, pipe.CatalogName, warnings);

            if (folderTime.HasValue)
            {
                info.LastUpdatedUtc = FormatUtc(folderTime.Value);
                info.LastUpdatedLocal = FormatLocal(ToLocal(folderTime.Value));
                info.LastUpdatedSource = "IndexFolder";
                return;
            }

            if (pipe.LastPublicationUtc.HasValue && pipe.LastPublicationUtc.Value != default(DateTime))
            {
                var utc = AsUtc(pipe.LastPublicationUtc.Value);
                info.LastUpdatedUtc = FormatUtc(utc);
                info.LastUpdatedLocal = FormatLocal(ToLocal(utc));
                info.LastUpdatedSource = "LastPublicationDate";
            }
        }

        /// <summary>
        /// Resolves Sitefinity's <c>ISearchService</c> through <c>ServiceBus.ResolveService</c>. Both
        /// types are looked up by name so this file compiles against any Sitefinity version, including
        /// one where the search implementation assembly is absent entirely.
        /// </summary>
        /// <param name="warnings">Warning sink.</param>
        private static object ResolveSearchService(List<string> warnings)
        {
            var serviceInterface = FindType("Telerik.Sitefinity.Services.Search.ISearchService");

            if (serviceInterface == null)
            {
                warnings.Add("ISearchService type not found — this Sitefinity version may not ship the search " +
                    "service, so index existence and freshness cannot be reported.");
                return null;
            }

            var serviceBus = FindType("Telerik.Sitefinity.Services.ServiceBus");

            if (serviceBus == null)
            {
                warnings.Add("ServiceBus type not found — index existence and freshness cannot be reported.");
                return null;
            }

            try
            {
                var resolve = serviceBus
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "ResolveService" && m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 0);

                if (resolve == null)
                {
                    warnings.Add("ServiceBus.ResolveService was not found on this Sitefinity version.");
                    return null;
                }

                return resolve.MakeGenericMethod(serviceInterface).Invoke(null, null);
            }
            catch (Exception ex)
            {
                warnings.Add("Could not resolve the search service: " + (ex.InnerException ?? ex).Message);
                return null;
            }
        }

        /// <summary>
        /// Follows the <c>InnerService</c> chain to the concrete search service behind Sitefinity's
        /// decorators, so the reported backend names something real and the probes below can reach
        /// members the decorator does not forward. Bounded, and never returns null for a non-null
        /// input.
        /// </summary>
        /// <param name="searchService">Possibly decorated search service.</param>
        private static object UnwrapSearchService(object searchService)
        {
            var serviceInterface = FindType("Telerik.Sitefinity.Services.Search.ISearchService");
            var current = searchService;

            for (var depth = 0; depth < 5 && current != null; depth++)
            {
                var inner = FindWrappedSearchService(current, serviceInterface);

                if (inner == null)
                {
                    break;
                }

                current = inner;
            }

            return current;
        }

        /// <summary>
        /// Finds the search service a decorator is wrapping by scanning its own properties and fields —
        /// public and non-public — for the first member holding a different object that is itself a
        /// search service. The wrapped member is not named consistently across versions, so it is
        /// identified by TYPE rather than by name.
        /// </summary>
        /// <param name="target">Decorator to look inside.</param>
        /// <param name="serviceInterface">The <c>ISearchService</c> type, or null when unavailable.</param>
        private static object FindWrappedSearchService(object target, Type serviceInterface)
        {
            const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            try
            {
                foreach (var property in target.GetType().GetProperties(Flags))
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    object value;

                    try
                    {
                        value = property.GetValue(target, null);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (IsWrappedSearchService(value, target, serviceInterface))
                    {
                        return value;
                    }
                }

                foreach (var field in target.GetType().GetFields(Flags))
                {
                    object value;

                    try
                    {
                        value = field.GetValue(target);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (IsWrappedSearchService(value, target, serviceInterface))
                    {
                        return value;
                    }
                }
            }
            catch (Exception)
            {
                // Reflection over an unfamiliar decorator failed — treat the backend as unreachable.
            }

            return null;
        }

        private static bool IsWrappedSearchService(object value, object target, Type serviceInterface)
        {
            if (value == null || ReferenceEquals(value, target) || value.GetType() == target.GetType())
            {
                return false;
            }

            if (serviceInterface != null)
            {
                return serviceInterface.IsInstanceOfType(value);
            }

            return value.GetType().Name.IndexOf("SearchService", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Whether a resolved service is still a wrapper rather than the concrete backend. A decorator
        /// answers the search interface but exposes none of the backend's own members, so a count or a
        /// catalogue path cannot be obtained through it.
        /// </summary>
        /// <param name="service">Service to classify.</param>
        private static bool IsDecorator(object service)
        {
            if (service == null)
            {
                return true;
            }

            var name = service.GetType().Name;

            return name.IndexOf("Decorator", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Guarded", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Asks the backend whether an index exists. Null when the service could not be resolved or the
        /// call failed — an unknown answer is not the same as "missing".
        /// </summary>
        /// <param name="searchService">Resolved search service, or null.</param>
        /// <param name="catalogName">Index catalog name.</param>
        private static bool? TryIndexExists(object searchService, string catalogName)
        {
            if (searchService == null || string.IsNullOrWhiteSpace(catalogName))
            {
                return null;
            }

            try
            {
                var method = searchService.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "IndexExists" && m.GetParameters().Length == 1);

                if (method == null)
                {
                    return null;
                }

                return method.Invoke(searchService, new object[] { catalogName }) as bool?;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Best-effort document count. Sitefinity's public search interface has no portable count call,
        /// so this probes for one on the concrete backend and returns null when there is none —
        /// deliberately without running a search, which on a large index would be expensive.
        /// </summary>
        /// <param name="searchService">Resolved search service, or null.</param>
        /// <param name="catalogName">Index catalog name.</param>
        private static long? TryDocumentCount(object searchService, string catalogName)
        {
            if (searchService == null || string.IsNullOrWhiteSpace(catalogName))
            {
                return null;
            }

            var candidates = new[] { "GetDocumentCount", "DocumentCount", "GetIndexDocumentCount", "GetDocumentsCount" };

            foreach (var name in candidates)
            {
                try
                {
                    var method = searchService.GetType()
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == 1 &&
                            m.GetParameters()[0].ParameterType == typeof(string));

                    if (method == null)
                    {
                        continue;
                    }

                    var value = method.Invoke(searchService, new object[] { catalogName });

                    if (value == null)
                    {
                        continue;
                    }

                    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
                }
                catch (Exception)
                {
                    // Try the next candidate name.
                }
            }

            return null;
        }

        /// <summary>
        /// Last write time of the on-disk index folder, for file-backed backends that expose
        /// <c>GetCataloguePath</c> (Lucene). Null for hosted backends, which have no local folder.
        /// </summary>
        /// <param name="searchService">Resolved search service, or null.</param>
        /// <param name="catalogName">Index catalog name.</param>
        /// <param name="warnings">Warning sink.</param>
        private static DateTime? TryIndexFolderTime(object searchService, string catalogName, List<string> warnings)
        {
            if (searchService == null || string.IsNullOrWhiteSpace(catalogName))
            {
                return null;
            }

            try
            {
                var method = searchService.GetType()
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetCataloguePath" && m.GetParameters().Length == 1);

                if (method == null)
                {
                    return null;
                }

                var path = method.Invoke(searchService, new object[] { catalogName }) as string;

                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    return null;
                }

                var newest = Directory.GetLastWriteTimeUtc(path);

                foreach (var file in Directory.GetFiles(path))
                {
                    var stamp = File.GetLastWriteTimeUtc(file);

                    if (stamp > newest)
                    {
                        newest = stamp;
                    }
                }

                return DateTime.SpecifyKind(newest, DateTimeKind.Utc);
            }
            catch (UnauthorizedAccessException ex)
            {
                warnings.Add("Could not read the index folder for '" + catalogName + "' (" + ex.Message +
                    "). Grant the application pool identity read access to the Sitefinity search index folder " +
                    "to report index freshness.");
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ── Formatting and reflection helpers ────────────────────────

        /// <summary>Formats an instant as <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
        /// <param name="utc">UTC instant.</param>
        private static string FormatUtc(DateTime utc)
        {
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats a server-local wall time as <c>yyyy-MM-ddTHH:mm:ss</c> — deliberately with NO zone
        /// suffix, because it is a clock reading rather than an instant. The response's
        /// <c>ServerTimeZoneId</c> and <c>ServerUtcOffsetMinutes</c> say which clock.
        /// </summary>
        /// <param name="local">Server-local wall time.</param>
        private static string FormatLocal(DateTime local)
        {
            return local.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>Converts a UTC instant to the server's local wall time.</summary>
        /// <param name="utc">UTC instant.</param>
        private static DateTime ToLocal(DateTime utc)
        {
            var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            return DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(asUtc, TimeZoneInfo.Local), DateTimeKind.Unspecified);
        }

        /// <summary>
        /// Normalizes a persisted Sitefinity timestamp to UTC. Scheduling and publishing rows are
        /// written in UTC, but an <c>Unspecified</c> kind is common; a value that genuinely arrives as
        /// <c>Local</c> is converted rather than mislabelled.
        /// </summary>
        /// <param name="value">Persisted timestamp.</param>
        private static DateTime AsUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Local)
            {
                return value.ToUniversalTime();
            }

            return DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        /// <summary>
        /// Redacts and truncates one outbound string. A blank value comes back as <c>null</c>, not an
        /// empty string, so "this row has no value here" is distinguishable from "the value was
        /// blanked out" — the redactor itself normalizes null to empty.
        /// </summary>
        /// <param name="value">Raw value.</param>
        private static string Clean(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return McpSecretRedactor.Redact(Truncate(value, MaxTextLength));
        }

        private static string BuildTitle(ScheduledTaskData task)
        {
            var title = !string.IsNullOrWhiteSpace(task.Title) ? task.Title : task.TaskName;

            return Clean(title);
        }

        /// <summary>
        /// The task's own title when it names something other than its type — for a ReindexTask that is
        /// the search index name. Null when the row carries no title of its own.
        /// </summary>
        /// <param name="task">Task row.</param>
        private static string BuildItemName(ScheduledTaskData task)
        {
            if (string.IsNullOrWhiteSpace(task.Title) ||
                string.Equals(task.Title, task.TaskName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return Clean(task.Title);
        }

        private static string ReadId(ScheduledTaskData task)
        {
            try
            {
                return task.Id == Guid.Empty ? null : task.Id.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max)
            {
                return value;
            }

            return value.Substring(0, max) + "...";
        }

        /// <summary>
        /// Reads a property that only newer Sitefinity versions declare, as a display string. Reading
        /// these reflectively keeps this file compiling inside older Sitefinity solutions.
        /// </summary>
        /// <param name="target">Object to read from.</param>
        /// <param name="name">Property name.</param>
        private static string ReadMemberAsString(object target, string name)
        {
            var value = ReadMember(target, name);

            return value == null ? null : value.ToString();
        }

        private static int? ReadNullableInt(object target, string name)
        {
            var value = ReadMember(target, name);

            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static DateTime ReadDateTime(object target, string name)
        {
            var value = ReadMember(target, name);

            return value is DateTime ? (DateTime)value : DateTime.UtcNow;
        }

        private static object ReadMember(object target, string name)
        {
            try
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

                return prop == null ? null : prop.GetValue(target, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;

                try
                {
                    type = asm.GetType(fullName, false);
                }
                catch (Exception)
                {
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        /// <summary>One configured search-index pipe, flattened out of its publishing point.</summary>
        private sealed class IndexPipeRef
        {
            public string CatalogName { get; set; }

            public string UIName { get; set; }

            public string PointName { get; set; }

            public string ProviderName { get; set; }

            public string TitleSource { get; set; }

            public string Backend { get; set; }

            public bool IsActive { get; set; }

            public DateTime? LastPublicationUtc { get; set; }

            public List<string> ContentSources { get; set; } = new List<string>();
        }

        /// <summary>A reindex task found in the scheduler — running, failed, or recently completed.</summary>
        private sealed class RebuildTaskRef
        {
            public string IndexName { get; set; }

            public string ItemName { get; set; }

            public string TaskType { get; set; }

            /// <summary><c>running</c>, <c>failed</c> or <c>completed</c>.</summary>
            public string Outcome { get; set; }

            public int? Progress { get; set; }

            /// <summary>ISO 8601 UTC the row started (running) or last executed.</summary>
            public string RanOnUtc { get; set; }

            /// <summary>Server-local wall time the row started or last executed.</summary>
            public string RanOnLocal { get; set; }
        }
    }
}

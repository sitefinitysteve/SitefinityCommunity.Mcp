using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for reading and searching Sitefinity log files. The concrete log provider
/// (filesystem vs HTTP) is picked per-environment via <see cref="ILogProviderFactory"/>.
/// Parsed entries come from <see cref="LogParsingService"/>.
/// <para>
/// Three tools, deliberately: read the newest entries of one file, search across files, list what
/// files exist. Reading Error.log and reading "the last error" were once separate tools; they are now
/// <c>sitefinity_read_log_file</c> with a default file name and a count, because near-duplicate tools
/// cost the agent a choice on every call without giving it anything new.
/// </para>
/// </summary>
[McpServerToolType]
public sealed class LogTools
{
    private readonly ILogProviderFactory _logProviderFactory;
    private readonly LogParsingService _logParser;

    public LogTools(ILogProviderFactory logProviderFactory, LogParsingService logParser)
    {
        this._logProviderFactory = logProviderFactory;
        this._logParser = logParser;
    }

    [McpServerTool(Name = "sitefinity_list_log_files", Title = "List Log Files", ReadOnly = true)]
    [Description("List all Sitefinity log files with their size and last modified date.")]
    public async Task<string> ListLogFiles(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var provider = this._logProviderFactory.Create(environment);
            var files = await provider.ListFilesAsync(ct);

            if (files.Count == 0)
            {
                return "No log files found.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {files.Count} log file(s):");
            sb.AppendLine();

            foreach (var file in files)
            {
                sb.AppendLine($"  {file}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing log files: {ex.Message}";
        }
    }

    [McpServerTool(Name = "sitefinity_read_log_file", Title = "Read Log File", ReadOnly = true)]
    [Description("Read the most recent entries from a Sitefinity log file — THE tool for \"check the error log\", " +
                 "\"what are the latest errors\", or \"show me the last exception\". fileName defaults to " +
                 "\"Error.log\", so calling it with no arguments returns the last 10 errors, each parsed into " +
                 "timestamp, severity, type, message, requested URL, and stack trace. Pass count: 1 for just the " +
                 "most recent entry with its full stack trace. Other common targets are \"Trace.log\" (general " +
                 "runtime trace) and any name from sitefinity_list_log_files. To find a specific error across all " +
                 "files rather than reading the newest ones, use sitefinity_search_logs; to work out what happened " +
                 "at a particular time or during an outage, use sitefinity_investigate_incident.")]
    public async Task<string> ReadLogFile(
        [Description("Name of the log file. Defaults to 'Error.log'; 'Trace.log' and any name from sitefinity_list_log_files also work.")] string fileName = "Error.log",
        [Description("Number of entries to return (default: 10, max: 50). Use 1 for the single most recent entry.")] int count = 10,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 50);
        return await ReadLogByName(fileName, count, environment, ct);
    }

    [McpServerTool(Name = "sitefinity_search_logs", Title = "Search Logs", ReadOnly = true)]
    [Description("Search Sitefinity log files using a regex pattern. Returns matching lines with surrounding context. Searches newest files first and stops after maxMatches hits, so it stays fast on large prod logs. To narrow a slow search, set fileName (e.g. 'Error.log') to search a single file instead of the whole rolled set. This searches ONLY Sitefinity's own .log files — to search the Windows event log, IIS access log, or HTTPERR (e.g. 'look for crashes in the event log'), use sitefinity_investigate_incident instead: no arguments for crash discovery, or query + sources for a targeted sweep.")]
    public async Task<string> SearchLogs(
        [Description("Regex pattern to search for (e.g., 'NullReference', 'timeout.*sql')")] string pattern,
        [Description("Number of context lines before and after each match (default: 2)")] int contextLines = 2,
        [Description("Whether the search is case-sensitive (default: false)")] bool caseSensitive = false,
        [Description("Restrict the search to a single log file by name (e.g. 'Error.log'). When omitted, searches all *.log files newest-first.")] string? fileName = null,
        [Description("Maximum number of matches to return before stopping (default: 200, max: 1000). Lower this or set fileName if a search is slow.")] int maxMatches = 0,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            contextLines = Math.Clamp(contextLines, 0, 10);
            var provider = this._logProviderFactory.Create(environment);
            var results = await provider.SearchAsync(pattern, contextLines, caseSensitive, fileName, maxMatches, ct);

            if (results.Count == 0)
            {
                return $"No matches found for pattern: {pattern}";
            }

            var effectiveCap = maxMatches > 0 ? Math.Min(maxMatches, 1000) : 200;
            var wasCapped = results.Count >= effectiveCap;

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} match(es) for '{pattern}'{(fileName != null ? $" in {fileName}" : "")}:");

            if (wasCapped)
            {
                sb.AppendLine($"(Stopped at the {effectiveCap}-match cap — there may be more. Refine the pattern, set fileName, or raise maxMatches to see others.)");
            }

            sb.AppendLine();

            foreach (var result in results.Take(100)) // Cap output at 100 matches
            {
                sb.AppendLine(result.ToString());
                sb.AppendLine();
            }

            if (results.Count > 100)
            {
                sb.AppendLine($"... and {results.Count - 100} more matches (showing first 100)");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching logs: {ex.Message}";
        }
    }

    private async Task<string> ReadLogByName(string fileName, int count, string? environment, CancellationToken ct)
    {
        try
        {
            var provider = this._logProviderFactory.Create(environment);
            var content = await provider.ReadFileAsync(fileName, ct);

            var entries = this._logParser.GetLastEntries(content, count);
            if (entries.Count == 0)
            {
                return $"No entries found in {fileName}.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Last {entries.Count} entries from {fileName}:");
            sb.AppendLine();

            for (var i = 0; i < entries.Count; i++)
            {
                sb.AppendLine($"--- Entry {i + 1}/{entries.Count} ---");
                sb.AppendLine(entries[i].ToString());
                sb.AppendLine();
            }

            return sb.ToString();
        }
        catch (FileNotFoundException)
        {
            return $"{fileName} not found.";
        }
        catch (Exception ex)
        {
            return $"Error reading {fileName}: {ex.Message}";
        }
    }
}

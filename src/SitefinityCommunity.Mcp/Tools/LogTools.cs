using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// MCP tools for reading and searching Sitefinity log files. The concrete log provider
/// (filesystem vs HTTP) is picked per-environment via <see cref="ILogProviderFactory"/>.
/// Parsed entries come from <see cref="LogParsingService"/>.
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

    [McpServerTool(Name = "sitefinity_read_error_log", ReadOnly = true)]
    [Description("Read the last N entries from Sitefinity's Error.log. Returns parsed error entries with timestamp, message, severity, stack trace, and requested URL.")]
    public async Task<string> ReadErrorLog(
        [Description("Number of entries to return (default: 10, max: 50)")] int count = 10,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 50);
        return await ReadLogByName("Error.log", count, environment, ct);
    }

    [McpServerTool(Name = "sitefinity_read_trace_log", ReadOnly = true)]
    [Description("Read the last N entries from Sitefinity's Trace.log. Returns parsed trace entries with timestamp and message.")]
    public async Task<string> ReadTraceLog(
        [Description("Number of entries to return (default: 10, max: 50)")] int count = 10,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 50);
        return await ReadLogByName("Trace.log", count, environment, ct);
    }

    [McpServerTool(Name = "sitefinity_list_log_files", ReadOnly = true)]
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

    [McpServerTool(Name = "sitefinity_read_log_file", ReadOnly = true)]
    [Description("Read any Sitefinity log file by name. Returns the last N parsed entries from the specified file.")]
    public async Task<string> ReadLogFile(
        [Description("Name of the log file (e.g., 'Error.log', 'Trace.log')")] string fileName,
        [Description("Number of entries to return (default: 10, max: 50)")] int count = 10,
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        count = Math.Clamp(count, 1, 50);
        return await ReadLogByName(fileName, count, environment, ct);
    }

    [McpServerTool(Name = "sitefinity_search_logs", ReadOnly = true)]
    [Description("Search Sitefinity log files using a regex pattern. Returns matching lines with surrounding context. Searches newest files first and stops after maxMatches hits, so it stays fast on large prod logs. To narrow a slow search, set fileName (e.g. 'Error.log') to search a single file instead of the whole rolled set.")]
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

    [McpServerTool(Name = "sitefinity_get_last_error", ReadOnly = true)]
    [Description("Get the most recent error from Sitefinity's Error.log with full details including stack trace.")]
    public async Task<string> GetLastError(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            var provider = this._logProviderFactory.Create(environment);
            var content = await provider.ReadFileAsync("Error.log", ct);

            var entries = this._logParser.GetLastEntries(content, 1);
            if (entries.Count == 0)
            {
                return "No errors found in Error.log.";
            }

            var entry = entries[0];
            var sb = new StringBuilder();
            sb.AppendLine("=== Most Recent Error ===");
            sb.AppendLine();

            if (entry.Timestamp.HasValue)
            {
                sb.AppendLine($"Time:     {entry.Timestamp:yyyy-MM-dd HH:mm:ss}");
            }

            if (!string.IsNullOrEmpty(entry.Severity))
            {
                sb.AppendLine($"Severity: {entry.Severity}");
            }

            if (!string.IsNullOrEmpty(entry.Type))
            {
                sb.AppendLine($"Type:     {entry.Type}");
            }

            sb.AppendLine($"Message:  {entry.Message}");

            if (!string.IsNullOrEmpty(entry.RequestedUrl))
            {
                sb.AppendLine($"URL:      {entry.RequestedUrl}");
            }

            if (!string.IsNullOrEmpty(entry.MachineName))
            {
                sb.AppendLine($"Machine:  {entry.MachineName}");
            }

            if (!string.IsNullOrEmpty(entry.StackTrace))
            {
                sb.AppendLine();
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(entry.StackTrace);
            }

            return sb.ToString();
        }
        catch (FileNotFoundException)
        {
            return "Error.log not found. Sitefinity may not have logged any errors yet.";
        }
        catch (Exception ex)
        {
            return $"Error reading last error: {ex.Message}";
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

using System.Globalization;
using System.Text.RegularExpressions;
using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Parses Sitefinity's standard log format where entries are delimited by 40-dash separators.
/// </summary>
public sealed partial class LogParsingService
{
    private const string Separator = "----------------------------------------";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    // Pre-compiled regexes for log field extraction
    private static readonly Regex TimestampRegex = CreateTimestampRegex();
    private static readonly Regex SeverityRegex = CreateSeverityRegex();
    private static readonly Regex TypeRegex = CreateTypeRegex();
    private static readonly Regex ActivityIdRegex = CreateActivityIdRegex();
    private static readonly Regex UrlRegex = CreateUrlRegex();
    private static readonly Regex MachineRegex = CreateMachineRegex();

    [GeneratedRegex(@"Timestamp:\s*(.+?)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CreateTimestampRegex();

    [GeneratedRegex(@"Severity:\s*(\w+)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CreateSeverityRegex();

    [GeneratedRegex(@"Type:\s*(.+?)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CreateTypeRegex();

    [GeneratedRegex(@"Activity\s*Id:\s*(.+?)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CreateActivityIdRegex();

    [GeneratedRegex(@"Requested URL:\s*(.+?)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CreateUrlRegex();

    [GeneratedRegex(@"Machine Name:\s*(.+?)$", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
    private static partial Regex CreateMachineRegex();

    /// <summary>
    /// Parses all log entries from raw log text.
    /// </summary>
    public IReadOnlyList<LogEntry> ParseEntries(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<LogEntry>();

        var blocks = content.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
        var entries = new List<LogEntry>();

        foreach (var block in blocks)
        {
            var trimmed = block.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            entries.Add(ParseBlock(trimmed));
        }

        return entries;
    }

    /// <summary>
    /// Gets the last N entries from raw log text. Reads from the end for efficiency.
    /// </summary>
    public IReadOnlyList<LogEntry> GetLastEntries(string content, int count)
    {
        var allEntries = ParseEntries(content);

        return allEntries
            .Skip(Math.Max(0, allEntries.Count - count))
            .ToList();
    }

    private static LogEntry ParseBlock(string block)
    {
        var entry = new LogEntry { RawText = block };

        // Extract structured fields
        var timestampMatch = TimestampRegex.Match(block);
        if (timestampMatch.Success)
        {
            if (DateTime.TryParse(timestampMatch.Groups[1].Value.Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var ts))
            {
                entry.Timestamp = ts;
            }
        }

        var severityMatch = SeverityRegex.Match(block);
        if (severityMatch.Success)
            entry.Severity = severityMatch.Groups[1].Value;

        var typeMatch = TypeRegex.Match(block);
        if (typeMatch.Success)
            entry.Type = typeMatch.Groups[1].Value.Trim();

        var activityMatch = ActivityIdRegex.Match(block);
        if (activityMatch.Success)
            entry.ActivityId = activityMatch.Groups[1].Value.Trim();

        var urlMatch = UrlRegex.Match(block);
        if (urlMatch.Success)
            entry.RequestedUrl = urlMatch.Groups[1].Value.Trim();

        var machineMatch = MachineRegex.Match(block);
        if (machineMatch.Success)
            entry.MachineName = machineMatch.Groups[1].Value.Trim();

        // Extract message: first line of the block that isn't a known field
        entry.Message = ExtractMessage(block);

        // Extract stack trace if present
        entry.StackTrace = ExtractStackTrace(block);

        return entry;
    }

    private static string ExtractMessage(string block)
    {
        var lines = block.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Skip known structured field lines
            if (trimmed.StartsWith("Timestamp:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Severity:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Type:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Activity Id:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Requested URL:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Machine Name:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("Message:", StringComparison.OrdinalIgnoreCase))
            {
                // If it's a "Message:" line, return the value portion
                if (trimmed.StartsWith("Message:", StringComparison.OrdinalIgnoreCase))
                    return trimmed["Message:".Length..].Trim();

                continue;
            }

            return trimmed;
        }

        return block.Split('\n').FirstOrDefault()?.Trim() ?? block;
    }

    private static string? ExtractStackTrace(string block)
    {
        // Look for lines starting with "   at " which is the .NET stack trace format
        var lines = block.Split('\n');
        var stackLines = new List<string>();
        var inStack = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (trimmed.TrimStart().StartsWith("at ") || trimmed.Contains("   at "))
            {
                inStack = true;
                stackLines.Add(trimmed);
            }
            else if (inStack && (trimmed.StartsWith("---") || string.IsNullOrWhiteSpace(trimmed)))
            {
                stackLines.Add(trimmed);
            }
            else if (inStack)
            {
                break;
            }
        }

        return stackLines.Count > 0 ? string.Join("\n", stackLines) : null;
    }
}

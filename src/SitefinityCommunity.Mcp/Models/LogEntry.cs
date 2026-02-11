namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A parsed Sitefinity log entry, delimited by 40-dash separators.
/// </summary>
public sealed class LogEntry
{
    public string? ActivityId { get; set; }
    public DateTime? Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Severity { get; set; }
    public string? StackTrace { get; set; }
    public string? RequestedUrl { get; set; }
    public string? MachineName { get; set; }
    public string RawText { get; set; } = string.Empty;

    public override string ToString()
    {
        var parts = new List<string>();

        if (this.Timestamp.HasValue)
            parts.Add($"[{this.Timestamp:yyyy-MM-dd HH:mm:ss}]");

        if (!string.IsNullOrEmpty(this.Severity))
            parts.Add($"[{this.Severity}]");

        if (!string.IsNullOrEmpty(this.Type))
            parts.Add($"({this.Type})");

        parts.Add(this.Message);

        if (!string.IsNullOrEmpty(this.RequestedUrl))
            parts.Add($"\n  URL: {this.RequestedUrl}");

        if (!string.IsNullOrEmpty(this.StackTrace))
            parts.Add($"\n  Stack: {this.StackTrace}");

        return string.Join(" ", parts);
    }
}

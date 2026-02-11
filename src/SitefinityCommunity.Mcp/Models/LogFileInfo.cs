namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// Metadata about a log file (name, size, last modified).
/// </summary>
public sealed class LogFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }

    public string FormattedSize => this.SizeBytes switch
    {
        < 1024 => $"{this.SizeBytes} B",
        < 1024 * 1024 => $"{this.SizeBytes / 1024.0:F1} KB",
        _ => $"{this.SizeBytes / (1024.0 * 1024.0):F1} MB"
    };

    public override string ToString() =>
        $"{this.FileName} ({this.FormattedSize}, modified {this.LastModified:yyyy-MM-dd HH:mm:ss})";
}

namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A single search match found in a log file.
/// </summary>
public sealed class LogSearchResult
{
    public string FileName { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string MatchedLine { get; set; } = string.Empty;
    public List<string> ContextBefore { get; set; } = new();
    public List<string> ContextAfter { get; set; } = new();

    public override string ToString()
    {
        var lines = new List<string>();

        foreach (var before in this.ContextBefore)
            lines.Add($"  {before}");

        lines.Add($"► {this.FileName}:{this.LineNumber} | {this.MatchedLine}");

        foreach (var after in this.ContextAfter)
            lines.Add($"  {after}");

        return string.Join("\n", lines);
    }
}

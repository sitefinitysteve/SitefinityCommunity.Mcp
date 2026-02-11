using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Abstract interface over local filesystem and remote HTTP log access.
/// </summary>
public interface ILogProvider
{
    Task<IReadOnlyList<LogFileInfo>> ListFilesAsync(CancellationToken ct = default);
    Task<string> ReadFileAsync(string fileName, CancellationToken ct = default);
    Task<IReadOnlyList<LogSearchResult>> SearchAsync(string pattern, int contextLines, bool caseSensitive, CancellationToken ct = default);
}

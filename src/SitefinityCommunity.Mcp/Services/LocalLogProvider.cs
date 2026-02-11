using System.Text.RegularExpressions;
using SitefinityCommunity.Mcp.Models;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Reads log files directly from the local filesystem.
/// Used when logsPath is configured for the environment (same machine as Sitefinity).
/// </summary>
public sealed class LocalLogProvider : ILogProvider
{
    private readonly string _logsPath;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    public LocalLogProvider(string logsPath)
    {
        this._logsPath = logsPath;

        if (!Directory.Exists(logsPath))
        {
            throw new DirectoryNotFoundException(
                $"Logs directory not found: {logsPath}. Check the 'logsPath' in your config.");
        }
    }

    public Task<IReadOnlyList<LogFileInfo>> ListFilesAsync(CancellationToken ct = default)
    {
        var dir = new DirectoryInfo(this._logsPath);
        var files = dir.GetFiles("*.log")
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => new LogFileInfo
            {
                FileName = f.Name,
                SizeBytes = f.Length,
                LastModified = f.LastWriteTime
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<LogFileInfo>>(files);
    }

    public async Task<string> ReadFileAsync(string fileName, CancellationToken ct = default)
    {
        ValidateFileName(fileName);
        var filePath = Path.Combine(this._logsPath, fileName);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Log file not found: {fileName}");

        try
        {
            // Use FileShare.ReadWrite since Sitefinity may be writing concurrently
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (IOException ex)
        {
            throw new IOException($"Could not read log file '{fileName}': {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<LogSearchResult>> SearchAsync(
        string pattern, int contextLines, bool caseSensitive, CancellationToken ct = default)
    {
        var dir = new DirectoryInfo(this._logsPath);
        var logFiles = dir.GetFiles("*.log");

        var regexOptions = RegexOptions.Compiled;
        if (!caseSensitive)
            regexOptions |= RegexOptions.IgnoreCase;

        var regex = new Regex(pattern, regexOptions, RegexTimeout);

        // Search files in parallel
        var tasks = logFiles.Select(file => Task.Run(() => SearchFile(file, regex, contextLines, ct), ct));
        var results = await Task.WhenAll(tasks);

        return results
            .SelectMany(r => r)
            .OrderByDescending(r => r.FileName)
            .ThenBy(r => r.LineNumber)
            .ToList();
    }

    private static List<LogSearchResult> SearchFile(
        FileInfo file, Regex regex, int contextLines, CancellationToken ct)
    {
        var results = new List<LogSearchResult>();

        try
        {
            // FileShare.ReadWrite for concurrent access
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var allLines = new List<string>();

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                ct.ThrowIfCancellationRequested();
                allLines.Add(line);
            }

            for (var i = 0; i < allLines.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (!regex.IsMatch(allLines[i]))
                    continue;

                var result = new LogSearchResult
                {
                    FileName = file.Name,
                    LineNumber = i + 1,
                    MatchedLine = allLines[i]
                };

                // Context before
                for (var j = Math.Max(0, i - contextLines); j < i; j++)
                    result.ContextBefore.Add(allLines[j]);

                // Context after
                for (var j = i + 1; j <= Math.Min(allLines.Count - 1, i + contextLines); j++)
                    result.ContextAfter.Add(allLines[j]);

                results.Add(result);
            }
        }
        catch (IOException)
        {
            // File may be locked by Sitefinity — skip it
        }

        return results;
    }

    /// <summary>
    /// Prevents path traversal attacks by rejecting filenames with directory separators or relative paths.
    /// </summary>
    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name cannot be empty.");

        if (fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            Path.IsPathRooted(fileName))
        {
            throw new ArgumentException($"Invalid file name: {fileName}. Path traversal is not allowed.");
        }
    }
}

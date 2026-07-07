using System.Text.RegularExpressions;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Security;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Reads log files directly from the local filesystem.
/// Used when logsPath is configured for the environment (same machine as Sitefinity).
/// </summary>
public sealed class LocalLogProvider : ILogProvider
{
    private readonly string _logsPath;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Default cap on total matches when the caller does not specify one.</summary>
    public const int DefaultMaxMatches = 200;

    /// <summary>Hard ceiling on matches, even when the caller asks for more.</summary>
    private const int MaxMatchesCeiling = 1000;

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
        {
            throw new FileNotFoundException($"Log file not found: {fileName}");
        }

        try
        {
            // Use FileShare.ReadWrite since Sitefinity may be writing concurrently
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync(ct);
            return SecretRedactor.Redact(content);
        }
        catch (IOException ex)
        {
            throw new IOException($"Could not read log file '{fileName}': {ex.Message}", ex);
        }
    }

    public Task<IReadOnlyList<LogSearchResult>> SearchAsync(
        string pattern,
        int contextLines,
        bool caseSensitive,
        string? fileName = null,
        int maxMatches = 0,
        CancellationToken ct = default)
    {
        var cap = maxMatches > 0 ? Math.Min(maxMatches, MaxMatchesCeiling) : DefaultMaxMatches;

        var regexOptions = RegexOptions.Compiled;
        if (!caseSensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        var regex = new Regex(pattern, regexOptions, RegexTimeout);

        var dir = new DirectoryInfo(this._logsPath);
        IEnumerable<FileInfo> files = dir.GetFiles("*.log")
            .OrderByDescending(f => f.LastWriteTime);

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            ValidateFileName(fileName);
            files = files.Where(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase));
        }

        // Newest files first, stopping once the cap is reached — bounds the work on large
        // rolled log sets so a common pattern can't scan hundreds of megabytes.
        var flat = new List<LogSearchResult>();
        foreach (var file in files)
        {
            if (flat.Count >= cap)
            {
                break;
            }

            SearchFile(file, regex, contextLines, cap, flat, ct);
        }

        // Always redact — a raw secret in the LLM context is a leak (it can be logged, cached, or
        // absorbed into model training data), so there is intentionally no opt-out, even on dev.
        foreach (var r in flat)
        {
            r.MatchedLine = SecretRedactor.Redact(r.MatchedLine);

            for (var i = 0; i < r.ContextBefore.Count; i++)
            {
                r.ContextBefore[i] = SecretRedactor.Redact(r.ContextBefore[i]);
            }

            for (var i = 0; i < r.ContextAfter.Count; i++)
            {
                r.ContextAfter[i] = SecretRedactor.Redact(r.ContextAfter[i]);
            }
        }

        return Task.FromResult<IReadOnlyList<LogSearchResult>>(flat);
    }

    /// <summary>
    /// Streams a single file line-by-line, appending matches to <paramref name="results"/> until the
    /// shared <paramref name="cap"/> is reached. Memory stays flat regardless of file size: only a
    /// small before-context window and the matches still collecting after-context are held.
    /// </summary>
    private static void SearchFile(
        FileInfo file, Regex regex, int contextLines, int cap, List<LogSearchResult> results, CancellationToken ct)
    {
        try
        {
            // FileShare.ReadWrite for concurrent access
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var before = new Queue<string>();
            var pendingAfter = new List<LogSearchResult>();
            var lineNumber = 0;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                ct.ThrowIfCancellationRequested();
                lineNumber++;

                // Feed this line as after-context to any recent match still collecting it.
                for (var p = pendingAfter.Count - 1; p >= 0; p--)
                {
                    pendingAfter[p].ContextAfter.Add(line);
                    if (pendingAfter[p].ContextAfter.Count >= contextLines)
                    {
                        pendingAfter.RemoveAt(p);
                    }
                }

                if (regex.IsMatch(line))
                {
                    var result = new LogSearchResult
                    {
                        FileName = file.Name,
                        LineNumber = lineNumber,
                        MatchedLine = line
                    };
                    result.ContextBefore.AddRange(before);
                    results.Add(result);

                    if (contextLines > 0)
                    {
                        pendingAfter.Add(result);
                    }

                    if (results.Count >= cap)
                    {
                        return;
                    }
                }

                // Maintain a rolling window of the last contextLines lines for before-context.
                before.Enqueue(line);
                while (before.Count > contextLines)
                {
                    before.Dequeue();
                }
            }
        }
        catch (IOException)
        {
            // File may be locked by Sitefinity — skip it
        }
    }

    /// <summary>
    /// Prevents path traversal attacks by rejecting filenames with directory separators or relative paths.
    /// </summary>
    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("File name cannot be empty.");
        }

        if (fileName.Contains("..") ||
            fileName.Contains('/') ||
            fileName.Contains('\\') ||
            Path.IsPathRooted(fileName))
        {
            throw new ArgumentException($"Invalid file name: {fileName}. Path traversal is not allowed.");
        }
    }
}

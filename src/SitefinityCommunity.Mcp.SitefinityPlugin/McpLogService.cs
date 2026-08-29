// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Hosting;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// ServiceStack service exposing Sitefinity log files via REST API.
    /// All endpoints require [McpApiKey] authentication.
    /// </summary>
    [McpApiKey]
    public class McpLogService : Service
    {
        private static readonly string LogsPath =
            HostingEnvironment.MapPath("~/App_Data/Sitefinity/Logs") ?? string.Empty;

        /// <summary>Default cap on total matches when the request does not specify one.</summary>
        private const int DefaultMaxMatches = 200;

        /// <summary>Hard ceiling on matches, even when the request asks for more.</summary>
        private const int MaxMatchesCeiling = 1000;

        /// <summary>
        /// GET /RestApi/mcp/logs — List all log files with metadata.
        /// </summary>
        public List<McpLogFileInfo> Get(ListLogFiles request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Logs);

            if (!Directory.Exists(LogsPath))
            {
                return new List<McpLogFileInfo>();
            }

            var dir = new DirectoryInfo(LogsPath);
            return dir.GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTime)
                .Select(f => new McpLogFileInfo
                {
                    FileName = f.Name,
                    SizeBytes = f.Length,
                    LastModified = f.LastWriteTime
                })
                .ToList();
        }

        /// <summary>
        /// GET /RestApi/mcp/logs/{FileName} — Read a log file's content.
        /// </summary>
        public string Get(ReadLogFile request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Logs);

            ValidateFileName(request.FileName);

            var filePath = Path.Combine(LogsPath, request.FileName);
            if (!File.Exists(filePath))
            {
                throw HttpError.NotFound("Log file not found: " + request.FileName);
            }

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                if (request.MaxLines > 0)
                {
                    var lines = new List<string>();
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        lines.Add(line);
                    }

                    // Return last N lines
                    var skip = Math.Max(0, lines.Count - request.MaxLines);
                    return string.Join("\n", lines.Skip(skip));
                }

                return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// POST /RestApi/mcp/logs/search — Search all logs with regex.
        /// </summary>
        public List<McpSearchResult> Post(SearchLogs request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Logs);

            if (string.IsNullOrEmpty(request.Pattern))
            {
                throw HttpError.BadRequest("Pattern is required.");
            }

            if (!Directory.Exists(LogsPath))
            {
                return new List<McpSearchResult>();
            }

            var regexOptions = RegexOptions.Compiled;
            if (!request.CaseSensitive)
            {
                regexOptions |= RegexOptions.IgnoreCase;
            }

            var regex = new Regex(request.Pattern, regexOptions, TimeSpan.FromSeconds(5));
            var contextLines = Math.Max(0, Math.Min(request.ContextLines, 10));

            var cap = request.MaxMatches > 0
                ? Math.Min(request.MaxMatches, MaxMatchesCeiling)
                : DefaultMaxMatches;

            var results = new List<McpSearchResult>();
            var dir = new DirectoryInfo(LogsPath);

            // Newest files first, stopping once the cap is reached — bounds the work on large rolled
            // log sets so a common pattern can't scan hundreds of megabytes and time the client out.
            IEnumerable<FileInfo> files = dir.GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTime);

            if (!string.IsNullOrWhiteSpace(request.FileName))
            {
                ValidateFileName(request.FileName);
                files = files.Where(f => string.Equals(f.Name, request.FileName, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var file in files)
            {
                if (results.Count >= cap)
                {
                    break;
                }

                try
                {
                    SearchFile(file, regex, contextLines, cap, results);
                }
                catch (IOException)
                {
                    // File may be locked by Sitefinity — skip it
                }
            }

            return results;
        }

        /// <summary>
        /// GET /RestApi/mcp/ping — Lightweight key validation endpoint.
        /// Returns <c>{ status: "ok" }</c> plus the per-capability feature roster if the API key is valid.
        /// <para>
        /// Deliberately NOT capability-gated: this is how the MCP server learns which capabilities
        /// are off, so it must answer even when every other endpoint is disabled.
        /// </para>
        /// </summary>
        public McpPingResponse Get(PingMcp request)
        {
            return new McpPingResponse
            {
                Status = "ok",
                Features = McpCapabilities.BuildRoster(),
            };
        }

        /// <summary>
        /// GET /RestApi/mcp/logs/last-error — Most recent error entry.
        /// </summary>
        public string Get(GetLastError request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Logs);

            var errorLogPath = Path.Combine(LogsPath, "Error.log");
            if (!File.Exists(errorLogPath))
            {
                return "No Error.log found.";
            }

            using (var stream = new FileStream(errorLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var content = reader.ReadToEnd();
                var separator = "----------------------------------------";
                var blocks = content.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);

                // Return the last non-empty block
                for (var i = blocks.Length - 1; i >= 0; i--)
                {
                    var trimmed = blocks[i].Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        return trimmed;
                    }
                }

                return "Error.log is empty.";
            }
        }

        /// <summary>
        /// Streams a single file line-by-line, appending matches until the shared <paramref name="cap"/>
        /// is reached. Memory stays flat regardless of file size: only a small before-context window and
        /// the matches still collecting after-context are held, never the whole file.
        /// </summary>
        private static void SearchFile(FileInfo file, Regex regex, int contextLines, int cap, List<McpSearchResult> results)
        {
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var before = new Queue<string>();
                var pendingAfter = new List<McpSearchResult>();
                var lineNumber = 0;
                string line;

                while ((line = reader.ReadLine()) != null)
                {
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
                        var result = new McpSearchResult
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
        }

        /// <summary>
        /// Prevents path traversal attacks.
        /// </summary>
        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw HttpError.BadRequest("File name cannot be empty.");
            }

            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\") || Path.IsPathRooted(fileName))
            {
                throw HttpError.BadRequest("Invalid file name. Path traversal is not allowed.");
            }
        }
    }
}

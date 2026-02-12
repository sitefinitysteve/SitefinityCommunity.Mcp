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

        /// <summary>
        /// GET /RestApi/mcp/logs — List all log files with metadata.
        /// </summary>
        public List<McpLogFileInfo> Get(ListLogFiles request)
        {
            if (!Directory.Exists(LogsPath))
                return new List<McpLogFileInfo>();

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
            ValidateFileName(request.FileName);

            var filePath = Path.Combine(LogsPath, request.FileName);
            if (!File.Exists(filePath))
                throw HttpError.NotFound("Log file not found: " + request.FileName);

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
            if (string.IsNullOrEmpty(request.Pattern))
                throw HttpError.BadRequest("Pattern is required.");

            if (!Directory.Exists(LogsPath))
                return new List<McpSearchResult>();

            var regexOptions = RegexOptions.Compiled;
            if (!request.CaseSensitive)
                regexOptions |= RegexOptions.IgnoreCase;

            var regex = new Regex(request.Pattern, regexOptions, TimeSpan.FromSeconds(5));
            var contextLines = Math.Max(0, Math.Min(request.ContextLines, 10));

            var results = new List<McpSearchResult>();
            var dir = new DirectoryInfo(LogsPath);

            foreach (var file in dir.GetFiles("*.log"))
            {
                try
                {
                    SearchFile(file, regex, contextLines, results);
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
        /// Returns { status: "ok" } if the API key is valid.
        /// </summary>
        public McpPingResponse Get(PingMcp request)
        {
            return new McpPingResponse { Status = "ok" };
        }

        /// <summary>
        /// GET /RestApi/mcp/logs/last-error — Most recent error entry.
        /// </summary>
        public string Get(GetLastError request)
        {
            var errorLogPath = Path.Combine(LogsPath, "Error.log");
            if (!File.Exists(errorLogPath))
                return "No Error.log found.";

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
                        return trimmed;
                }

                return "Error.log is empty.";
            }
        }

        private static void SearchFile(FileInfo file, Regex regex, int contextLines, List<McpSearchResult> results)
        {
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
            {
                var allLines = new List<string>();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    allLines.Add(line);
                }

                for (var i = 0; i < allLines.Count; i++)
                {
                    if (!regex.IsMatch(allLines[i]))
                        continue;

                    var result = new McpSearchResult
                    {
                        FileName = file.Name,
                        LineNumber = i + 1,
                        MatchedLine = allLines[i]
                    };

                    for (var j = Math.Max(0, i - contextLines); j < i; j++)
                        result.ContextBefore.Add(allLines[j]);

                    for (var j = i + 1; j <= Math.Min(allLines.Count - 1, i + contextLines); j++)
                        result.ContextAfter.Add(allLines[j]);

                    results.Add(result);
                }
            }
        }

        /// <summary>
        /// Prevents path traversal attacks.
        /// </summary>
        private static void ValidateFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw HttpError.BadRequest("File name cannot be empty.");

            if (fileName.Contains("..") || fileName.Contains("/") || fileName.Contains("\\") || Path.IsPathRooted(fileName))
                throw HttpError.BadRequest("Invalid file name. Path traversal is not allowed.");
        }
    }
}

// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    // ── Request DTOs ──────────────────────────────────────────────────

    [Route("/mcp/logs", "GET")]
    public class ListLogFiles : IReturn<List<McpLogFileInfo>>
    {
    }

    [Route("/mcp/logs/{FileName}", "GET")]
    public class ReadLogFile : IReturn<string>
    {
        public string FileName { get; set; }
        public int MaxLines { get; set; }
    }

    [Route("/mcp/logs/search", "POST")]
    public class SearchLogs : IReturn<List<McpSearchResult>>
    {
        public string Pattern { get; set; }
        public int ContextLines { get; set; }
        public bool CaseSensitive { get; set; }
    }

    [Route("/mcp/logs/last-error", "GET")]
    public class GetLastError : IReturn<string>
    {
    }

    [Route("/mcp/ping", "GET")]
    public class PingMcp : IReturn<McpPingResponse>
    {
    }

    // ── Response DTOs ─────────────────────────────────────────────────

    public class McpPingResponse
    {
        public string Status { get; set; }
    }


    public class McpLogFileInfo
    {
        public string FileName { get; set; }
        public long SizeBytes { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class McpSearchResult
    {
        public string FileName { get; set; }
        public int LineNumber { get; set; }
        public string MatchedLine { get; set; }
        public List<string> ContextBefore { get; set; } = new List<string>();
        public List<string> ContextAfter { get; set; } = new List<string>();
    }

    // ── Metadata Request DTOs ────────────────────────────────────────

    [Route("/mcp/site-info", "GET")]
    public class GetSiteInfo : IReturn<McpSiteInfoResponse>
    {
    }

    [Route("/mcp/modules", "GET")]
    public class ListModules : IReturn<List<McpModuleInfo>>
    {
    }

    [Route("/mcp/dynamic-types", "GET")]
    public class ListDynamicTypes : IReturn<List<McpDynamicTypeInfo>>
    {
    }

    [Route("/mcp/dynamic-types/{TypeFullName}/fields", "GET")]
    public class GetTypeFields : IReturn<List<McpDynamicFieldInfo>>
    {
        public string TypeFullName { get; set; }
    }

    [Route("/mcp/routes", "GET")]
    public class ListRoutes : IReturn<McpRoutesResponse>
    {
    }

    // ── Metadata Response DTOs ───────────────────────────────────────

    public class McpSiteInfoResponse
    {
        public string SitefinityVersion { get; set; }
        public string DotNetVersion { get; set; }
        public string ProjectName { get; set; }
        public int ModuleCount { get; set; }
        public List<string> Languages { get; set; } = new List<string>();
        public List<McpSiteEntry> Sites { get; set; } = new List<McpSiteEntry>();
    }

    public class McpSiteEntry
    {
        public string Name { get; set; }
        public string LiveUrl { get; set; }
        public bool IsDefault { get; set; }
    }

    public class McpModuleInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string StartupType { get; set; }
        public string Status { get; set; }
    }

    public class McpDynamicTypeInfo
    {
        public string ModuleName { get; set; }
        public string TypeName { get; set; }
        public string TypeFullName { get; set; }
        public int FieldCount { get; set; }
    }

    public class McpDynamicFieldInfo
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string FieldType { get; set; }
        public bool IsRequired { get; set; }
        public bool IsMainField { get; set; }
        public string ClassificationName { get; set; }
        public string RelatedDataType { get; set; }
    }

    // ── Route Response DTOs ────────────────────────────────────────

    public class McpRoutesResponse
    {
        public List<McpPageRoute> PageRoutes { get; set; } = new List<McpPageRoute>();
        public List<McpApiRoute> ApiRoutes { get; set; } = new List<McpApiRoute>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpPageRoute
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string NodeType { get; set; }
        public bool IsPublished { get; set; }
        public int Depth { get; set; }
        public bool HasUrlEvaluation { get; set; }
        public string UrlEvaluationMode { get; set; }
    }

    public class McpApiRoute
    {
        public string Path { get; set; }
        public string Verbs { get; set; }
        public string RequestType { get; set; }
    }
}

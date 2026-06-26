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

    [Route("/mcp/page-routes", "GET")]
    public class ListPageRoutes : IReturn<McpPageRoutesResponse>
    {
    }

    [Route("/mcp/api-routes", "GET")]
    public class ListApiRoutes : IReturn<McpApiRoutesResponse>
    {
    }

    [Route("/mcp/page-details", "GET")]
    public class GetPageDetails : IReturn<McpPageDetailsResponse>
    {
        public string PageIdentifier { get; set; }
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
        public string ClrType { get; set; }
        public bool IsRequired { get; set; }
        public bool IsMainField { get; set; }
        public string ClassificationName { get; set; }
        public string RelatedDataType { get; set; }
    }

    [Route("/mcp/modules/{ModuleName}/structure", "GET")]
    public class GetModuleStructure : IReturn<McpModuleStructureResponse>
    {
        public string ModuleName { get; set; }
    }

    public class McpModuleStructureResponse
    {
        public string ModuleName { get; set; }
        public string ModuleTitle { get; set; }
        public List<McpDynamicTypeNode> RootTypes { get; set; } = new List<McpDynamicTypeNode>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpDynamicTypeNode
    {
        public string TypeName { get; set; }
        public string TypeFullName { get; set; }
        public string ParentTypeName { get; set; }
        public List<McpDynamicFieldInfo> Fields { get; set; } = new List<McpDynamicFieldInfo>();
        public List<McpDynamicTypeNode> ChildTypes { get; set; } = new List<McpDynamicTypeNode>();
    }

    // ── Route Response DTOs ────────────────────────────────────────

    public class McpPageRoute
    {
        public string Title { get; set; }
        public string Url { get; set; }
        public string Slug { get; set; }
        public List<string> AdditionalUrls { get; set; }
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

    // ── Split Route Response DTOs ─────────────────────────────────

    public class McpPageRoutesResponse
    {
        public List<McpPageRoute> PageRoutes { get; set; } = new List<McpPageRoute>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpApiRoutesResponse
    {
        public List<McpApiRoute> ServiceStackRoutes { get; set; } = new List<McpApiRoute>();
        public List<McpODataRoute> ODataRoutes { get; set; } = new List<McpODataRoute>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpODataRoute
    {
        public string EntitySetName { get; set; }
        public string EntitySetUrl { get; set; }
    }

    // ── Page Details Response DTOs ─────────────────────────────────

    public class McpPageDetailsResponse
    {
        public string Id { get; set; }
        public string PageDataId { get; set; }
        public string Title { get; set; }
        public string Url { get; set; }
        public string UrlName { get; set; }
        public string NodeType { get; set; }
        public bool IsPublished { get; set; }
        public string TemplateName { get; set; }
        public string Description { get; set; }
        public int Depth { get; set; }
        public List<McpPageWidgetInfo> Widgets { get; set; } = new List<McpPageWidgetInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpPageWidgetInfo
    {
        public string Id { get; set; }
        public string ObjectType { get; set; }
        public string WidgetName { get; set; }
        public string FriendlyName { get; set; }
        public string PlaceHolder { get; set; }
        public string Caption { get; set; }
        public bool IsLayoutControl { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> SettingsProperties { get; set; } = new Dictionary<string, string>();
    }

    // ── Widget Properties Request/Response DTOs ───────────────────

    [Route("/mcp/widgets/{WidgetId}/properties", "GET")]
    public class GetWidgetProperties : IReturn<McpWidgetPropertiesResponse>
    {
        public string WidgetId { get; set; }
        public string PageIdentifier { get; set; }
    }

    public class McpWidgetPropertiesResponse
    {
        public string WidgetId { get; set; }
        public string ObjectType { get; set; }
        public string FriendlyName { get; set; }
        public string PlaceHolder { get; set; }
        public string Caption { get; set; }
        public bool IsLayoutControl { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> SettingsProperties { get; set; } = new Dictionary<string, string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Content / Templates / Taxonomies / Page-Widget-Tree DTOs ─────

    [Route("/mcp/content", "GET")]
    public class ListContent : IReturn<McpContentListResponse>
    {
        public string TypeFullName { get; set; }
        public int Take { get; set; }
        public int Skip { get; set; }
    }

    public class McpContentItemInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string UrlName { get; set; }
        public string Status { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? LastModified { get; set; }
        public string ContentType { get; set; }
    }

    public class McpContentListResponse
    {
        public string TypeFullName { get; set; }
        public int TotalCount { get; set; }
        public int Take { get; set; }
        public int Skip { get; set; }
        public List<McpContentItemInfo> Items { get; set; } = new List<McpContentItemInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    [Route("/mcp/templates", "GET")]
    public class ListTemplates : IReturn<McpTemplatesResponse>
    {
        /// <summary>When true, backend/hybrid admin templates are included. Default: false.</summary>
        public bool IncludeBackend { get; set; }
    }

    public class McpPageTemplateInfo
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Name { get; set; }
        public string Framework { get; set; }
        public string ParentTemplateId { get; set; }
        public string Culture { get; set; }
        /// <summary>Resource package / theme the template belongs to (MVC templates only).</summary>
        public string ResourcePackage { get; set; }
        public bool IsBackend { get; set; }
    }

    public class McpTemplatesResponse
    {
        public List<McpPageTemplateInfo> Templates { get; set; } = new List<McpPageTemplateInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    [Route("/mcp/taxonomies", "GET")]
    public class ListTaxonomies : IReturn<McpTaxonomiesResponse>
    {
    }

    public class McpTaxonomyInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string TaxonomyType { get; set; }
        public int TaxaCount { get; set; }
    }

    public class McpTaxonInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string ParentId { get; set; }
    }

    public class McpTaxonomiesResponse
    {
        public List<McpTaxonomyInfo> Taxonomies { get; set; } = new List<McpTaxonomyInfo>();
        public Dictionary<string, List<McpTaxonInfo>> Taxa { get; set; } = new Dictionary<string, List<McpTaxonInfo>>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    [Route("/mcp/page-widget-tree", "GET")]
    public class GetPageWidgetTree : IReturn<McpPageWidgetTreeResponse>
    {
        public string PageIdentifier { get; set; }
        public bool IncludeLayoutControls { get; set; }
    }

    public class McpPageWidgetTreeResponse
    {
        public string PageId { get; set; }
        public string PageTitle { get; set; }
        public string PageUrl { get; set; }
        public string TemplateId { get; set; }
        public List<McpPlaceholderNode> Placeholders { get; set; } = new List<McpPlaceholderNode>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpPlaceholderNode
    {
        public string Name { get; set; }
        public List<McpWidgetNode> Widgets { get; set; } = new List<McpWidgetNode>();
    }

    public class McpWidgetNode
    {
        public string Id { get; set; }
        public string ObjectType { get; set; }
        public string ControllerName { get; set; }
        public string FriendlyName { get; set; }
        public string Caption { get; set; }
        public string PlaceHolder { get; set; }
        public bool IsLayoutControl { get; set; }
        public string SiblingId { get; set; }
        public int RenderOrder { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public List<McpPlaceholderNode> Children { get; set; } = new List<McpPlaceholderNode>();
    }

    // ── Forms DTOs ────────────────────────────────────────────────────

    [Route("/mcp/forms", "GET")]
    public class ListForms : IReturn<McpFormsResponse>
    {
    }

    [Route("/mcp/forms/{FormIdentifier}/fields", "GET")]
    public class GetFormFields : IReturn<McpFormFieldsResponse>
    {
        public string FormIdentifier { get; set; }

        /// <summary>
        /// When true, the response includes a raw dump of every FormControl's
        /// Properties + ChildProperties tree under <see cref="McpFormFieldsResponse.DebugDump"/>.
        /// Use this to troubleshoot empty Name/Title results on unfamiliar Sitefinity versions.
        /// </summary>
        public bool Debug { get; set; }
    }

    [Route("/mcp/forms/{FormIdentifier}/responses", "GET")]
    public class ListFormResponses : IReturn<McpFormResponsesResponse>
    {
        public string FormIdentifier { get; set; }
        public int Take { get; set; }
        public int Skip { get; set; }

        /// <summary>
        /// When set, only entries where at least one field value contains this term
        /// (case-insensitive) are returned. Matching is performed against the redacted
        /// values, so sensitive fields can never leak via search.
        /// </summary>
        public string SearchTerm { get; set; }
    }

    public class McpFormInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public bool IsPublished { get; set; }
        public int FieldCount { get; set; }
        public int EntryCount { get; set; }
        public DateTime? LastModified { get; set; }
    }

    public class McpFormsResponse
    {
        public List<McpFormInfo> Forms { get; set; } = new List<McpFormInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpFormFieldInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Title { get; set; }
        public string FieldType { get; set; }
        public bool IsRequired { get; set; }
        public string PlaceHolder { get; set; }
        public string DefaultValue { get; set; }
        public List<string> Choices { get; set; } = new List<string>();
    }

    public class McpFormFieldsResponse
    {
        public string FormId { get; set; }
        public string FormName { get; set; }
        public string FormTitle { get; set; }
        public List<McpFormFieldInfo> Fields { get; set; } = new List<McpFormFieldInfo>();
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Populated only when the request's Debug flag is true. Text dump of the raw
        /// Properties tree for every FormControl on the form — intended to be pasted back
        /// to a maintainer for diagnosing lookup mismatches.
        /// </summary>
        public string DebugDump { get; set; }
    }

    public class McpFormResponseInfo
    {
        public string Id { get; set; }
        public DateTime? SubmittedOn { get; set; }
        public string IpAddress { get; set; }
        public string UserAgent { get; set; }
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    }

    public class McpFormResponsesResponse
    {
        public string FormId { get; set; }
        public string FormName { get; set; }

        /// <summary>Total submissions on the form, regardless of search filter.</summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// When a SearchTerm was provided, how many entries matched. Equals TotalCount when no
        /// search was applied.
        /// </summary>
        public int MatchedCount { get; set; }

        public int Take { get; set; }
        public int Skip { get; set; }

        /// <summary>Echo of the search term that was applied, if any.</summary>
        public string SearchTerm { get; set; }

        public List<McpFormResponseInfo> Responses { get; set; } = new List<McpFormResponseInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Config Reader DTOs ────────────────────────────────────────────

    [Route("/mcp/config", "GET")]
    public class GetConfigSections : IReturn<McpConfigSectionsResponse>
    {
    }

    [Route("/mcp/config/{SectionName}", "GET")]
    public class GetConfigSection : IReturn<McpConfigSectionResponse>
    {
        public string SectionName { get; set; }
    }

    public class McpConfigSectionsResponse
    {
        public List<string> Sections { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpConfigEntry
    {
        public string Path { get; set; }
        public string Value { get; set; }
    }

    public class McpConfigSectionResponse
    {
        public string SectionName { get; set; }
        public string SectionType { get; set; }
        public bool Found { get; set; }
        public List<McpConfigEntry> Entries { get; set; } = new List<McpConfigEntry>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Where-Used DTOs ───────────────────────────────────────────────

    [Route("/mcp/where-used", "GET")]
    public class WhereUsed : IReturn<McpWhereUsedResponse>
    {
        public string Query { get; set; }

        /// <summary>Optional interpretation override: "widget", "content", or "template".</summary>
        public string Kind { get; set; }
    }

    public class McpWhereUsedItem
    {
        public string HostKind { get; set; }
        public string HostId { get; set; }
        public string HostTitle { get; set; }
        public string HostUrl { get; set; }
        public string WidgetId { get; set; }
        public string WidgetName { get; set; }
        public string MatchReason { get; set; }
    }

    public class McpWhereUsedResponse
    {
        public string Query { get; set; }
        public string ResolvedKind { get; set; }
        public string ResolvedTitle { get; set; }
        public int TotalUsages { get; set; }
        public List<McpWhereUsedItem> Usages { get; set; } = new List<McpWhereUsedItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Permissions Inspector DTOs ────────────────────────────────────

    [Route("/mcp/permissions", "GET")]
    public class GetObjectPermissions : IReturn<McpPermissionsResponse>
    {
        public string Identifier { get; set; }

        /// <summary>When set, the Identifier is treated as a content item Guid of this CLR type.</summary>
        public string TypeFullName { get; set; }
    }

    public class McpPermissionEntry
    {
        public string PermissionSetName { get; set; }
        public string RoleId { get; set; }
        public string RoleName { get; set; }
        public List<string> GrantedActions { get; set; } = new List<string>();
        public List<string> DeniedActions { get; set; } = new List<string>();
    }

    public class McpPermissionsResponse
    {
        public string Target { get; set; }
        public string TargetKind { get; set; }
        public string TargetTitle { get; set; }
        public bool InheritsPermissions { get; set; }
        public List<McpPermissionEntry> Permissions { get; set; } = new List<McpPermissionEntry>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Maintenance (Write) DTOs ──────────────────────────────────────

    [Route("/mcp/cache/clear", "POST")]
    public class ClearCache : IReturn<McpMaintenanceResponse>
    {
        /// <summary>"output" (default), "whole", or "page".</summary>
        public string Scope { get; set; }

        /// <summary>Page identifier (Guid, URL, or title) when Scope is "page".</summary>
        public string PageIdentifier { get; set; }
    }

    [Route("/mcp/app/recycle", "POST")]
    public class RecycleApp : IReturn<McpMaintenanceResponse>
    {
    }

    public class McpMaintenanceResponse
    {
        public string Operation { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }
}

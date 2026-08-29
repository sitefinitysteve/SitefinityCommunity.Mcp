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

        /// <summary>
        /// Maximum matches to collect before stopping. Bounds the work on large rolled log sets.
        /// 0 (or unset) falls back to the server default (200); values above 1000 are clamped.
        /// </summary>
        public int MaxMatches { get; set; }

        /// <summary>
        /// When set, only this single log file is searched (e.g. "Error.log"). When empty, every
        /// *.log file is searched newest-first.
        /// </summary>
        public string FileName { get; set; }
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

        /// <summary>
        /// Version of the plugin source installed in this Sitefinity project, from
        /// <c>McpPluginInfo.Version</c>. Builds before 3.6.0 omit it, which the MCP server reports as
        /// "3.5.0 or earlier".
        /// </summary>
        public string PluginVersion { get; set; }

        /// <summary>
        /// Per-capability roster reflecting Admin &gt; Advanced &gt; McpSettings. Older plugin
        /// builds omit this; MCP servers treat a missing roster as "everything enabled".
        /// </summary>
        public McpFeatureRoster Features { get; set; }
    }

    /// <summary>
    /// Which MCP capability areas are currently switched on. Advisory only — the plugin
    /// enforces the same flags on every request via <c>McpCapabilities.EnsureEnabled</c>.
    /// </summary>
    public class McpFeatureRoster
    {
        /// <summary>Sitefinity log endpoints.</summary>
        public bool Logs { get; set; }

        /// <summary>Site info, modules, dynamic types, routes, pages, widgets, templates, taxonomies.</summary>
        public bool Metadata { get; set; }

        /// <summary>Live content queries.</summary>
        public bool Content { get; set; }

        /// <summary>Form definitions and submissions.</summary>
        public bool Forms { get; set; }

        /// <summary>Configuration section reader and advanced-settings search.</summary>
        public bool ConfigReader { get; set; }

        /// <summary>Reverse lookup of widget / content / template usage.</summary>
        public bool WhereUsed { get; set; }

        /// <summary>Effective permissions reader.</summary>
        public bool Permissions { get; set; }

        /// <summary>Scheduled-task status and search index diagnostics.</summary>
        public bool Tasks { get; set; }

        /// <summary>Cache clear / application recycle. Mirrors <c>McpConfig.AllowWriteOperations</c>.</summary>
        public bool Maintenance { get; set; }

        /// <summary>Incident forensics, plus its three OS-level source flags.</summary>
        public McpIncidentFeatures Incident { get; set; }

        /// <summary>
        /// Creates a roster with every capability enabled — the shipped default.
        /// </summary>
        public McpFeatureRoster()
        {
            this.Logs = true;
            this.Metadata = true;
            this.Content = true;
            this.Forms = true;
            this.ConfigReader = true;
            this.WhereUsed = true;
            this.Permissions = true;
            this.Tasks = true;
            this.Maintenance = false;
            this.Incident = new McpIncidentFeatures();
        }
    }

    /// <summary>
    /// Incident capability state: whether the endpoint is on, and which OS-level sources it may read.
    /// </summary>
    public class McpIncidentFeatures
    {
        /// <summary>Whether the incident endpoint is reachable at all.</summary>
        public bool Enabled { get; set; }

        /// <summary>Whether the IIS W3C access log may be scanned.</summary>
        public bool AllowIisLogs { get; set; }

        /// <summary>Whether the Windows Application and System event logs may be read.</summary>
        public bool AllowEventLogs { get; set; }

        /// <summary>Whether the http.sys HTTPERR logs may be read.</summary>
        public bool AllowHttpErr { get; set; }

        /// <summary>
        /// Creates the incident feature state with everything enabled — the shipped default.
        /// </summary>
        public McpIncidentFeatures()
        {
            this.Enabled = true;
            this.AllowIisLogs = true;
            this.AllowEventLogs = true;
            this.AllowHttpErr = true;
        }
    }

    /// <summary>
    /// Body returned with HTTP 403 when a capability has been switched off by an administrator.
    /// </summary>
    public class McpCapabilityDisabledResponse
    {
        /// <summary>Name of the disabled capability (e.g. <c>Forms</c>).</summary>
        public string Disabled { get; set; }

        /// <summary>Human-readable explanation pointing at the admin screen.</summary>
        public string Reason { get; set; }
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

        /// <summary>
        /// When true, emit every property Sitefinity materializes, including values still sitting at
        /// their compiled-in defaults. Off by default: a defaults-merged section such as
        /// ContentViewConfig expands to hundreds of thousands of leaves, the overwhelming majority of
        /// which nobody ever set.
        /// </summary>
        public bool IncludeDefaults { get; set; }

        /// <summary>
        /// Case-insensitive substring; only entries whose path contains it are returned. The walk still
        /// visits the whole section, so <see cref="McpConfigSectionResponse.TotalCount"/> stays honest.
        /// </summary>
        public string PathFilter { get; set; }

        /// <summary>Maximum entries to return. Defaults to 500, clamped to 5000.</summary>
        public int MaxEntries { get; set; }
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

        /// <summary>Entries that matched the filter, capped at <see cref="MaxEntries"/>.</summary>
        public List<McpConfigEntry> Entries { get; set; } = new List<McpConfigEntry>();

        /// <summary>Total entries that matched the filter across the whole section, ignoring the cap.</summary>
        public int TotalCount { get; set; }

        /// <summary>Entries actually returned — <c>Entries.Count</c>, echoed for convenience.</summary>
        public int ReturnedCount { get; set; }

        /// <summary>True when <see cref="TotalCount"/> exceeded the cap and entries were dropped.</summary>
        public bool Truncated { get; set; }

        /// <summary>Echo of the applied options, so a caller can see what shaped the result.</summary>
        public bool IncludedDefaults { get; set; }
        public string PathFilter { get; set; }
        public int MaxEntries { get; set; }

        /// <summary>Leaves suppressed because they still held their compiled-in default value.</summary>
        public int DefaultsSkipped { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Settings Search DTOs ──────────────────────────────────────────

    [Route("/mcp/settings/search", "GET")]
    public class SearchSettings : IReturn<McpSettingsSearchResponse>
    {
        /// <summary>Full-text query, e.g. "output cache" or "smtp host".</summary>
        public string Query { get; set; }

        /// <summary>Maximum results to return. Defaults to 25, clamped to 100.</summary>
        public int Take { get; set; }
    }

    public class McpSettingsSearchResult
    {
        /// <summary>The setting's display caption, when the index provides one.</summary>
        public string Title { get; set; }

        /// <summary>Breadcrumb path to the setting within Advanced Settings.</summary>
        public string Path { get; set; }

        /// <summary>Owning config section, when the index provides one.</summary>
        public string Section { get; set; }

        /// <summary>Every indexed field on the document, secret-redacted.</summary>
        public Dictionary<string, string> Fields { get; set; } = new Dictionary<string, string>();
    }

    public class McpSettingsSearchResponse
    {
        public string Query { get; set; }
        public string IndexName { get; set; }
        public int Take { get; set; }
        public int ReturnedCount { get; set; }

        /// <summary>False when the advanced-settings index is disabled, missing, or unresolvable.</summary>
        public bool IndexAvailable { get; set; }

        /// <summary>Which query-construction variant produced the results (diagnostic).</summary>
        public string QueryVariant { get; set; }

        public List<McpSettingsSearchResult> Results { get; set; } = new List<McpSettingsSearchResult>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Where-Used DTOs ───────────────────────────────────────────────

    [Route("/mcp/where-used", "GET")]
    public class WhereUsed : IReturn<McpWhereUsedResponse>
    {
        /// <summary>
        /// What to look for. By Kind: a widget/controller type name (widget), a content-item Guid
        /// (content), a template id/name (template), or any literal substring to find in widget
        /// property values (property).
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Optional interpretation override: "widget", "content", "template", or "property".
        /// Auto-detected when omitted (a Guid probes template then content; any other token is a widget).
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// When false (default), a widget/content/property match found on a TEMPLATE is expanded into the
        /// individual pages that ride that template, each reported as an inherited usage — so the result
        /// answers "what actually breaks if I change this?". Set true to report only the template host and
        /// suppress the page expansion.
        /// </summary>
        public bool TemplateHostsOnly { get; set; }
    }

    public class McpWhereUsedItem
    {
        /// <summary>"page" or "template".</summary>
        public string HostKind { get; set; }
        public string HostId { get; set; }
        public string HostTitle { get; set; }
        public string HostUrl { get; set; }

        public string WidgetId { get; set; }
        public string WidgetName { get; set; }
        public string ControllerName { get; set; }
        public string ObjectType { get; set; }

        /// <summary>"medportal", "sitefinity", or "unknown" — provenance of the matched widget.</summary>
        public string Origin { get; set; }
        public string PlaceHolder { get; set; }

        public string MatchReason { get; set; }

        /// <summary>For content/property matches: the property whose value matched and a short snippet around it.</summary>
        public string MatchedProperty { get; set; }
        public string MatchSnippet { get; set; }

        /// <summary>Set when this page usage is inherited from a widget that actually lives on a template.</summary>
        public string ViaTemplateId { get; set; }
        public string ViaTemplateTitle { get; set; }
    }

    public class McpWhereUsedResponse
    {
        public string Query { get; set; }
        public string ResolvedKind { get; set; }
        public string ResolvedTitle { get; set; }

        public int TotalUsages { get; set; }
        public int PageUsageCount { get; set; }
        public int TemplateUsageCount { get; set; }

        /// <summary>Pages reported because the match lives on a template they ride (not on the page itself).</summary>
        public int InheritedPageCount { get; set; }

        public int ScannedPages { get; set; }
        public int ScannedTemplates { get; set; }
        public int SkippedHosts { get; set; }

        public List<McpWhereUsedItem> Usages { get; set; } = new List<McpWhereUsedItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    // ── Permissions Inspector DTOs ────────────────────────────────────

    [Route("/mcp/permissions", "GET")]
    public class GetObjectPermissions : IReturn<McpPermissionsResponse>
    {
        /// <summary>A page identifier (Guid, URL, slug, or title) or — with TypeFullName — a content-item Guid.</summary>
        public string Identifier { get; set; }

        /// <summary>When set, the Identifier is treated as a content item Guid of this CLR type.</summary>
        public string TypeFullName { get; set; }

        /// <summary>
        /// Optional. Focus a single action type (View, Create, Modify, Manage, Delete, ChangeOwner,
        /// ChangePermissions, Unlock). Combined with Principal it yields a direct yes/no Answer.
        /// </summary>
        public string Action { get; set; }

        /// <summary>Optional. Focus a single principal (role/user name or Guid) for the direct Answer.</summary>
        public string Principal { get; set; }
    }

    /// <summary>Computed access for one principal (role/user) on the object, after deny-wins resolution.</summary>
    public class McpPrincipalAccess
    {
        public string PrincipalId { get; set; }
        public string PrincipalName { get; set; }

        /// <summary>"Role", "User", "SpecialRole" (Everyone / Authenticated / Owner), or "Unknown".</summary>
        public string PrincipalType { get; set; }

        /// <summary>True for an administrative role — implicitly has full control regardless of grants.</summary>
        public bool IsAdministrative { get; set; }
        public string PermissionSet { get; set; }

        /// <summary>Actions the principal can effectively perform (granted AND not denied). The headline result.</summary>
        public List<string> EffectiveActions { get; set; } = new List<string>();
        public List<string> GrantedActions { get; set; } = new List<string>();
        public List<string> DeniedActions { get; set; } = new List<string>();
        public string Note { get; set; }
    }

    /// <summary>One permission set on the object: its full action vocabulary plus per-principal access.</summary>
    public class McpPermissionSetView
    {
        public string SetName { get; set; }
        public List<string> AvailableActions { get; set; } = new List<string>();
        public List<McpPrincipalAccess> Principals { get; set; } = new List<McpPrincipalAccess>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Direct yes/no answer to "can &lt;Principal&gt; &lt;Action&gt; this object?" when both are supplied.</summary>
    public class McpAccessAnswer
    {
        public string Principal { get; set; }
        public string Action { get; set; }
        public bool Allowed { get; set; }
        public string Reason { get; set; }
    }

    public class McpPermissionsResponse
    {
        public string Target { get; set; }
        public string TargetKind { get; set; }
        public string TargetTitle { get; set; }

        /// <summary>One-line human summary of the access picture.</summary>
        public string Summary { get; set; }

        public bool InheritsPermissions { get; set; }
        public bool CanInheritPermissions { get; set; }

        /// <summary>True when the object carries its own (local) permission rows rather than purely inheriting.</summary>
        public bool HasLocalOverrides { get; set; }

        /// <summary>For pages: the parent the object inherits permissions from, when inheriting.</summary>
        public string InheritedFrom { get; set; }

        /// <summary>True when the Everyone (anonymous) role can effectively View the object.</summary>
        public bool IsPublic { get; set; }

        /// <summary>True when any authenticated user can effectively View the object.</summary>
        public bool IsAuthenticatedAccessible { get; set; }

        public List<string> SupportedPermissionSets { get; set; } = new List<string>();

        /// <summary>Flattened, deny-resolved access across every set — the quick "who can do what" view.</summary>
        public List<McpPrincipalAccess> Principals { get; set; } = new List<McpPrincipalAccess>();

        /// <summary>Per-set detail, including each set's full action vocabulary.</summary>
        public List<McpPermissionSetView> Sets { get; set; } = new List<McpPermissionSetView>();

        /// <summary>Populated only when the request supplied Action and/or Principal.</summary>
        public McpAccessAnswer Answer { get; set; }

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

    // ── Incident Window (system log correlation) DTOs ──────────────────

    [Route("/mcp/incident-window", "GET")]
    public class GetIncidentWindow : IReturn<McpIncidentResponse>
    {
        /// <summary>
        /// The moment to centre the window on. Parsed as SERVER-LOCAL time unless the string carries an
        /// explicit offset or a trailing Z. Accepts "11:00", "2026-08-27 11:00", or a full ISO 8601 value.
        /// <para>
        /// When this is EMPTY the endpoint switches to discovery mode and returns candidate incident
        /// moments over the last <c>LookbackHours</c> instead of a correlated window.
        /// </para>
        /// </summary>
        public string Center { get; set; }

        /// <summary>Half-width of the window in minutes (the window is Center +/- this). Default 15, clamped 1-120.</summary>
        public int WindowMinutes { get; set; }

        /// <summary>
        /// Discovery mode only (Center empty): how far back to hunt for candidate incident moments.
        /// Default 72, clamped 1-336.
        /// </summary>
        public int LookbackHours { get; set; }

        /// <summary>
        /// Optional case-insensitive plain substring (NOT a regex) matched against entries in every
        /// scanned source. With a Center it filters the window; without one it switches the endpoint to
        /// search mode over <c>LookbackHours</c>.
        /// <para>
        /// Matching runs AFTER redaction, so a query can never be used as an oracle to probe for a
        /// redacted secret value.
        /// </para>
        /// </summary>
        public string Query { get; set; }

        /// <summary>
        /// Comma-separated list of sources to collect: <c>sitefinity</c>, <c>iis</c>, <c>eventlog</c>,
        /// <c>httperr</c>. Empty means all four. Ignored in discovery mode, which always scans only the
        /// cheap high-signal sources.
        /// </summary>
        public string Sources { get; set; }
    }

    /// <summary>
    /// Envelope for the two shapes the incident endpoint can return. Exactly one of
    /// <c>Window</c> / <c>Candidates</c> is populated, per <c>Mode</c>.
    /// </summary>
    public class McpIncidentResponse
    {
        /// <summary>
        /// "window" when a Center was supplied, "search" when only a Query was, "candidates" when
        /// neither was (discovery).
        /// </summary>
        public string Mode { get; set; }

        /// <summary>Populated when Mode is "window".</summary>
        public McpIncidentWindowResponse Window { get; set; }

        /// <summary>Populated when Mode is "candidates".</summary>
        public McpIncidentCandidatesResponse Candidates { get; set; }

        /// <summary>Populated when Mode is "search".</summary>
        public McpIncidentSearchResponse Search { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Search-mode result: every source swept over the lookback period for a plain substring. Uses the
    /// same section shapes as window mode, so an agent can read one set of fields either way.
    /// </summary>
    public class McpIncidentSearchResponse
    {
        /// <summary>Echo of the substring that was matched (case-insensitively, after redaction).</summary>
        public string Query { get; set; }

        public string ServerTimeZoneId { get; set; }
        public int ServerUtcOffsetMinutes { get; set; }

        public int LookbackHours { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:00:00Z</c>.</summary>
        public string LookbackStartUtc { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:00:00Z</c>.</summary>
        public string LookbackEndUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:00:00</c>.</summary>
        public string LookbackStartLocal { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:00:00</c>.</summary>
        public string LookbackEndLocal { get; set; }

        public List<string> ScannedSources { get; set; } = new List<string>();

        public McpIncidentSitefinitySection Sitefinity { get; set; }
        public McpIncidentIisSection Iis { get; set; }
        public McpIncidentEventLogSection EventLog { get; set; }
        public McpIncidentHttpErrSection HttpErr { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Discovery-mode result: candidate incident moments found over the lookback period, newest first.
    /// Feed one of these timestamps back as <c>Center</c> to reconstruct the full correlated window.
    /// </summary>
    public class McpIncidentCandidatesResponse
    {
        public string ServerTimeZoneId { get; set; }
        public int ServerUtcOffsetMinutes { get; set; }

        public int LookbackHours { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:00:00Z</c>.</summary>
        public string LookbackStartUtc { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:00:00Z</c>.</summary>
        public string LookbackEndUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:00:00</c>.</summary>
        public string LookbackStartLocal { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:00:00</c>.</summary>
        public string LookbackEndLocal { get; set; }

        /// <summary>Which cheap sources were actually scanned.</summary>
        public List<string> ScannedSources { get; set; } = new List<string>();

        /// <summary>Individual signals found before clustering.</summary>
        public int TotalSignals { get; set; }

        /// <summary>Clusters found before the cap.</summary>
        public int TotalCandidates { get; set; }
        public int ReturnedCount { get; set; }
        public bool Truncated { get; set; }

        public List<McpIncidentCandidate> Candidates { get; set; } = new List<McpIncidentCandidate>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>One clustered candidate incident moment.</summary>
    public class McpIncidentCandidate
    {
        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:01:00Z</c>. Feed this back as <c>Center</c>.</summary>
        public string TimestampUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:01:00</c>.</summary>
        public string TimestampLocal { get; set; }

        /// <summary>Headline signal, e.g. "WAS 5011 worker process crash".</summary>
        public string Signal { get; set; }

        /// <summary>"eventlog", "httperr", or "sitefinity".</summary>
        public string Source { get; set; }

        /// <summary>Everything else that clustered with the headline, summarised.</summary>
        public string Detail { get; set; }

        /// <summary>Total signals that merged into this candidate.</summary>
        public int SignalCount { get; set; }
    }

    /// <summary>
    /// Correlated view of everything four log sources recorded inside one time window. Every entry
    /// carries both a UTC and a server-local timestamp so clocks can never be misaligned by the caller.
    /// </summary>
    public class McpIncidentWindowResponse
    {
        /// <summary>The server's time zone id, e.g. "Eastern Standard Time".</summary>
        public string ServerTimeZoneId { get; set; }

        /// <summary>
        /// The server's UTC offset in minutes, evaluated AT the queried instant (so a window inside a
        /// different DST period than "now" still reports the offset that actually applied).
        /// </summary>
        public int ServerUtcOffsetMinutes { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:00:00Z</c>.</summary>
        public string CenterUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:00:00</c>.</summary>
        public string CenterLocal { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T14:45:00Z</c>.</summary>
        public string WindowStartUtc { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:15:00Z</c>.</summary>
        public string WindowEndUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T10:45:00</c>.</summary>
        public string WindowStartLocal { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:15:00</c>.</summary>
        public string WindowEndLocal { get; set; }

        public int WindowMinutes { get; set; }

        /// <summary>Echo of the sources that were actually collected.</summary>
        public List<string> RequestedSources { get; set; } = new List<string>();

        /// <summary>Echo of the substring filter that was applied to entries, if any.</summary>
        public string Query { get; set; }

        public McpIncidentSitefinitySection Sitefinity { get; set; }
        public McpIncidentIisSection Iis { get; set; }
        public McpIncidentEventLogSection EventLog { get; set; }
        public McpIncidentHttpErrSection HttpErr { get; set; }

        /// <summary>Top-level problems (bad input, a source that could not be reached at all).</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Sitefinity's own Error/Trace logs, filtered to the window.</summary>
    public class McpIncidentSitefinitySection
    {
        public bool Available { get; set; }
        public string LogsPath { get; set; }
        public List<string> FilesScanned { get; set; } = new List<string>();

        /// <summary>
        /// How the timestamps in the raw log were interpreted. Sitefinity writes server-local time by
        /// default; a site configured to log UTC would shift these by the server offset.
        /// </summary>
        public string TimestampInterpretation { get; set; }

        /// <summary>Entries whose timestamp fell inside the window, before any query filter or cap.</summary>
        public int TotalMatched { get; set; }

        /// <summary>
        /// Entries that also contained the Query substring. Equals <c>TotalMatched</c> when no query was
        /// supplied. Matching runs after redaction.
        /// </summary>
        public int MatchedCount { get; set; }

        public int ReturnedCount { get; set; }
        public bool Truncated { get; set; }

        public List<McpIncidentLogEntry> Entries { get; set; } = new List<McpIncidentLogEntry>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpIncidentLogEntry
    {
        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:01:12Z</c>.</summary>
        public string TimestampUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:01:12</c>.</summary>
        public string TimestampLocal { get; set; }
        public string FileName { get; set; }
        public string Severity { get; set; }
        public string Type { get; set; }
        public string Message { get; set; }
        public string RequestedUrl { get; set; }

        /// <summary>First few stack frames, when the entry carried a stack trace.</summary>
        public string StackTraceHead { get; set; }
    }

    /// <summary>
    /// Aggregated IIS W3C access-log activity for the window. Raw log lines are never returned — only
    /// counts, a status histogram, the 5xx responses, and the slowest requests.
    /// </summary>
    public class McpIncidentIisSection
    {
        public bool Available { get; set; }

        /// <summary>W3C log timestamps are always UTC, regardless of the log-rollover setting.</summary>
        public string TimestampInterpretation { get; set; }

        public int SiteId { get; set; }
        public string LogFolder { get; set; }
        public List<string> FilesScanned { get; set; } = new List<string>();

        public long LinesScanned { get; set; }

        /// <summary>Data lines that could not be parsed (short rows, unexpected field values).</summary>
        public int MalformedLines { get; set; }

        /// <summary>True when the line-scan ceiling was hit and later lines were not examined.</summary>
        public bool Truncated { get; set; }

        public int TotalRequests { get; set; }

        /// <summary>
        /// Per-minute request counts. Populated in WINDOW mode only (at most 240 rows for the 120-minute
        /// maximum), where minute resolution is what shows traffic falling off the instant a pool dies.
        /// Empty in search mode — see <c>RequestsPerHour</c>.
        /// </summary>
        public List<McpIncidentMinuteCount> RequestsPerMinute { get; set; } = new List<McpIncidentMinuteCount>();

        /// <summary>
        /// Per-hour request counts. Populated in SEARCH mode only, where the lookback can be 14 days —
        /// minute rows would be ~20,000 entries of pure context bloat for a result the caller is reading
        /// for its matches, not its traffic shape. Empty in window mode — see <c>RequestsPerMinute</c>.
        /// </summary>
        public List<McpIncidentMinuteCount> RequestsPerHour { get; set; } = new List<McpIncidentMinuteCount>();

        /// <summary>Counts keyed by "status.substatus", e.g. "200.0", "500.0", "503.2".</summary>
        public List<McpIncidentCount> StatusHistogram { get; set; } = new List<McpIncidentCount>();

        public int TotalServerErrors { get; set; }
        public int ReturnedServerErrors { get; set; }
        public bool ServerErrorsTruncated { get; set; }
        public List<McpIisRequestEntry> ServerErrors { get; set; } = new List<McpIisRequestEntry>();

        public List<McpIisRequestEntry> SlowestRequests { get; set; } = new List<McpIisRequestEntry>();

        /// <summary>
        /// Requests whose redacted username / URI / query / client IP / referer contained the Query
        /// substring — ALL status codes, not just 5xx, because a user's full request trail is the point.
        /// Empty when no query was supplied. The aggregates above always cover the whole window,
        /// unfiltered, so traffic context survives the filter.
        /// </summary>
        public List<McpIisRequestEntry> MatchedRequests { get; set; } = new List<McpIisRequestEntry>();

        /// <summary>
        /// Requests matching the Query across the whole window, before the cap. Zero when no query was
        /// supplied — use <c>TotalRequests</c> for the unfiltered count.
        /// </summary>
        public int MatchedCount { get; set; }
        public bool MatchedRequestsTruncated { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// One bucket of the request-rate series. The bucket width is minutes or hours depending on which
    /// list it came from; the labels are preformatted strings so a local wall time cannot be re-read as
    /// an instant somewhere down the wire.
    /// </summary>
    public class McpIncidentMinuteCount
    {
        /// <summary>Bucket start in UTC, e.g. <c>2026-08-27 15:01</c> (or <c>15:00</c> for an hour bucket).</summary>
        public string MinuteUtc { get; set; }

        /// <summary>Bucket start in server-local wall time, e.g. <c>2026-08-27 11:01</c>.</summary>
        public string MinuteLocal { get; set; }

        public int Count { get; set; }
    }

    public class McpIncidentCount
    {
        public string Key { get; set; }
        public int Count { get; set; }
    }

    /// <summary>
    /// One IIS request. Client IP and <c>cs-username</c> are deliberately retained — they are the whole
    /// point of correlating an outage to who was hitting what. Query strings are redacted, and
    /// <c>cs(Cookie)</c> / <c>cs(Authorization)</c> columns are never read at all.
    /// </summary>
    public class McpIisRequestEntry
    {
        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:01:12Z</c>.</summary>
        public string TimestampUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:01:12</c>.</summary>
        public string TimestampLocal { get; set; }
        public string Method { get; set; }
        public string UriStem { get; set; }
        public string UriQuery { get; set; }
        public int Status { get; set; }
        public int SubStatus { get; set; }
        public long Win32Status { get; set; }
        public int TimeTakenMs { get; set; }
        public string UserName { get; set; }
        public string ClientIp { get; set; }

        /// <summary>
        /// The <c>cs(Referer)</c> column, redacted. Returned because a Query can match on it: without the
        /// field, a hit on a referer looks like a false positive to the caller (a search for "macdot"
        /// legitimately matching /appstatus requests referred from a macdot page, with no visible reason).
        /// Empty when the server does not log the column.
        /// </summary>
        public string Referer { get; set; }
    }

    /// <summary>
    /// http.sys error-log activity. These are the 503s that never reach the site's own IIS log because
    /// the app pool was already down (AppOffline, QueueFull, Disabled, connection timers).
    /// </summary>
    public class McpIncidentHttpErrSection
    {
        public bool Available { get; set; }

        /// <summary>HTTPERR timestamps are always UTC.</summary>
        public string TimestampInterpretation { get; set; }

        public string LogFolder { get; set; }
        public List<string> FilesScanned { get; set; } = new List<string>();
        public long LinesScanned { get; set; }
        public int MalformedLines { get; set; }

        public int TotalMatched { get; set; }

        /// <summary>
        /// Records that also contained the Query substring. Equals <c>TotalMatched</c> when no query was
        /// supplied. Matching runs after redaction.
        /// </summary>
        public int MatchedCount { get; set; }

        public int ReturnedCount { get; set; }
        public bool Truncated { get; set; }

        /// <summary>Counts keyed by the http.sys reason phrase, e.g. "AppOffline", "QueueFull".</summary>
        public List<McpIncidentCount> ReasonHistogram { get; set; } = new List<McpIncidentCount>();

        public List<McpHttpErrEntry> Entries { get; set; } = new List<McpHttpErrEntry>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpHttpErrEntry
    {
        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:01:12Z</c>.</summary>
        public string TimestampUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:01:12</c>.</summary>
        public string TimestampLocal { get; set; }
        public string ClientIp { get; set; }
        public string Method { get; set; }
        public string Uri { get; set; }
        public int Status { get; set; }

        /// <summary>http.sys reason, e.g. "AppOffline", "QueueFull", "Timer_ConnectionIdle".</summary>
        public string Reason { get; set; }

        public string QueueName { get; set; }
    }

    /// <summary>Windows Event Log entries from the Application and System channels. Security is never read.</summary>
    public class McpIncidentEventLogSection
    {
        public bool Available { get; set; }

        /// <summary>Event Log timestamps are stored in UTC and reported here in both forms.</summary>
        public string TimestampInterpretation { get; set; }

        public List<McpEventLogChannel> Channels { get; set; } = new List<McpEventLogChannel>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpEventLogChannel
    {
        public string LogName { get; set; }
        public bool Available { get; set; }

        /// <summary>Records the XPath query returned, before provider filtering, the Query filter, and the cap.</summary>
        public int TotalMatched { get; set; }

        /// <summary>
        /// Records that survived provider filtering AND contained the Query substring. Matching runs
        /// after redaction.
        /// </summary>
        public int MatchedCount { get; set; }

        public int ReturnedCount { get; set; }
        public bool Truncated { get; set; }

        public List<McpEventLogEntry> Entries { get; set; } = new List<McpEventLogEntry>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class McpEventLogEntry
    {
        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-27T15:01:12Z</c>.</summary>
        public string TimestampUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-27T11:01:12</c>.</summary>
        public string TimestampLocal { get; set; }
        public string LogName { get; set; }
        public int EventId { get; set; }

        /// <summary>"Critical", "Error", "Warning", "Information", or the numeric level when unmapped.</summary>
        public string Level { get; set; }

        public string ProviderName { get; set; }

        /// <summary>Rendered description, redacted and truncated to 1000 characters.</summary>
        public string Message { get; set; }
    }

    // ── Scheduled tasks + search index diagnostics DTOs ────────────────

    /// <summary>
    /// GET /mcp/scheduled-tasks — what the Sitefinity scheduler is doing right now, what runs next,
    /// and what it last finished.
    /// </summary>
    [Route("/mcp/scheduled-tasks", "GET")]
    public class GetScheduledTasks : IReturn<McpScheduledTaskStatusResponse>
    {
    }

    /// <summary>
    /// Scheduler snapshot. Every section is collected defensively — a failure in one becomes a
    /// <c>Warnings</c> entry and the rest of the response is still returned.
    /// <para>
    /// All timestamps are pre-formatted STRINGS. ServiceStack serializes a <c>DateTime</c> DTO
    /// property as <c>/Date(ms)/</c>, which destroys a server-local wall time; the UTC form is
    /// <c>yyyy-MM-ddTHH:mm:ssZ</c> and the local form is <c>yyyy-MM-ddTHH:mm:ss</c> with no suffix
    /// (the zone is stated once by <c>ServerTimeZoneId</c>).
    /// </para>
    /// </summary>
    public class McpScheduledTaskStatusResponse
    {
        /// <summary>Windows time zone id of the server, e.g. <c>Eastern Standard Time</c>.</summary>
        public string ServerTimeZoneId { get; set; }

        /// <summary>Server UTC offset in minutes at the moment this snapshot was taken.</summary>
        public int ServerUtcOffsetMinutes { get; set; }

        /// <summary>ISO 8601 UTC instant the snapshot was taken.</summary>
        public string SnapshotUtc { get; set; }

        /// <summary>Server-local wall time the snapshot was taken, with no zone suffix.</summary>
        public string SnapshotLocal { get; set; }

        /// <summary>Tasks the scheduler currently has flagged as running. Capped at 25.</summary>
        public List<McpRunningTaskInfo> RunningNow { get; set; } = new List<McpRunningTaskInfo>();

        /// <summary>
        /// Tasks whose scheduler status is <c>Failed</c>, newest execution first. Capped at 10.
        /// This is the "what is broken" section — a failed ReindexTask row is why a search index has
        /// silently stopped updating.
        /// </summary>
        public List<McpFailedTaskInfo> Failed { get; set; } = new List<McpFailedTaskInfo>();

        /// <summary>
        /// Where successful-execution history lives. Completed rows are deliberately not returned
        /// (the scheduler routinely deletes them); the trace log is the record of what actually ran.
        /// </summary>
        public string HistoryNote { get; set; }

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>One task the scheduler is executing right now.</summary>
    public class McpRunningTaskInfo
    {
        public string Id { get; set; }

        /// <summary>
        /// The task's name as the Sitefinity admin's Scheduled tasks screen shows it — its CLR type
        /// name, e.g. <c>Telerik.Sitefinity.Publishing.ReindexTask</c>. Read from
        /// <c>ScheduledTaskData.TaskName</c>, which is always populated.
        /// </summary>
        public string Name { get; set; }

        /// <summary>Human title, falling back to the name when the row has none.</summary>
        public string Title { get; set; }

        /// <summary>
        /// What the task is operating on, when the row names it — a ReindexTask row carries the search
        /// index name here (e.g. <c>Docs Index</c>). Null when the row has no title of its own.
        /// </summary>
        public string ItemName { get; set; }

        public string Description { get; set; }

        /// <summary>Scheduler key — for a reindex this usually carries the publishing point identity.</summary>
        public string Key { get; set; }

        /// <summary>ISO 8601 UTC, e.g. <c>2026-08-28T15:01:12Z</c>.</summary>
        public string StartedUtc { get; set; }

        /// <summary>Server-local wall time with NO zone suffix, e.g. <c>2026-08-28T11:01:12</c>.</summary>
        public string StartedLocal { get; set; }

        /// <summary>
        /// Which column the start time was taken from — <c>LastExecutedTime</c>, <c>ExecuteTime</c> or
        /// <c>LastModified</c>. Sitefinity has no dedicated "started" column, so this states the proxy
        /// used rather than pretending to a precision it does not have.
        /// </summary>
        public string StartedSource { get; set; }

        /// <summary>Seconds elapsed since <c>StartedUtc</c>. A large value on a short task means it is hung.</summary>
        public long RunningForSeconds { get; set; }

        /// <summary>Whether this task is rebuilding a search index.</summary>
        public bool IsSearchIndexRebuild { get; set; }

        /// <summary>Search index (catalog) being rebuilt, when one could be identified.</summary>
        public string IndexName { get; set; }

        /// <summary>Percentage complete, when this Sitefinity version reports it.</summary>
        public int? Progress { get; set; }

        /// <summary>Scheduler status value, when this Sitefinity version reports it.</summary>
        public string Status { get; set; }

        /// <summary>Scheduler status message, when this Sitefinity version reports it.</summary>
        public string StatusMessage { get; set; }
    }

    /// <summary>One task the scheduler has marked as failed.</summary>
    public class McpFailedTaskInfo
    {
        public string Id { get; set; }

        /// <summary>
        /// The task's name as the admin's Scheduled tasks screen shows it — its CLR type name. Read
        /// from <c>ScheduledTaskData.TaskName</c>.
        /// </summary>
        public string Name { get; set; }

        /// <summary>Human title, falling back to the name when the row has none.</summary>
        public string Title { get; set; }

        /// <summary>
        /// What the task was operating on — for a failed ReindexTask this is the search index name.
        /// </summary>
        public string ItemName { get; set; }

        /// <summary>Scheduler key.</summary>
        public string Key { get; set; }

        /// <summary>ISO 8601 UTC the task was scheduled for, e.g. <c>2026-08-08T15:01:12Z</c>.</summary>
        public string ScheduledForUtc { get; set; }

        /// <summary>Server-local wall time the task was scheduled for, with NO zone suffix.</summary>
        public string ScheduledForLocal { get; set; }

        /// <summary>ISO 8601 UTC the failed execution ran. Null when the row never recorded one.</summary>
        public string ExecutedOnUtc { get; set; }

        /// <summary>Server-local wall time the failed execution ran, with NO zone suffix.</summary>
        public string ExecutedOnLocal { get; set; }

        /// <summary>Scheduler status value — <c>Failed</c> for every row in this list.</summary>
        public string Status { get; set; }

        /// <summary>Scheduler status message, redacted and truncated. Usually the failure reason.</summary>
        public string StatusMessage { get; set; }

        /// <summary>Whether the failed task was rebuilding a search index.</summary>
        public bool IsSearchIndexRebuild { get; set; }

        /// <summary>Search index (catalog) the failed rebuild targeted, when one could be identified.</summary>
        public string IndexName { get; set; }
    }

    /// <summary>
    /// GET /mcp/search-indexes — every configured search index, its backend, and whether it is
    /// currently being rebuilt.
    /// </summary>
    [Route("/mcp/search-indexes", "GET")]
    public class GetSearchIndexes : IReturn<McpSearchIndexesResponse>
    {
    }

    /// <summary>
    /// Search index inventory. Every field is best-effort: the search API differs across Lucene,
    /// Azure Search, Elasticsearch and hybrid backends, so anything unobtainable is left null and
    /// explained in <c>Warnings</c> rather than failing the call.
    /// </summary>
    public class McpSearchIndexesResponse
    {
        public string ServerTimeZoneId { get; set; }

        public int ServerUtcOffsetMinutes { get; set; }

        /// <summary>CLR type name of the resolved search service, e.g. <c>LuceneSearchService</c>.</summary>
        public string SearchServiceType { get; set; }

        /// <summary>
        /// Publishing providers that were queried for search-index pipes. Search indexes live under
        /// their OWN publishing provider (<c>SearchPublishingProvider</c>), not the default one, so
        /// this names every provider searched — the first thing to check if the list comes back empty.
        /// </summary>
        public List<string> ProvidersScanned { get; set; } = new List<string>();

        public int TotalIndexes { get; set; }

        public List<McpSearchIndexInfo> Indexes { get; set; } = new List<McpSearchIndexInfo>();

        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>One configured search index (a search-index pipe on a publishing point).</summary>
    public class McpSearchIndexInfo
    {
        /// <summary>Catalog name — the value a search widget or query targets, e.g. <c>docs-index</c>.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Display name as the admin's "Search indexes" screen shows it, e.g. <c>Docs Index</c>. This
        /// is the name scheduled-task rows use, and it is never blank — it falls back through the
        /// publishing point's title and name to the catalog name.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Where <c>Title</c> came from: <c>PublishingPointTitle</c>, <c>PublishingPointName</c>,
        /// <c>PipeTitle</c>, <c>PipeUIName</c>, or <c>derived</c> when nothing on the pipe or its point
        /// named the index in human terms and the title was humanized from the catalog name
        /// (<c>docs-index</c> to <c>Docs Index</c>). A pipe-type label such as <c>SearchIndexPipe</c>
        /// is never emitted as a title.
        /// </summary>
        public string TitleSource { get; set; }

        /// <summary>Publishing point that owns the index pipe.</summary>
        public string PublishingPointName { get; set; }

        /// <summary>Publishing provider the point lives under, e.g. <c>SearchPublishingProvider</c>.</summary>
        public string PublishingProvider { get; set; }

        /// <summary>
        /// Search provider serving this index, from the pipe's <c>SearchProviderName</c>. Falls back
        /// to the concrete search service type (decorators unwrapped) when the pipe names none.
        /// </summary>
        public string Backend { get; set; }

        /// <summary>Whether both the publishing point and its index pipe are active.</summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Whether the backend reports the index as existing. Null when the search service could not
        /// be resolved or does not answer the question.
        /// </summary>
        public bool? Exists { get; set; }

        /// <summary>
        /// Number of documents in the index, when the backend exposes a count. Null otherwise —
        /// the public search API has no portable document-count call.
        /// </summary>
        public long? DocumentCount { get; set; }

        /// <summary>ISO 8601 UTC of the last known index update, when obtainable.</summary>
        public string LastUpdatedUtc { get; set; }

        /// <summary>Server-local wall time of the last known index update, with no zone suffix.</summary>
        public string LastUpdatedLocal { get; set; }

        /// <summary>Where <c>LastUpdatedUtc</c> came from — <c>IndexFolder</c> or <c>LastPublicationDate</c>.</summary>
        public string LastUpdatedSource { get; set; }

        /// <summary>Whether a scheduler task is rebuilding this index right now.</summary>
        public bool IsRebuilding { get; set; }

        /// <summary>The rebuilding task's type, when one was matched.</summary>
        public string RebuildTaskType { get; set; }

        /// <summary>The rebuilding task's reported progress, when available.</summary>
        public int? RebuildProgress { get; set; }

        /// <summary>
        /// Outcome of the most recent reindex task the scheduler still holds for this index:
        /// <c>running</c>, <c>failed</c>, <c>completed</c>, or <c>unknown</c> when no row survives.
        /// A <c>failed</c> value is the usual reason a search index has silently gone stale.
        /// </summary>
        public string LastReindexStatus { get; set; }

        /// <summary>ISO 8601 UTC the last known reindex ran, when a row records it.</summary>
        public string LastReindexUtc { get; set; }

        /// <summary>Server-local wall time the last known reindex ran, with NO zone suffix.</summary>
        public string LastReindexLocal { get; set; }

        /// <summary>
        /// What feeds this index — the inbound pipes on its publishing point, named by content type
        /// where the pipe settings expose one. Empty when the publishing point declares none.
        /// </summary>
        public List<string> ContentSources { get; set; } = new List<string>();
    }
}

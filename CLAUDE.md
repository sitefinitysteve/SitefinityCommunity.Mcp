# SitefinityCommunity.Mcp — Claude Code Instructions

## What This Project Is

An MCP (Model Context Protocol) server that gives Claude Code direct access to Sitefinity CMS logs, diagnostics, and status. It runs as a .NET console app communicating via stdio, and exposes tools that Claude Code can call.

**Two components:**
1. **MCP Server** (`src/SitefinityCommunity.Mcp/`) — .NET 10 console app using the official ModelContextProtocol SDK
2. **Sitefinity Plugin** (`src/SitefinityCommunity.Mcp.SitefinityPlugin/`) — `.cs` source files dropped into a Sitefinity web app that expose REST endpoints at `/RestApi/mcp/*`

## Project Structure

```
SitefinityCommunity.Mcp/
├── CLAUDE.md                          ← You are here
├── README.md                          ← User-facing docs
├── install-plugin.ps1                 ← Copies plugin files to a Sitefinity project
├── install-skills.ps1                 ← Offline installer: Claude/Cursor/Codex/Copilot (project or global) — fallback to `npx skills`
├── SitefinityCommunity.Mcp.slnx       ← Solution file
├── skills/                            ← 16 Agent Skills (`npx skills add github:sitefinitysteve/SitefinityCommunity.Mcp`, or install-skills.ps1)
│   ├── sitefinity-best-practices/
│   │   └── SKILL.md                  ← Read-this-first foundation + skill router
│   ├── sitefinity-widget-expert/
│   │   └── SKILL.md                  ← MVC widget development guidance
│   └── ...                           ← 13 more (page, data, API, Vue3, build, debloat, icons)
├── tests/
│   ├── test-config.example.json       ← Template (committed)
│   ├── test-config.json               ← Your dev config (gitignored)
│   └── SitefinityCommunity.Mcp.Tests/ ← xUnit test project
│       ├── SitefinityFixture.cs       ← Shared fixture (loads config, builds DI)
│       ├── Helpers/
│       │   └── MockHttpMessageHandler.cs
│       ├── Integration/               ← Tests requiring running Sitefinity
│       │   ├── MetadataServiceTests.cs
│       │   └── StatusServiceTests.cs
│       └── Unit/                      ← Offline tests with mocked HTTP
│           ├── MetadataServiceUnitTests.cs
│           ├── RouteToolsUnitTests.cs
│           ├── PageToolsUnitTests.cs
│           └── SitefinityDocsResourcesTests.cs
└── src/
    ├── SitefinityCommunity.Mcp/       ← THE MCP SERVER
    │   ├── Program.cs                 ← Entry point, DI, MCP server config, tool filter
    │   ├── Configuration/
    │   │   └── SitefinityMcpConfig.cs ← Config model (loads from sitefinity-mcp.json)
    │   ├── Models/
    │   │   ├── LogEntry.cs            ← Parsed log entry
    │   │   ├── LogFileInfo.cs         ← Log file metadata
    │   │   ├── LogSearchResult.cs     ← Search match result
    │   │   ├── SitefinityHealthResponse.cs
    │   │   ├── SiteInfoResponse.cs    ← Site info + SiteEntry for multisite
    │   │   ├── ModuleInfo.cs          ← Installed module metadata
    │   │   ├── DynamicTypeInfo.cs     ← Module Builder type metadata
    │   │   ├── DynamicFieldInfo.cs    ← Dynamic type field definition
    │   │   ├── RoutesResponse.cs    ← Page routes, API routes, OData routes
    │   │   ├── PageDetailsResponse.cs  ← Page details + PageWidgetInfo with Settings
    │   │   ├── WidgetPropertiesResponse.cs ← Single widget full properties response
    │   │   ├── PageWidgetTreeResponse.cs ← Widget tree in render order + nested placeholders
    │   │   ├── ContentItemInfo.cs / ContentListResponse.cs ← Live content queries
    │   │   ├── PageTemplateInfo.cs / TemplatesResponse.cs ← Page templates
    │   │   ├── TaxonomyInfo.cs / TaxonInfo.cs / TaxonomiesResponse.cs ← Classifications
    │   │   ├── FormInfo.cs / FormFieldInfo.cs / FormResponseInfo.cs ← Forms + submissions
    │   ├── Services/
    │   │   ├── IEnvironmentResolver.cs    ← Resolves named environments
    │   │   ├── EnvironmentResolver.cs     ← Tracks active default environment
    │   │   ├── ILogProvider.cs            ← Abstract log access interface
    │   │   ├── ILogProviderFactory.cs     ← Creates local/remote providers
    │   │   ├── LogProviderFactory.cs      ← Factory implementation
    │   │   ├── LocalLogProvider.cs        ← Reads logs from filesystem
    │   │   ├── RemoteLogProvider.cs       ← Reads logs via HTTP plugin endpoints
    │   │   ├── LogParsingService.cs       ← Parses Sitefinity's 40-dash-separated log format
    │   │   ├── ISitefinityStatusService.cs
    │   │   ├── SitefinityStatusService.cs ← Polls /RestApi/systemstatus
    │   │   ├── ApiKeyValidationService.cs ← Validates API keys via /RestApi/mcp/ping
    │   │   ├── ISitefinityMetadataService.cs ← Interface for metadata operations
    │   │   └── SitefinityMetadataService.cs  ← HTTP client for metadata endpoints
    │   ├── Security/
    │   │   └── SecretRedactor.cs      ← Deny-list + pattern scanner for secrets
    │   ├── Tools/                     ← MCP TOOLS (auto-discovered)
    │   │   ├── LogTools.cs            ← read_error_log, read_trace_log, list_log_files, etc.
    │   │   ├── EnvironmentTools.cs    ← list_environments, set_default_environment
    │   │   ├── SitefinityStatusTools.cs ← check_status
    │   │   ├── SitefinityInfoTools.cs ← get_site_info, list_modules
    │   │   ├── ContentTypeTools.cs    ← list_dynamic_types, get_type_fields, get_module_structure
    │   │   ├── RouteTools.cs          ← list_page_routes, list_api_routes
    │   │   ├── PageTools.cs           ← get_page_details, get_widget_properties, get_page_widget_tree
    │   │   ├── ContentTools.cs        ← list_content
    │   │   ├── TemplateTools.cs       ← list_templates
    │   │   ├── TaxonomyTools.cs       ← list_taxonomies
    │   │   ├── FormTools.cs           ← list_forms, get_form_fields, list_form_responses
    │   │   ├── ConfigTools.cs         ← list_config_sections, get_config_section
    │   │   ├── WhereUsedTools.cs      ← where_used (reverse lookup)
    │   │   ├── PermissionTools.cs     ← get_permissions
    │   │   └── MaintenanceTools.cs    ← clear_cache, recycle_app (WRITE; gated)
    │   ├── Resources/                 ← MCP RESOURCES (auto-discovered)
    │   │   └── SitefinityDocsResources.cs ← Widget designer attributes reference
    │   └── Docs/                      ← Embedded resource files
    │       └── WidgetDesignerAttributes.md ← Sitefinity widget attribute reference
    │
    └── SitefinityCommunity.Mcp.SitefinityPlugin/  ← SITEFINITY PLUGIN (source files)
        ├── McpInit.cs                 ← Registration (checks Enabled + ApiKey before registering)
        ├── McpConfig.cs               ← Sitefinity config section (Admin > Advanced > McpSettings)
        ├── McpApiKeyAttribute.cs      ← Request filter validating X-MCP-API-Key header
        ├── McpServicePlugin.cs        ← ServiceStack plugin registration
        ├── McpLogRequest.cs           ← Request/response DTOs (logs + metadata)
        ├── McpLogService.cs           ← ServiceStack service handlers (logs)
        ├── McpMetadataService.cs      ← ServiceStack service handlers (site info, modules, types, widget tree, templates, taxonomies)
        ├── McpContentService.cs       ← ServiceStack service handler (live content queries)
        ├── McpFormsService.cs         ← ServiceStack service handlers (forms + responses)
        ├── McpConfigService.cs        ← ServiceStack service handlers (config section reader; redacted)
        ├── McpWhereUsedService.cs     ← ServiceStack service handler (reverse lookup)
        ├── McpPermissionsService.cs   ← ServiceStack service handler (effective permissions)
        ├── McpMaintenanceService.cs   ← ServiceStack service handlers (clear cache / recycle; WRITE, gated)
        ├── McpSecretRedactor.cs       ← .NET 4.8 mirror of SecretRedactor (scrubs forms/widgets/config)
        └── README.md                  ← Plugin installation guide
```

## How to Build and Run

```bash
# Build
dotnet build

# Run (requires SITEFINITY_MCP_CONFIG env var)
SITEFINITY_MCP_CONFIG=/path/to/sitefinity-mcp.json dotnet run --project src/SitefinityCommunity.Mcp
```

The server communicates via stdio (stdin/stdout) using the MCP protocol. All logging goes to stderr.

## Configuration File (sitefinity-mcp.json)

```json
{
    "defaultEnvironment": "dev",
    "environments": {
        "dev": {
            "url": "https://dev.example.com",
            "logsPath": "C:\\Path\\To\\App_Data\\Sitefinity\\Logs",
            "sitefinityApiKey": "must-match-sitefinity-config",
            "allowWriteOperations": true
        },
        "staging": {
            "url": "https://staging.example.com",
            "sitefinityApiKey": "must-match-sitefinity-config"
        }
    }
}
```

- **`sitefinityApiKey`** — Required for every environment. Must match the key in Sitefinity Admin > Settings > Advanced > McpSettings
- **`logsPath`** — When set, reads logs directly from filesystem (local mode). When omitted, uses HTTP via the plugin (remote mode)
- **`url`** — Required. The Sitefinity site URL
- **`allowWriteOperations`** — Optional (default false). Permits `sitefinity_clear_cache` / `sitefinity_recycle_app`. Ignored for prod-like names. Also requires "Allow Write Operations" enabled in the Sitefinity admin.

> Note: there is **no** flag to disable secret redaction. Logs and config dumps always redact credentials in every environment, including dev.

## How to Add a New Tool

Tools are auto-discovered via `[McpServerToolType]` — no changes to `Program.cs` needed.

### Step 1: Create a tool class in `Tools/`

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

[McpServerToolType]
public sealed class MyNewTools
{
    private readonly IEnvironmentResolver _resolver;

    // Constructor injection — any registered service can be injected
    public MyNewTools(IEnvironmentResolver resolver)
    {
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_my_tool", ReadOnly = true)]
    [Description("What this tool does — Claude reads this to decide when to use it.")]
    public async Task<string> MyTool(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        var (envName, config) = this._resolver.Resolve(environment);
        // ... your logic ...
        return "result text";
    }
}
```

### Conventions

- Tool names: `sitefinity_` prefix, snake_case (e.g. `sitefinity_read_error_log`)
- Return type: `string` or `Task<string>` — plain text, not JSON
- Include `string? environment = null` parameter on tools that target a specific environment
- Include `CancellationToken ct = default` for async operations
- Use `[Description]` on the class method AND on parameters — Claude uses these descriptions
- Set `ReadOnly = true` for tools that don't modify state
- Wrap external calls in try/catch and return error messages as strings (don't let exceptions propagate)

### Step 2: If your tool needs a new service

1. Create interface + implementation in `Services/`
2. Register in `Program.cs`: `builder.Services.AddSingleton<IMyService, MyService>();`

### Step 3: If your tool needs a new REST endpoint in Sitefinity

1. Add a request DTO to `SitefinityPlugin/McpLogRequest.cs`
2. Add a handler to `SitefinityPlugin/McpLogService.cs`
3. Copy updated files to the installed location in the Sitefinity project

## How to Add a New Resource

Resources are auto-discovered via `[McpServerResourceType]` — registered in `Program.cs` with `.WithResourcesFromAssembly()`.

Resources provide static reference material that clients can read on demand (unlike tools which perform actions). Content is compiled into the assembly as embedded resources.

### Step 1: Add the content file to `Docs/`

Place the markdown (or other content) file in `src/SitefinityCommunity.Mcp/Docs/` and register it as an embedded resource in the `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Docs\YourFile.md" />
</ItemGroup>
```

### Step 2: Create a resource class in `Resources/`

```csharp
using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;

namespace SitefinityCommunity.Mcp.Resources;

[McpServerResourceType]
public sealed class MyResources
{
    private static readonly Lazy<string> Content = new(() =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("SitefinityCommunity.Mcp.Docs.YourFile.md")
            ?? throw new InvalidOperationException("Embedded resource not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    [McpServerResource(
        Name = "sitefinity_my_resource",
        Title = "Human-Readable Title",
        MimeType = "text/markdown")]
    [Description("What this resource contains — clients use this to decide when to read it.")]
    public static string GetMyResource() => Content.Value;
}
```

### Conventions

- Resource names: `sitefinity_` prefix, snake_case
- Return type: `string` for text content
- Use `Lazy<string>` to load embedded resources once and cache
- Use `[Description]` — Claude uses this to decide when the resource is relevant
- Embedded resource names follow: `{RootNamespace}.{FolderPath}.{FileName}` (dots for path separators)

## Key Architecture Decisions

### Dual-Mode Log Access

Each environment can use either:
- **Local mode** (`logsPath` set) — `LocalLogProvider` reads `.log` files directly from disk. Used for dev where the MCP server runs on the same machine as Sitefinity.
- **Remote mode** (`logsPath` not set) — `RemoteLogProvider` makes HTTP calls to `/RestApi/mcp/*` endpoints exposed by the Sitefinity plugin. Used for staging/prod.

The `LogProviderFactory` picks the right one based on config.

### Security: API Keys and Enabled Flag

**API Key Validation:**
The `ApiKeyValidationService` proactively validates that the MCP server's key matches Sitefinity's key by calling `GET /RestApi/mcp/ping`. `Valid` and `InvalidKey` results are cached for 5 minutes; `Unreachable` is cached for only 15 seconds (so the server retries quickly during cold starts). The ping also detects bootstrapping redirects — if Sitefinity redirects to `/sitefinity/status` or returns HTML instead of JSON, it's treated as `Unreachable` (not falsely `Valid`).

The `CallToolFilter` in `Program.cs` runs this check before every tool call:
- **Valid** — proceed normally
- **InvalidKey** — return clear error message (no tools execute)
- **Unreachable** — behavior depends on the tool:
  - **Local-only tools** (`sitefinity_list_environments`, `sitefinity_set_default_environment`) and `sitefinity_check_status` — proceed immediately without waiting
  - **All other tools** — call `WaitForReadyAsync` (90s timeout), then re-validate the API key. If ready + valid, proceed. If ready + invalid key, return error. If still unreachable, warn and allow (so local log tools still work)

**Enabled Flag (Sitefinity Admin > Advanced > McpSettings):**
Two-layer enforcement ensures MCP endpoints can be fully disabled:

1. **Startup gate** (`McpInit.Register`) — If `Enabled` is `false` OR `ApiKey` is blank, the ServiceStack plugin is never registered. Routes don't exist (404). Config section is always registered so the admin UI stays accessible. Requires app pool recycle to take effect.
2. **Runtime gate** (`[McpApiKey]` attribute) — Checks `Enabled` on every request. Blocks immediately if disabled in admin, even without a recycle.

**Blank key rejection at every layer:**
- MCP server startup: `SitefinityMcpConfig.Validate()` uses `IsNullOrWhiteSpace` — rejects `""`, `null`, `"   "`
- Sitefinity startup: `McpInit.Register()` checks `IsNullOrWhiteSpace(config.ApiKey)` — skips plugin registration
- Sitefinity runtime: `McpApiKeyAttribute` checks `IsNullOrWhiteSpace` on both the config key and the request header

**Important for new plugin services:** Always apply `[McpApiKey]` at the class level on any new ServiceStack service. This ensures the `Enabled` and key checks are enforced automatically on all endpoints in that service.

### Plugin Source Files (Not a NuGet Package)

Sitefinity bundles pinned versions of ServiceStack, Newtonsoft.Json, etc. that change across versions. A precompiled DLL would cause binding redirect hell. Source files compile against whatever assemblies the host Sitefinity project references — zero conflicts.

### Log Parsing

Sitefinity logs use a format where entries are separated by 40-dash lines (`----------------------------------------`). The `LogParsingService` uses source-generated regexes to extract structured fields (timestamp, severity, type, URL, stack trace, etc.) from the raw text blocks.

### Secret Redaction (Defense in Depth)

Everything this MCP returns lands in an LLM context window, which may be logged, cached, or transmitted to third-party model providers. Two mirrored redaction classes enforce scrubbing before text leaves either side:

- **MCP server side** — `Security/SecretRedactor.cs` (.NET 10, source-generated regex). Wired into `LocalLogProvider.ReadFileAsync` + `SearchAsync` so filesystem log reads are scrubbed before parsing.
- **Plugin side** — `McpSecretRedactor.cs` (.NET 4.8, compiled regex). Wired into `McpFormsService` (field names + values) and planned for `McpMetadataService` widget properties.

Redaction has two layers:
1. **Field-name deny-list** — exact matches (`password`, `apikey`, `token`, `authorization`, …) and substring fragments (`*secret*`, `*password*`) replace the whole value with `[REDACTED]`.
2. **Value-pattern scanner** — regex matches for JWTs, bearer headers, AWS/GitHub/Slack/OpenAI tokens, Azure storage keys, connection-string passwords, App Insights instrumentation keys.

**Redaction is UNCONDITIONAL everywhere — there is no opt-out.** Logs (`LocalLogProvider.ReadFileAsync` / `SearchAsync`) always run through `SecretRedactor.Redact` in every environment, including dev. There is intentionally no `allowRawSecrets` flag — a raw secret in the LLM context is a leak (it can be logged, cached, or absorbed into model training data), so the server never emits one.

**Config reader is UNCONDITIONAL (no opt-out):** the config dump (`/mcp/config/{SectionName}`) **always** redacts anything credential-shaped — `[SecretData]`/encrypted properties, connection strings, and any path/leaf name containing key/secret/token/password/etc. — in **every** environment including dev. There is no flag to reveal config secrets. A raw secret/key/password in the LLM context is a leak (it can be logged, cached, or absorbed into model training data), so `McpConfigService.BuildEntry` over-redacts deliberately.

**Important:** any new tool returning user-authored content (widget properties, content fields, form submissions, logs, config values) must route string values through the redactor on the side closest to the data source. Credentials/keys/passwords must never reach the LLM in raw form, regardless of environment.

### Write Operations (Cache Clear / Recycle)

The only state-changing tools (`sitefinity_clear_cache`, `sitefinity_recycle_app`) are gated on **both** sides and never run by default:

1. **MCP server side** — per-environment `allowWriteOperations: true` in `sitefinity-mcp.json`. `EnvironmentConfig.EffectiveAllowWriteOperations(name)` is prod-guarded (always false for names starting with `prod`). `MaintenanceTools` refuses before any network call when this is false.
2. **Plugin side** — `McpConfig.AllowWriteOperations` (Admin > Advanced > McpSettings > "Allow Write Operations", default false). `McpMaintenanceService.EnsureWriteAllowed()` returns HTTP 403 when off.

Both must opt in. Cache APIs vary across Sitefinity versions, so `McpMaintenanceService` invokes `ClearWholeCache` / `CacheDependency.Notify` / `RestartApplication` **reflectively** and reports in the response exactly what it managed to do.

## Available Services (for DI)

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `IEnvironmentResolver` | `EnvironmentResolver` | Resolve environment by name, track default |
| `ILogProviderFactory` | `LogProviderFactory` | Create local/remote log provider per environment |
| `ILogProvider` | `LocalLogProvider` / `RemoteLogProvider` | List, read, search log files |
| `LogParsingService` | (concrete) | Parse Sitefinity log format into structured entries |
| `ISitefinityStatusService` | `SitefinityStatusService` | Check if Sitefinity is bootstrapped; `WaitForReadyAsync` polls until ready or timeout |
| `IApiKeyValidationService` | `ApiKeyValidationService` | Validate API keys via ping endpoint |
| `ISitefinityMetadataService` | `SitefinityMetadataService` | Fetch site info, modules, dynamic types, fields, config sections, where-used, permissions; clear cache / recycle |
| `IHttpClientFactory` | (framework) | Create HTTP clients for remote calls |
| `SitefinityMcpConfig` | (concrete) | Loaded config singleton |

## Plugin Endpoint Reference

All endpoints require `X-MCP-API-Key` header. Protected by `[McpApiKey]` attribute.

| Route | Method | Description |
|-------|--------|-------------|
| `/mcp/ping` | GET | Lightweight key validation — returns `{ status: "ok" }` |
| `/mcp/logs` | GET | List all log files with metadata |
| `/mcp/logs/{FileName}` | GET | Read a log file (optional `MaxLines` query param) |
| `/mcp/logs/search` | POST | Search logs with a regex pattern. Searches `*.log` files **newest-first** and stops after `MaxMatches` hits (default 200, max 1000) so large prod log sets don't time the client out. Optional `FileName` restricts the search to a single file (e.g. `Error.log`). Streams each file line-by-line — flat memory regardless of file size |
| `/mcp/logs/last-error` | GET | Most recent error log entry |
| `/mcp/site-info` | GET | Sitefinity version, .NET version, project name, languages, multisite |
| `/mcp/modules` | GET | All installed modules with type, status, startup type |
| `/mcp/dynamic-types` | GET | All Module Builder types grouped by module |
| `/mcp/dynamic-types/{TypeFullName}/fields` | GET | Fields for a specific dynamic type |
| `/mcp/modules/{ModuleName}/structure` | GET | Full module tree: nested parent/child types with fields + CLR type hints (POCO-ready) |
| `/mcp/routes` | GET | Combined page + API routes (backward compat) |
| `/mcp/page-routes` | GET | CMS page routes with URL evaluation warnings |
| `/mcp/api-routes` | GET | ServiceStack API routes and OData entity sets |
| `/mcp/page-details` | GET | Full page details with widgets and properties (Level 1 + Level 2 Settings) |
| `/mcp/widgets/{WidgetId}/properties` | GET | Full widget properties with both Level 1 and Level 2 Settings children (requires `PageIdentifier` query param) |
| `/mcp/page-widget-tree` | GET | Widget tree in render order, nested placeholders, merged L1+L2 props (requires `PageIdentifier`; optional `IncludeLayoutControls`) |
| `/mcp/content` | GET | List content items of a given type (requires `TypeFullName`; optional `Take`, `Skip`) |
| `/mcp/templates` | GET | List page templates (MVC + WebForms) |
| `/mcp/taxonomies` | GET | List classifications with top-level taxa |
| `/mcp/forms` | GET | List all forms with metadata counts |
| `/mcp/forms/{FormIdentifier}/fields` | GET | Field definitions for a form. Optional `Debug=true` to include a raw Properties/ChildProperties tree dump for diagnosing empty Name/Title on unfamiliar Sitefinity versions |
| `/mcp/forms/{FormIdentifier}/responses` | GET | Paged form submissions (secret-redacted). Optional `SearchTerm` filters to entries where any field value (or IP / UserAgent) contains the term (case-insensitive; matching runs **after** redaction so sensitive values cannot leak via search). Response includes `TotalCount` (all entries), `MatchedCount` (after filter), and echoes `SearchTerm` |
| `/mcp/config` | GET | List all registered configuration section names (discovered by scanning `ConfigSection`-derived types in the AppDomain) |
| `/mcp/config/{SectionName}` | GET | Flattened dump of a config section. Credential-like values (keys, passwords, connection strings, tokens, `[SecretData]`/encrypted properties) are **ALWAYS redacted** and never returned — in every environment, with no flag to reveal them |
| `/mcp/where-used` | GET | Reverse lookup: every page/template referencing a widget type, content item, or template (requires `Query`; optional `Kind`=widget\|content\|template) |
| `/mcp/permissions` | GET | Effective per-role granted/denied actions on a page or content item, and whether it inherits (requires `Identifier`; optional `TypeFullName` for a content item) |
| `/mcp/cache/clear` | POST | **Write.** Clear cache: `Scope`=output\|whole\|page (`PageIdentifier` required for page). Refused (403) unless `AllowWriteOperations` is enabled in admin |
| `/mcp/app/recycle` | POST | **Write.** Restart the Sitefinity application (`SystemManager.RestartApplication`). Refused (403) unless `AllowWriteOperations` is enabled in admin |

## Coding Conventions

- **`this.` prefix** — Always use `this.` when accessing instance members
- **Properties at bottom** — Class organization: constructor, methods, properties
- **File-scoped namespaces** — Use `namespace X;` not `namespace X { }`
- **Primary constructors** — Prefer primary constructors for tool classes with simple DI
- **Readable control flow** — Do not compress `if`, `else`, `catch`, `for`, `foreach`, or `while` bodies onto one line
- **Braces required** — Always use braces for control-flow blocks, even for single-statement guards
- **Whitespace around conditionals** — Leave a blank line above and below standalone `if` blocks when it improves scanning and keeps guard clauses from feeling packed together
- **Keep simple calls compact** — Do not split ordinary function or method argument lists across multiple lines unless the call is genuinely long or hard to scan
- **Nullable enabled** — Project has `<Nullable>enable</Nullable>`
- **No manual JSON serialization** — Use `System.Text.Json` with source generators where applicable
- **Target framework** — .NET 10 (`net10.0`)
- **MCP SDK** — `ModelContextProtocol` v0.8.0-preview.1

## Testing Locally

1. Set a matching API key in `sitefinity-mcp.json` and in Sitefinity Admin > Advanced > McpSettings
2. Run the MCP server with the config file path
3. Verify with Claude Code — tools like `sitefinity_list_environments` don't need Sitefinity running
4. Log tools need either a local `logsPath` or a running Sitefinity with the plugin installed

## Automated Tests

The test project is at `tests/SitefinityCommunity.Mcp.Tests/`.

### Setup

1. Copy `tests/test-config.example.json` to `tests/test-config.json`
2. Fill in your Sitefinity dev URL and API key (gitignored)

### Running Tests

```bash
# All tests (unit + integration)
dotnet test

# Unit tests only (no Sitefinity needed)
dotnet test --filter "Category=Unit"

# Integration tests only (requires running Sitefinity)
dotnet test --filter "Category=Integration"
```

### Test Categories

- **Unit** (`Trait("Category", "Unit")`) — Mock HTTP responses, NSubstitute for service interfaces. Always pass offline.
- **Integration** (`Trait("Category", "Integration")`) — Hit a real Sitefinity instance. Use `[SkippableFact]` + `Skip.If(!fixture.IsAvailable)` to skip when config is missing or Sitefinity is unreachable.

### Shared Fixture

`SitefinityFixture` (IAsyncLifetime, `[CollectionDefinition("Sitefinity")]`) loads `test-config.json`, builds a DI container, waits for Sitefinity readiness, and validates the API key — all once per test run.

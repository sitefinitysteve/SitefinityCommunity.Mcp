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
    │   │   ├── IncidentWindowResponse.cs ← Incident envelope (window / candidates / search) + all four source sections
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
    │   │   ├── LogTools.cs            ← read_log_file (defaults to Error.log), search_logs, list_log_files
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
    │   │   ├── IncidentTools.cs       ← investigate_incident (SF + IIS + EventLog + HTTPERR correlation)
    │   │   └── MaintenanceTools.cs    ← clear_cache, recycle_app (WRITE; gated)
    │   ├── Resources/                 ← MCP RESOURCES (auto-discovered)
    │   │   └── SitefinityDocsResources.cs ← Widget designer attributes reference
    │   └── Docs/                      ← Embedded resource files
    │       └── WidgetDesignerAttributes.md ← Sitefinity widget attribute reference
    │
    └── SitefinityCommunity.Mcp.SitefinityPlugin/  ← SITEFINITY PLUGIN (source files)
        ├── McpInit.cs                 ← Registration (checks Enabled + ApiKey before registering)
        ├── McpConfig.cs               ← Config section + per-capability tool elements + McpCapabilities guard
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
        ├── McpSystemLogService.cs     ← ServiceStack service handler (incident window / candidate discovery / cross-source search: SF + IIS + Event Log + HTTPERR)
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

- Tool names: `sitefinity_` prefix, snake_case (e.g. `sitefinity_read_log_file`)
- **Prefer a parameter over a near-duplicate tool.** Every tool costs the agent a choice on every call. `read_error_log` / `read_trace_log` / `get_last_error` were collapsed into `sitefinity_read_log_file` (default `fileName`, `count: 1` for the latest) because they differed only by an argument. Split a tool out only when it does something genuinely different, not when it does the same thing with a preset.
- Return type: a typed response model (`Task<MyResponse>`) with `UseStructuredContent = true` when the tool returns data — the SDK publishes an output schema and structured content automatically. Use `Task<string>` only for human-formatted text output (logs, status summaries, markdown tables)
- Include `string? environment = null` parameter on tools that target a specific environment
- Include `CancellationToken ct = default` for async operations
- Use `[Description]` on the class method AND on parameters — Claude uses these descriptions
- Set `ReadOnly = true` for tools that don't modify state
- Wrap external calls in try/catch and rethrow as `McpException` with a clear message — the SDK returns it as an isError result. Typed tools cannot return error strings

### Step 2: If your tool needs a new service

1. Create interface + implementation in `Services/`
2. Register in `Program.cs`: `builder.Services.AddSingleton<IMyService, MyService>();`

### Step 3: If your tool needs a new REST endpoint in Sitefinity

1. Add a request DTO to `SitefinityPlugin/McpLogRequest.cs`
2. Add a handler to `SitefinityPlugin/McpLogService.cs`
3. Copy updated files to the installed location in the Sitefinity project

### Step 4: Register the tool's capability (REQUIRED)

**A new tool or endpoint is not accepted without its capability entry.** Every capability is admin-toggleable, so a tool that skips this is silently un-disableable. Four places, all small:

1. **Config element** - a `McpToolElement` subclass (`Mcp<Name>ToolElement`, default `Enabled = true`) plus its typed `[ConfigurationProperty]` on `McpConfig`, both in `SitefinityPlugin/McpConfig.cs`. Reuse the existing element if the tool is backed by an existing plugin service.
2. **Plugin guard** - `McpCapabilities.EnsureEnabled(McpCapabilities.<Name>)` as the first line of the handler, plus the name constant and the `IsEnabled` switch arm in `McpCapabilities` (also in `McpConfig.cs`).
3. **Ping roster** - a property on `McpFeatureRoster` (`SitefinityPlugin/McpLogRequest.cs`), populated in `McpCapabilities.BuildRoster`, mirrored on `Models/FeatureRoster.cs` with a `true` default.
4. **Tool -> capability map** - an entry in `CapabilityGate.ToolCapabilities` (and `AdminElementNames`) in `Services/CapabilityGate.cs`.

Tools that work without a running Sitefinity (environment, status) and the log tools (which read the filesystem in local mode) are deliberately left out of the map - the plugin's 403 covers them in remote mode.

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

**Brute-force throttle + constant-time compare (`McpApiKeyAttribute`):**

- **Per-IP throttle.** A static bounded `ConcurrentDictionary<string, FailureEntry>` tracks failures per client IP. **10** failures inside a **5-minute** rolling window freeze that IP for **15 minutes**, answered with HTTP **429** and a `Retry-After` header. Constants only — deliberately no config knobs.
- **A correct key always wins.** The presented key is evaluated *before* the freeze is enforced: a valid key unfreezes the IP and resets its counter, then proceeds. This is what stops a lockout-DoS — someone spraying bad keys from behind the same NAT/egress address as the legitimate MCP server can never lock it out. A valid key resets the counter on every request, frozen or not.
- **Missing keys count as failures** — an unauthenticated probe is exactly what the throttle exists to slow down. Only wrong/missing *non-config* keys count; the `Enabled` and blank-config-key checks keep their existing precedence and never touch the throttle.
- **Fail open, never closed.** Every throttle call site (`SafeIsFrozen` / `SafeRecordFailure` / `SafeReset` / `Prune` / `TryAddRetryAfter`) swallows its own exceptions. A bug in the throttle degrades to "no throttling" and a plain 401 — it must never turn an auth failure into a 500.
- **Bounded memory.** Capped at 10,000 tracked IPs; on overflow `Prune` drops expired entries first, then the least recently seen, and never evicts a currently frozen entry. One thread prunes at a time (`Monitor.TryEnter`); others skip.
- **Constant-time key comparison.** `ConstantTimeEquals` walks the full length of *both* byte arrays and accumulates inequality (seeded with the length difference) rather than returning at the first mismatch, so response timing does not leak a prefix. Hand-rolled because .NET Framework 4.8 has no `CryptographicOperations.FixedTimeEquals`.
- **X-Forwarded-For is deliberately ignored.** The key is the direct connection address (`HttpContext.Current.Request.UserHostAddress`), because ServiceStack's `IRequest.RemoteIp` consults `X-Forwarded-For` — and an attacker-controlled header would let one client evade the throttle by rotating a value. Behind a reverse proxy this means all traffic shares one bucket; the valid-key-always-wins rule is what keeps that safe.
- **Off-by-one to expect when testing:** the freeze is checked *before* the failure is recorded, so attempts 1–10 return 401 (the 10th trips the freeze) and attempt **11** is the first 429.
- Not unit-testable in `tests/` — it is plugin-side .NET 4.8 code. It was verified with an offline reflection harness (threshold, freeze, reset, per-IP isolation, 32-thread concurrency, memory cap) plus a live curl test.

**Request audit log (`McpConfig.AuditRequests`, default ON):**

Every request through the `[McpApiKey]` choke point — accepted or rejected — appends one line to `App_Data\Sitefinity\Logs\McpAudit.log`:

```
{utcIso}Z | ip={directIp} | xff={X-Forwarded-For or -} | {METHOD} {path} | {redactedQuery} | auth={valid|invalid-key|missing-key|throttled|disabled}
```

- **Requests only, never results.** The audit answers "who called what from where", not "what came back".
- **Two IP fields, never conflated.** `ip=` is the direct TCP connection address — trustworthy, and what the throttle keys on. `xff=` is the `X-Forwarded-For` header verbatim (`-` when absent), truncated to 200 chars and redactor-scanned: behind a proxy/CDN it names the real client, but it is caller-supplied and forgeable, so it is informational only.
- **Query strings are redacted** with the same per-parameter deny-list plus pattern scan used for IIS queries (`McpSecretRedactor.IsDeniedKey` then `Redact`). An audit trail that leaks a secret is worse than no audit trail.
- **Rejected keys are fingerprinted, never logged.** An `invalid-key` line carries `attempted: len={n} prefix={6 chars} sha256={12 hex}`. The invalid keys that actually occur are nearly-valid — a prod key hitting dev, a stale key after rotation, a typo — so logging them raw would turn the audit file into a credential store. Hash your known keys and compare to identify which one was used. **A VALID key is never fingerprinted — no prefix, no hash, nothing.** `missing-key` and `throttled` lines carry no `attempted:` section.
- **Log-forging is blocked** — `Sanitize` flattens newlines and pipes out of every field, so a crafted URL cannot inject a fake line.
- **Rolling + fail-open.** Rolls at 10 MB to `McpAudit.1.log` … `McpAudit.3.log` (oldest deleted). Writes go through a small lock and an append-only `FileStream` with `FileShare.ReadWrite`. The entire path swallows its exceptions — a locked file, a full disk or an unmapped path degrades to no auditing and never affects the request. If `MapPath` fails or the Logs folder is missing, auditing is skipped (Sitefinity owns that folder; it is never created here).
- **The audit trail is itself MCP-inspectable** — deliberately placed in the standard Sitefinity Logs folder, so `sitefinity_read_log_file("McpAudit.log")` and `sitefinity_search_logs` read it like any other log.

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

### Incident Correlation (`sitefinity_investigate_incident`)

One tool, one plugin endpoint (`/mcp/incident-window`), three modes — deliberately not four tools.

**Clock discipline is the whole point.** The four sources disagree: Sitefinity writes **server-local** time, W3C access logs and HTTPERR are **always UTC**, Event Log records are stored in UTC. So every entry is emitted with both `TimestampUtc` and `TimestampLocal`, and the response carries `ServerTimeZoneId` + `ServerUtcOffsetMinutes` computed via `TimeZoneInfo.Local.GetUtcOffset(<the queried instant>)` — **not** `DateTime.Now`, which would report the wrong offset for a window sitting in a different DST period.

**Modes:** `Center` → window; `Query` alone → search over `LookbackHours`; neither → candidate discovery. Discovery scans only the cheap high-signal sources (event-log crash records, HTTPERR per-minute bursts, Sitefinity error-density buckets) and clusters signals within 10 minutes into one candidate, ranked WAS crash > Application Error 1000 > .NET Runtime 1026 > HTTPERR burst > Sitefinity burst. It never touches the IIS access log — search mode does, because a single line ceiling plus the time budget bound it.

**⚠️ Timestamps on the wire are pre-formatted STRINGS, by design — never `DateTime` DTO properties.** ServiceStack serializes a `DateTime` as `/Date(ms)/`, an *instant*, which destroys a server-local wall time: the MCP server's `SitefinityDateTimeConverter` then re-renders 11:00 local as 15:00 UTC. Every incident timestamp is formatted at assignment via `FormatUtc` (`yyyy-MM-ddTHH:mm:ssZ`) / `FormatLocal` (`yyyy-MM-ddTHH:mm:ss`, no suffix — the header's `ServerTimeZoneId` / `ServerUtcOffsetMinutes` state the zone), and the matching server models are `string` too. Internal window math stays `DateTime`; only the DTO surface is string. **Never "fix" this with `JsConfig.DateHandler = ISO8601`** or any other `JsConfig` global/scoped tweak: Sitefinity's own backend runs on the same ServiceStack instance, and a global date-format change breaks the admin site-wide. `MinuteUtc`/`MinuteLocal` were always strings and were the only fields immune to the original bug — that is the pattern.

**Bounding.** Fixed caps (20 SF / 25 per event channel / 25 IIS 5xx / 10 slowest / 50 query-matched / 25 HTTPERR / 20 candidates), a 2,000,000-line scan ceiling, and a 30-second wall-clock `ScanBudget` checked between sources, between files, and periodically inside the line and event loops. Exceeding any of them returns partial results plus a `Warnings` entry, never a hang or a failure. The endpoint stays plain synchronous request/response — **no** Sitefinity background-task service, no async job pattern.

The IIS request-rate series is split by mode: `RequestsPerMinute` in window mode (≤240 rows), `RequestsPerHour` in search mode (a 14-day lookback would otherwise emit ~20,000 minute rows). Exactly one is ever populated.

**Redaction + matching order.** Everything outbound goes through `McpSecretRedactor`. Query strings are additionally split into `name=value` pairs and deny-list-checked per parameter name (`McpSecretRedactor.IsDeniedKey`) before the whole string is pattern-scanned. `cs(Cookie)` and `cs(Authorization)` are **never read at all** — a redacted credential is still credential-shaped. `cs-username` and `c-ip` are retained deliberately (documented in both READMEs): correlating an outage to who was hitting what is the point. The `Query` filter matches **after** redaction, mirroring `McpFormsService.SearchTerm`, so it cannot be used as an oracle for redacted values.

**OS permissions.** Reading the event logs needs the app pool identity in the local **Event Log Readers** group; the IIS and HTTPERR folders need read ACLs. Every failure path produces a warning carrying the exact `icacls` / `net localgroup` command rather than an error. The IIS folder is derived from `HostingEnvironment.ApplicationID` (`/LM/W3SVC/{id}/ROOT`, parsed defensively) and overridable via `McpConfig.Incident.IisLogPath` (Admin > Advanced > McpSettings > Incident > IIS Log Path).

### Per-Capability Toggles (Admin > Advanced > McpSettings)

Every capability area can be switched off independently by a Sitefinity administrator. **All of them default to enabled**, so upgrading an existing install changes nothing until someone unchecks something.

**The plugin is the security boundary; the ping roster is a courtesy.**

1. **Plugin (authoritative)** - each capability is a nested `ConfigElement` on `McpConfig`, and every service handler calls `McpCapabilities.EnsureEnabled(McpCapabilities.<Name>)` as its **first line**. When off it throws an HTTP 403 carrying a structured body `{ Disabled: "<capability>", Reason: "Disabled by the administrator in Sitefinity Admin > Advanced > McpSettings." }`.
2. **MCP server (advisory)** - `GET /mcp/ping` returns a `Features` roster; `ApiKeyValidationService` caches it alongside the existing key-validation result (same TTLs), and the `CallToolFilter` refuses a disabled tool up front via `CapabilityGate.CheckTool` with "This tool is disabled by the Sitefinity administrator (Admin > Advanced > McpSettings > <element>)" - no network call. A stale roster can never grant access, because the plugin re-checks on every request.

**Fail open, never closed.** A missing or unreadable config section is treated as "everything enabled" (`McpCapabilities.TryGetConfig` swallows and returns null; `IsEnabled` and `BuildRoster` default to true). A configuration read error must not silently disable a working install. Likewise, a ping response with **no** `Features` object (any plugin build before 3.5.0) means "roster unknown" -> every tool proceeds, so the server stays fully backwards compatible.

**`/mcp/ping` is deliberately NOT capability-gated** - it is how the server learns what is off, so it must answer even when everything else is disabled.

**Config elements** (all in `McpConfig.cs` - this feature added **no** new plugin source files):

| Element | Class | Gates |
|---------|-------|-------|
| Logs | `McpLogsToolElement` | `McpLogService` (all log endpoints) |
| Metadata | `McpMetadataToolElement` | `McpMetadataService` - site info, modules, dynamic types, routes, pages, widgets, templates, taxonomies |
| Content | `McpContentToolElement` | `McpContentService` |
| Forms | `McpFormsToolElement` | `McpFormsService` — plus `AllowResponses` (definitions stay readable, submissions 403) and `ExcludedFields` |
| Config Reader | `McpConfigReaderToolElement` | `McpConfigService` + `McpSettingsSearchService` — plus `ExcludedSections` (wildcard-capable) |
| Where Used | `McpWhereUsedToolElement` | `McpWhereUsedService` |
| Permissions | `McpPermissionsToolElement` | `McpPermissionsService` |
| Incident | `McpIncidentToolElement` | `McpSystemLogService` - plus `AllowIisLogs` / `AllowEventLogs` / `AllowHttpErr` and the `IisLogPath` override |

Maintenance needs no element - `AllowWriteOperations` already gates it, and the roster reports it as `Maintenance` so the same pre-block message applies.

**Sub-capabilities.** Two settings gate something narrower than a tool group, so they are deliberately **not** roster entries and are never pre-blocked — the tool runs and gets the plugin's 403, which `CapabilityGate.AdminElementNames` maps to the right admin path:

- **`Forms > Allow Responses`** (`McpCapabilities.EnsureFormResponsesAllowed`, capability name `FormsResponses`) — form *definitions* stay readable; only the submissions endpoint 403s. An assistant can still reason about a form's shape without ever seeing what people submitted.
- **`Config Reader > Excluded Sections`** (`McpCapabilities.EnsureSectionNotExcluded`, capability name `ConfigSection`) — gates individual sections by name, not the capability.

**Hiding data rather than blocking endpoints.** Two settings strip data server-side instead of refusing the call:

- **`Forms > ExcludedFields`** — comma-separated, exact field names, case-insensitive. Enforced in `McpFormsService` by removing the names from `fieldNames` *before* `BuildResponseInfo` runs, so an excluded field is never read, never redacted, never in `Values`, and therefore **cannot be matched by `SearchTerm`**. Same oracle rule as redaction-before-match. No placeholder is emitted — the field simply isn't there.
- **`Config Reader > ExcludedSections`** — comma-separated section names, matched by `McpCapabilities.IsSectionExcluded`. Normalization trims, lower-cases, then strips a trailing `.config` and a trailing `config`, so `Authentication`, `authenticationconfig` and `Authentication.config` all match section `AuthenticationConfig`. Tokens containing `*` are wildcards (`Auth*`, `*Security*`), translated via `Regex.Escape` + `\*` → `.*`, anchored, `IgnoreCase`, 1s timeout, and matched against **both** the raw and suffix-stripped name; a malformed or timing-out pattern is ignored rather than throwing. Suffix-stripping is applied to plain tokens only — stripping `config` off `*config*` would change what the pattern means. Enforced at three points: omitted from `/mcp/config`, 403 from `/mcp/config/{SectionName}` (re-checked against the **resolved** type name so an alias can't slip past), and filtered out of `/mcp/settings/search` **before** `Take` and the count, so a hidden section can't be inferred from a short page.


**Element design rules.** Every capability gets its **own named class** deriving from the abstract `McpToolElement` base (which carries only `Enabled`), even when it adds nothing today - that gives each tool a natural home for a future setting without a config migration. Granularity follows the **plugin service boundary**: tools backed by one service (the log trio, the metadata family) share one element; don't split finer. Keep the number of settings small - `Enabled` only, unless a tool has a genuine knob (`Incident`'s three source flags and `IisLogPath` are the proof case).

**Incident source flags degrade, they don't fail.** When `Incident.Enabled` is false the endpoint 403s like any other capability. When it's on but a source flag is off, that source is skipped and a `Warnings` entry is added ("IIS source disabled by administrator (McpSettings > Incident > Allow IIS Logs).") - the same shape as the existing ACL-denied warnings. `ParseSources` applies the flags for window and search modes; `RunDiscovery` applies them directly because it bypasses `ParseSources`.

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
| `IApiKeyValidationService` | `ApiKeyValidationService` | Validate API keys via ping endpoint; caches the per-capability feature roster (`GetFeaturesAsync`) |
| `ISitefinityMetadataService` | `SitefinityMetadataService` | Fetch site info, modules, dynamic types, fields, config sections, where-used, permissions, incident windows; clear cache / recycle |
| `IHttpClientFactory` | (framework) | Create HTTP clients for remote calls |
| `SitefinityMcpConfig` | (concrete) | Loaded config singleton |

## Plugin Endpoint Reference

All endpoints require `X-MCP-API-Key` header. Protected by `[McpApiKey]` attribute. Every endpoint except `/mcp/ping` is additionally gated by its capability toggle (see **Per-Capability Toggles**) and returns HTTP 403 with a `{ Disabled, Reason }` body when switched off.

| Route | Method | Description |
|-------|--------|-------------|
| `/mcp/ping` | GET | Lightweight key validation — returns `{ status: "ok", features: { … } }`. The `features` roster reports each capability's Enabled state (plus `Maintenance` = Allow Write Operations, and Incident's three source flags). **Never capability-gated** — it is how the MCP server learns what is off |
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
| `/mcp/config/{SectionName}` | GET | Flattened dump of a config section. Returns **overrides only** by default — Sitefinity materializes a fully defaults-merged graph, so an unbounded `ContentViewConfig` is ~79 MB / 375k entries. Defaults are pruned via `ConfigElement.Source` and `ConfigProperty.DefaultValue`/`SkipOnExport`. Optional `IncludeDefaults`, `PathFilter` (case-insensitive substring), `MaxEntries` (default 500, max 5000). Response carries `TotalCount`, `ReturnedCount`, `Truncated`, `DefaultsSkipped`. Credential-like values (keys, passwords, connection strings, tokens, `ConfigProperty.IsSecret`, `[SecretData]`/encrypted properties) are **ALWAYS redacted** and never returned — in every environment, with no flag to reveal them |
| `/mcp/settings/search` | GET | Full-text search over the backend `advanced-settings-search` Lucene index (requires `Query`; optional `Take`, default 25, max 100). Returns caption/path/section per hit, secret-redacted; reports `IndexAvailable: false` with enablement guidance when the index is disabled or missing |
| `/mcp/where-used` | GET | Reverse lookup: every page/template referencing a widget type, content item, or template (requires `Query`; optional `Kind`=widget\|content\|template) |
| `/mcp/permissions` | GET | Effective per-role granted/denied actions on a page or content item, and whether it inherits (requires `Identifier`; optional `TypeFullName` for a content item) |
| `/mcp/incident-window` | GET | Incident forensics across four sources — Sitefinity logs (server-local timestamps), the IIS W3C access log (always UTC), the Windows Application + System event logs (UTC; Security never read), and HTTPERR (always UTC). **Three modes**: `Center` set → correlated window (`WindowMinutes`, default 15, clamp 1–120); `Query` set with no `Center` → search across `LookbackHours` (default 72, clamp 1–336); neither → clustered candidate crash moments over `LookbackHours` (IIS deliberately NOT scanned — too large over multi-day ranges). Optional `Sources` (`sitefinity,iis,eventlog,httperr`). Every entry carries `TimestampUtc` + `TimestampLocal`; the response reports `ServerTimeZoneId` and the offset **at the queried instant** (DST-correct). Raw IIS lines are never returned — aggregates plus capped lists only. Bounded by fixed caps, a 2M-line ceiling, and a 30s wall-clock budget (synchronous; no background jobs). `cs(Cookie)` / `cs(Authorization)` are never read; `cs-username` and `c-ip` are deliberately retained |
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
- **MCP SDK** — `ModelContextProtocol` v2.2.0 (stable)
- **XML doc comments must be warning-free** — Plugin `.cs` files are compiled *inside* customer Sitefinity solutions, which usually have `GenerateDocumentationFile` on. Anything sloppy in a `///` comment becomes a CS1570/CS1574 warning in **their** build, not ours (this repo's projects don't emit doc XML, so the warnings are invisible here). Rules:
  - **Escape `&` as `&amp;`** in doc text — query-string examples like `?A=1&B=2` are the usual culprit (CS1570 "reference to undefined entity"). Same for literal `<` / `>` (`&lt;` / `&gt;`).
  - **Only `cref` things the file can actually resolve** — a member on another type needs the qualifier (`<see cref="McpConfigSectionResponse.TotalCount"/>`, not `<see cref="TotalCount"/>`), and **extension methods never resolve through the extended type** — write `<c>PageNode.GetFullUrl()</c>`, not `<see cref="PageNode.GetFullUrl()"/>` (CS1574).
  - When in doubt, use `<c>…</c>` — it's plain text and can't break a consumer's build.

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

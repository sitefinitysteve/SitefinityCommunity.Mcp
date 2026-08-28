# SitefinityCommunity.Mcp — Sitefinity Plugin

Source files that add MCP REST endpoints to any Sitefinity CMS installation. These compile directly into your Sitefinity web app — no separate DLL needed.

## Installation

### 1. Install plugin files

**Option A — Automated** (recommended):

From the repo root, run the install script pointing at your Sitefinity web app:

```powershell
.\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp"
```

This will:
- Copy all `.cs` files into `Code\Mcp\SitefinityCommunity\`
- Add `<Compile Include="...">` entries to your `.csproj` (legacy format only — SDK-style projects auto-include)
- On update (`-Force`), clean out old files and refresh `.csproj` entries so renames/removals are handled

```powershell
# Update to latest version (removes old files, refreshes csproj)
.\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp" -Force
```

**Option B — Manual**:

Copy all `.cs` files from this folder into `Code\Mcp\SitefinityCommunity\` in your Sitefinity web app, then add each file to your `.csproj` under an `<ItemGroup>` with `<Compile Include="Code\Mcp\SitefinityCommunity\FileName.cs" />`:

```
SitefinityWebApp/
├── Code/
│   └── Mcp/
│       └── SitefinityCommunity/
│           ├── McpInit.cs
│           ├── McpConfig.cs
│           ├── McpApiKeyAttribute.cs
│           ├── McpSecretRedactor.cs
│           ├── McpServicePlugin.cs
│           ├── McpLogService.cs
│           ├── McpLogRequest.cs
│           ├── McpMetadataService.cs
│           ├── McpContentService.cs
│           ├── McpFormsService.cs
│           ├── McpConfigService.cs
│           ├── McpWhereUsedService.cs
│           ├── McpPermissionsService.cs
│           ├── McpSystemLogService.cs
│           └── McpMaintenanceService.cs
├── Global.asax.cs
└── ...
```

The `Code/Mcp/SitefinityCommunity/` path keeps MCP files isolated from your application code. The `SitefinityCommunity` subfolder makes it clear these are community files that can be updated by re-running the install script.

### 2. Register in Global.asax

In your `Global.asax.cs`, add a single line inside the `Bootstrapper_Initialized` handler:

```csharp
SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();
```

That's it — this registers the config section and all ServiceStack endpoints.

### 3. Configure API key in Sitefinity admin

1. Go to **Administration → Settings → Advanced → McpSettings**
2. Set the **API Key** (must match `sitefinityApiKey` in your `sitefinity-mcp.json`)
3. **Enabled** is `true` by default — set to `false` to disable endpoints
4. **IIS Log Path** is optional — leave blank to auto-detect this site's W3SVC log folder (used by `/mcp/incident-window`)
5. **Allow Write Operations** is `false` by default. Enable it only if you want the MCP server to be able to clear caches or recycle this instance (`/mcp/cache/clear`, `/mcp/app/recycle`). Leave OFF on production.

### 4. Verify

```
GET https://your-site.com/RestApi/mcp/logs
Header: X-MCP-API-Key: your-api-key
```

Should return a JSON array of log files.

## Endpoints

| Route | Method | Description |
|-------|--------|-------------|
| `/mcp/ping` | GET | Lightweight key validation (returns `{ status: "ok" }`) |
| `/mcp/logs` | GET | List all log files |
| `/mcp/logs/{FileName}` | GET | Read a log file |
| `/mcp/logs/search` | POST | Search logs with regex |
| `/mcp/logs/last-error` | GET | Most recent error entry |
| `/mcp/site-info` | GET | Sitefinity version, .NET version, project name, languages, multisite |
| `/mcp/modules` | GET | All installed modules with type, status, startup type |
| `/mcp/dynamic-types` | GET | Module Builder types grouped by module |
| `/mcp/dynamic-types/{TypeFullName}/fields` | GET | Field definitions for a specific dynamic type |
| `/mcp/routes` | GET | Combined page + API routes (backward compat) |
| `/mcp/page-routes` | GET | CMS page routes via Sitemap API (cached, fast). Includes URL evaluation warnings |
| `/mcp/api-routes` | GET | ServiceStack API routes and OData entity sets |
| `/mcp/page-details?PageIdentifier={id}` | GET | Full page detail: metadata, template, all widgets with configured properties. Accepts Guid, URL path, slug, or title |
| `/mcp/config` | GET | List registered configuration section names |
| `/mcp/config/{SectionName}` | GET | Flattened, **redacted** dump of a config section. Returns **overrides only** by default (defaults pruned); optional `IncludeDefaults`, `PathFilter`, `MaxEntries` (default 500, max 5000). Keys / passwords / connection strings / `[SecretData]` values are **always** withheld (no flag reveals them, in any environment) |
| `/mcp/settings/search` | GET | Full-text search over the backend `advanced-settings-search` Lucene index (requires `Query`; optional `Take`, default 25, max 100). Returns caption/path/section per hit, secret-redacted; reports `IndexAvailable: false` with enablement guidance when the index is disabled or missing |
| `/mcp/where-used?Query={x}` | GET | Reverse lookup of a widget type / content item / template across pages and templates |
| `/mcp/permissions?Identifier={x}` | GET | Effective per-role permissions on a page (or content item via `TypeFullName`) |
| `/mcp/incident-window` | GET | Incident forensics across Sitefinity logs, the IIS W3C access log, the Windows Application + System event logs, and HTTPERR. Three modes: `Center` → correlated window (`WindowMinutes`, default 15, max 120); `Query` only → search over `LookbackHours` (default 72, max 336); neither → candidate crash moments over `LookbackHours`. Optional `Sources` (`sitefinity,iis,eventlog,httperr`). See the prerequisites below |
| `/mcp/cache/clear` | POST | **Write** — clear cache (`Scope`=output\|whole\|page). Requires "Allow Write Operations" |
| `/mcp/app/recycle` | POST | **Write** — restart the application. Requires "Allow Write Operations" |

All endpoints require `X-MCP-API-Key` header. The two **write** endpoints additionally require **Allow Write Operations** to be enabled (see below) and return HTTP 403 otherwise.

## Incident endpoint prerequisites (`/mcp/incident-window`)

This is the only endpoint that reads things *outside* the Sitefinity web app, so it depends on OS-level permissions the app pool identity does not have by default. Nothing here is required for the endpoint to respond — every source is wrapped, and a missing permission comes back as a warning carrying the exact fix rather than an error. But without them the corresponding section is simply empty.

Substitute your app pool name for `{AppPool}` below (the plugin reports the live one, from `APP_POOL_ID`, in its warnings).

**1. Windows Event Log (Application + System).** Add the app pool identity to the local **Event Log Readers** group, then recycle the pool:

```powershell
net localgroup "Event Log Readers" "IIS APPPOOL\{AppPool}" /add
```

The **Security** log is deliberately never read — an app pool identity cannot read it, and nothing an outage investigation needs lives there.

**2. IIS W3C access log folder.** Auto-detected as `%SystemDrive%\inetpub\logs\LogFiles\W3SVC{siteId}`, with the site id parsed from `HostingEnvironment.ApplicationID`. Override it with **IIS Log Path** in Sitefinity Admin > Advanced > McpSettings when the site logs elsewhere (or when the site id cannot be resolved — virtual applications). Grant read access:

```powershell
icacls "C:\inetpub\logs\LogFiles\W3SVC1" /grant "IIS APPPOOL\{AppPool}:(OI)(CI)R"
```

**3. HTTPERR.** `C:\Windows\System32\LogFiles\HTTPERR` is administrator-readable only by default. This is where http.sys records the 503s that never reached the site — `AppOffline`, `QueueFull`, `Timer_ConnectionIdle` — so it is usually the most valuable source when the app pool itself died:

```powershell
icacls "C:\Windows\System32\LogFiles\HTTPERR" /grant "IIS APPPOOL\{AppPool}:(OI)(CI)R"
```

### What this endpoint returns about people — a deliberate decision

Raw IIS log lines are **never** returned; the response carries aggregates (per-minute counts, a status histogram including sub-status) plus capped entry lists. Within those entries:

- **`cs-username` and `c-ip` ARE returned.** Correlating an outage to who was hitting what is the entire point of the endpoint, and stripping them would make it useless. If your threat model does not allow user identifiers in an LLM context, do not enable this plugin.
- **`cs(Cookie)` and `cs(Authorization)` are never read at all** — not redacted, not fetched. A redacted credential is still a credential-shaped string.
- **Query strings are redacted per parameter**: any `name=value` whose name hits the redactor's deny-list loses its value outright, then the whole string is pattern-scanned for tokens sitting under innocuous names.
- **`Query` matching runs *after* redaction**, following the same rule as `/mcp/forms/{id}/responses` — so the filter can never be used as an oracle to confirm a value the redactor removed.

### A note for anyone editing this endpoint

Timestamps in the incident DTOs are **pre-formatted strings, never `DateTime` properties** — ServiceStack serializes a `DateTime` as `/Date(ms)/` (an instant), which turns a server-local wall time into a point on the timeline that the client then re-renders as UTC. Do **not** "fix" that with `JsConfig.DateHandler = ISO8601` or any other `JsConfig` change: Sitefinity's own backend runs on the same ServiceStack instance, and a global date-format change breaks the admin site-wide. Format at assignment instead (`FormatUtc` / `FormatLocal`).

### Bounding the work

Fixed caps everywhere (20 Sitefinity entries, 25 per event-log channel, 25 IIS 5xx, 10 slowest, 50 query-matched requests, 25 HTTPERR, 20 candidates), each reporting its true total and a `Truncated` flag. Scanning also stops at 2,000,000 lines or a **30-second wall-clock budget**, whichever comes first, returning whatever was gathered plus a warning. The endpoint is a plain synchronous request/response — it never queues background work.

## Why source files instead of a NuGet DLL?

Sitefinity is a closed-source CMS that **bundles its own pinned versions** of ServiceStack, Newtonsoft.Json, Telerik.Sitefinity.* assemblies, and dozens of other dependencies. These versions change across Sitefinity releases — ServiceStack 5.x in Sitefinity 13, ServiceStack 6.x in Sitefinity 14, etc.

A precompiled NuGet DLL would need to reference **the exact same assembly versions** as the consumer's Sitefinity installation. This creates a nightmare:

- **Binding redirect hell** — "Could not load file or assembly 'ServiceStack, Version=X.X.X'" at runtime
- **Multi-targeting burden** — we'd need separate NuGet packages per Sitefinity major version
- **Transitive dependency conflicts** — our DLL's dependency tree would fight with Sitefinity's

**Source files sidestep all of this.** When you copy these `.cs` files into your Sitefinity web app, they compile against *whatever assemblies your project already references*. Whether you're on Sitefinity 12.x or 15.x, the code compiles against your exact ServiceStack version, your exact Telerik assemblies, your exact .NET Framework target.

This is the same approach used by several Sitefinity community packages and is common in ecosystems where the host application tightly controls its dependency versions.

**Bonus benefits:**
- **Debuggable** — set breakpoints, step through the code
- **Customizable** — tweak the source to fit your needs
- **No assembly loading issues** — nothing to go wrong at deploy time

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
│           ├── McpTasksService.cs
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

> **Keep the plugin in step with the MCP server.** `/mcp/ping` reports this plugin drop's version
> (`McpPluginInfo.Version` in `McpServicePlugin.cs`), and `sitefinity_check_status` compares it with the
> MCP server's own version and prints the fix when they differ. After upgrading the MCP server, re-run
> `install-plugin.ps1 -Target "<your Sitefinity web project>"` from the matching repo tag — it copies
> the `.cs` files **and registers any new ones in the `.csproj`** — then rebuild the solution and
> recycle the app pool. New endpoints answer 404 until you do.

### 3. Configure API key in Sitefinity admin

1. Go to **Administration → Settings → Advanced → McpSettings**
2. Set the **API Key** (must match `sitefinityApiKey` in your `sitefinity-mcp.json`)
3. **Enabled** is `true` by default — set to `false` to disable endpoints
4. **Allow Write Operations** is `false` by default. Enable it only if you want the MCP server to be able to clear caches or recycle this instance (`/mcp/cache/clear`, `/mcp/app/recycle`). Leave OFF on production.

### 3b. Turn individual capabilities off (optional)

Under **McpSettings** each capability is its own expandable node with an **Enabled** checkbox. **They all start enabled** — you only need to visit this if you want to narrow what the MCP server can reach.

| Node | Turning it off blocks |
|------|-----------------------|
| **Logs** | `/mcp/logs`, `/mcp/logs/{FileName}`, `/mcp/logs/search`, `/mcp/logs/last-error` |
| **Metadata** | Site info, modules, dynamic types, routes, page details, widget properties, widget tree, templates, taxonomies |
| **Content** | `/mcp/content` (live content item queries) |
| **Forms** | Form definitions and form submissions |
| **Config Reader** | `/mcp/config`, `/mcp/config/{SectionName}`, `/mcp/settings/search` |
| **Where Used** | `/mcp/where-used` |
| **Permissions** | `/mcp/permissions` |
| **Incident** | `/mcp/incident-window` |
| **Scheduled Tasks** | `/mcp/scheduled-tasks`, `/mcp/search-indexes` |

A disabled capability returns **HTTP 403** with `{ "Disabled": "<name>", "Reason": "…" }`. The change takes effect on the next request — **no app pool recycle needed**. `/mcp/ping` is never blocked: it reports the current on/off state so the MCP server can tell the user which tools are unavailable and why.

**Incident** has three extra checkboxes for the OS-level log sources it reads, plus the IIS folder override:

- **Allow IIS Logs** — read this site's IIS W3C access log. Cookie and Authorization columns are never read; client IP and IIS username are returned.
- **Allow Event Logs** — read the Windows Application and System event logs. The Security log is never read.
- **Allow HTTPERR** — read the http.sys HTTPERR logs.
- **IIS Log Path** — optional; leave blank to auto-detect this site's `W3SVC{siteId}` log folder.

Unchecking a source does **not** fail the incident call — that source is skipped and the response carries a warning saying an administrator disabled it.

**Forms** has two extra settings for submission privacy:

- **Allow Responses** (default on) — uncheck to keep form *definitions* readable while refusing `/mcp/forms/{id}/responses` with 403. Useful when an assistant should understand a form's shape but never see what people submitted.
- **Excluded Fields** (default blank) — comma-separated field names stripped from every submission, e.g. `SSN, HealthCard`. Case-insensitive, matched on the exact field name. Excluded fields are removed **before** redaction and **before** any `SearchTerm` is matched, so they can't be found by searching for their values, and they simply don't appear in the output (no placeholder). The response lists which fields were excluded.

**Config Reader** has one extra setting:

- **Excluded Sections** (default blank) — comma-separated configuration section names hidden from the MCP entirely: omitted from `/mcp/config`, refused with 403 from `/mcp/config/{SectionName}`, and stripped from `/mcp/settings/search` results before the result count. A `Config` / `.config` suffix is optional — `Authentication` also matches `AuthenticationConfig` and `Authentication.config`. `*` wildcards work too: `Auth*` hides everything starting with Auth, `*Security*` everything containing Security.

### 3c. Brute-force protection (automatic, nothing to configure)

The `[McpApiKey]` filter throttles failed authentication per client IP: **10 failures in 5 minutes freezes that IP for 15 minutes**, returning HTTP **429** with a `Retry-After` header. Requests with no key at all count as failures too.

A **correct key always wins** — it is evaluated before the freeze is applied, so a valid key immediately unfreezes that IP and resets its counter. Nobody can lock your MCP server out by spraying bad keys from the same address.

Notes:

- The bucket is the **direct connection IP**. `X-Forwarded-For` is ignored on purpose (it is attacker-controlled). Behind a reverse proxy every caller shares one bucket — safe because a valid key always resets it.
- Thresholds are fixed constants; there is nothing to configure and nothing to turn off.
- Memory is capped at 10,000 tracked IPs and the whole path fails open — a throttle problem degrades to a normal 401, never a 500.
- The API key comparison is constant-time, so response timing cannot leak the key.

When testing with curl, expect attempts 1–10 to return 401 and attempt 11 to be the first 429 — the freeze is checked before the current failure is recorded.

### 3d. Request auditing (on by default)

**Audit Requests** under **McpSettings** (default **on**) appends one line per request — accepted or rejected — to `App_Data\Sitefinity\Logs\McpAudit.log`:

```
{utcIso}Z | ip={directIp} | xff={X-Forwarded-For or -} | {METHOD} {path} | {redactedQuery} | auth={valid|invalid-key|missing-key|throttled|disabled}
```

- **Requests only, never results.**
- `ip=` is the direct connection address (what the throttle uses). `xff=` is the `X-Forwarded-For` header verbatim, capped at 200 chars — useful behind a proxy, but forgeable, so treat it as a lead rather than proof.
- Query strings are secret-redacted; newlines and pipes are stripped so a crafted URL cannot forge lines.
- An `invalid-key` line adds `attempted: len={n} prefix={6 chars} sha256={12 hex}` so you can identify a stale or cross-environment key by hashing your known keys and comparing. The raw key is never written — and a **valid** key is never fingerprinted in any form.
- Rolls at 10 MB (`McpAudit.1.log` … `McpAudit.3.log`). The whole path fails open: if the file is locked, the disk is full, or the Logs folder is missing, auditing is skipped and the request is unaffected.
- The file sits in the normal Sitefinity Logs folder on purpose, so the MCP's own log tools (`sitefinity_read_log_file("McpAudit.log")`, `sitefinity_search_logs`) can read it.

### 4. Verify

```
GET https://your-site.com/RestApi/mcp/logs
Header: X-MCP-API-Key: your-api-key
```

Should return a JSON array of log files.

## Endpoints

| Route | Method | Description |
|-------|--------|-------------|
| `/mcp/ping` | GET | Lightweight key validation (returns `{ status: "ok", features: { ... } }`). Never blocked by a capability toggle |
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
| `/mcp/scheduled-tasks` | GET | Scheduler snapshot: **RunningNow** (genuinely running tasks — `Name`, item name, `StartedUtc`/`StartedLocal`, `RunningForSeconds`, `IsSearchIndexRebuild` + `IndexName`, progress; cap 25; a row must have `IsRunning` **and** scheduler status `Started`, since `IsRunning` alone stays set on failed and pending rows) and **Failed** (rows whose status is `Failed`, newest first — `ScheduledForUtc`/`Local`, `ExecutedOnUtc`/`Local`, status message; cap 10). Both are bounded status-filtered queries — the scheduled-task store is never enumerated, and successfully completed rows are not returned (the scheduler deletes them) |
| `/mcp/search-indexes` | GET | Every configured search index (a search-index pipe on a publishing point), uncapped. Per index: catalog name (`docs-index`) **and display name** (`Docs Index`), owning publishing point **and provider**, backend, whether it exists, document count where the backend exposes one, `LastUpdatedUtc`/`Local`, rebuild state, and `LastReindexStatus` (`running`/`failed`/`completed`/`unknown`) cross-referenced from the scheduler's own rows. Anything unobtainable is reported per index in `Warnings`; a `Title` is never a pipe-type label (it falls back to the publishing point's name, then a title derived from the catalog name, with `TitleSource` saying which), and a backend hidden behind Sitefinity's search-service decorators yields `Backend`/`DocumentCount` of **null** plus one response-level warning rather than the wrapper's type name. Indexes live under the `SearchPublishingProvider` publishing provider, not the default one — every configured provider is queried and `ProvidersScanned` names them |
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

**2. IIS W3C access log folder.** Auto-detected as `%SystemDrive%\inetpub\logs\LogFiles\W3SVC{siteId}`, with the site id parsed from `HostingEnvironment.ApplicationID`. Override it with **IIS Log Path** in Sitefinity Admin > Advanced > McpSettings > Incident when the site logs elsewhere (or when the site id cannot be resolved — virtual applications). Grant read access:

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

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
4. **Allow Write Operations** is `false` by default. Enable it only if you want the MCP server to be able to clear caches or recycle this instance (`/mcp/cache/clear`, `/mcp/app/recycle`). Leave OFF on production.

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
| `/mcp/where-used?Query={x}` | GET | Reverse lookup of a widget type / content item / template across pages and templates |
| `/mcp/permissions?Identifier={x}` | GET | Effective per-role permissions on a page (or content item via `TypeFullName`) |
| `/mcp/cache/clear` | POST | **Write** — clear cache (`Scope`=output\|whole\|page). Requires "Allow Write Operations" |
| `/mcp/app/recycle` | POST | **Write** — restart the application. Requires "Allow Write Operations" |

All endpoints require `X-MCP-API-Key` header. The two **write** endpoints additionally require **Allow Write Operations** to be enabled (see below) and return HTTP 403 otherwise.

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

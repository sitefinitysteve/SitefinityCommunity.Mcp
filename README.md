# SitefinityCommunity.Mcp

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server for **Sitefinity CMS**. Gives Claude Code (and any MCP client) direct access to Sitefinity logs, diagnostics, and CMS status.

[Buy Me A Coffee](https://buymeacoffee.com/stevewgw)

Inspired by [Laravel Boost](https://github.com/nicepkg/laravel-boost) — designed as an extensible framework where adding new tools requires only creating a new class file.

## Why this exists

MCP servers are popping up across the web development ecosystem — Laravel has Boost, Rails and Next.js have their own — and Sitefinity deserves one too.

I built this for myself. I work in Sitefinity every day and wanted the same AI-assisted workflow that other frameworks already have. Sitefinity's official repos exist on GitHub, but community PRs tend to sit — and when you need something for your day-to-day, you can't wait indefinitely. This scratches my own itch first: if a feature would save me time on a real project, it gets built. That means it stays practical and actively maintained, not theoretical.

It's open source and community-driven — contributions, ideas, and feedback are welcome. No guarantees on timelines (this is a side project), but because I use it daily, useful improvements tend to land quickly.

## Features

- **Log Tools** — Read error/trace logs, search across all log files with regex, get the last error
- **Site Info** — Sitefinity version, .NET version, project name, configured languages, multisite info
- **Module Inspector** — List all installed modules with type, status, and startup type
- **Content Model** — Browse Module Builder dynamic types and their field definitions
- **Page Inspector** — List all CMS page routes (via Sitemap API for performance), get full page details including template name, all widgets, and their configured properties
- **Route Discovery** — Browse CMS page routes with URL evaluation warnings, ServiceStack API routes, and OData entity sets
- **Config Reader** — List and dump Sitefinity configuration sections (credential-like values always redacted)
- **Where-Used** — Reverse lookup of every page/template that references a widget type, content item, or template — the safety check before a refactor
- **Permissions Inspector** — Effective per-role permissions on a page or content item, including parent inheritance
- **Maintenance** — Clear caches and recycle the app from your editor (opt-in write operations, never on prod)
- **Status Check** — Verify if Sitefinity is bootstrapped and ready
- **Multi-Environment** — Switch between dev/staging/prod environments on the fly
- **Dual-Mode Logs** — Local filesystem access for dev, HTTP via companion plugin for remote servers
- **Auto-Discovery** — New tools are picked up automatically via `[McpServerToolType]` attribute
- **API Key Validation** — Proactive key matching between MCP server and Sitefinity plugin

## Installation

There are two components to set up: the **MCP server** (runs on your dev machine) and the **Sitefinity plugin** (drops into your Sitefinity web app). Follow these steps in order.

### Step 1 — Get the MCP server

Clone this repo to your local machine:

```bash
git clone https://github.com/SitefinityCommunity/SitefinityCommunity.Mcp.git
cd SitefinityCommunity.Mcp
dotnet build
```

### Step 2 — Install the plugin into your Sitefinity project

The companion plugin exposes REST endpoints at `/RestApi/mcp/*` that the MCP server calls. It's distributed as source files (not a NuGet package) so it compiles against your existing Sitefinity assemblies — no DLL binding conflicts across versions.

Run the install script, pointing it at your Sitefinity web app root:

```powershell
.\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp"
```

This copies the plugin source files into `Code\Mcp\SitefinityCommunity\` in your project.

Then wire it up in `Global.asax.cs` inside your `Bootstrapper_Initialized` handler:

```csharp
protected void Bootstrapper_Initialized(object sender, ExecutedEventArgs e)
{
    if (e.CommandName == "Bootstrapped")
    {
        SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();
    }
}
```

Build your Sitefinity project and recycle the app pool.

### Step 3 — Generate an API key

The MCP server and Sitefinity plugin authenticate with a shared API key. Generate a cryptographically secure one:

```bash
dotnet run --project src/SitefinityCommunity.Mcp -- generate-key
```

This prints a new 256-bit Base64 key. Copy it — you'll use it in the next two steps.

> **Important:** The key flow is one direction only — generate it here, then paste it into both places. Do **not** copy the key back out of Sitefinity's Advanced config — what's stored there is the encrypted value, not the original key.

### Step 4 — Configure the Sitefinity plugin

In **Sitefinity Admin > Settings > Advanced > McpSettings**:

1. Paste the generated key into **API Key**
2. Check **Enabled** (it's `false` by default — you must opt in)
3. Save, then **recycle the app pool** — the Enabled flag and API key are read at startup

The key is stored encrypted at rest via Sitefinity's `[SecretData]` mechanism. What you see in the Advanced config after saving is the encrypted form — always use the original generated key in `sitefinity-mcp.json`.

### Step 5 — Create your config file

Create `sitefinity-mcp.json` somewhere on your machine (keep it gitignored — it contains keys):

```json
{
    "defaultEnvironment": "dev",
    "environments": {
        "dev": {
            "url": "https://dev.example.com",
            "logsPath": "C:\\Path\\To\\Sitefinity\\App_Data\\Sitefinity\\Logs",
            "sitefinityApiKey": "your-key-from-step-3"
        },
        "staging": {
            "url": "https://staging.example.com",
            "sitefinityApiKey": "your-staging-api-key"
        }
    }
}
```

- **`sitefinityApiKey`** — paste the same key you put in Sitefinity Admin
- **`logsPath`** — set this when Sitefinity runs on the same machine (local mode). Omit it for remote servers — logs are fetched via HTTP through the plugin instead
- **`url`** — required for every environment
- **`allowWriteOperations`** — optional, default `false`. Set to `true` to permit the write tools (`sitefinity_clear_cache`, `sitefinity_recycle_app`) for this environment. Ignored for prod-like names, and also requires **Allow Write Operations** enabled in the Sitefinity admin

### Step 6 — Configure your AI client

#### Claude Code

Add to your project's `.mcp.json`:

```json
{
    "mcpServers": {
        "sitefinity-mcp": {
            "type": "stdio",
            "command": "dotnet",
            "args": ["run", "--project", "C:\\GitHub\\SitefinityCommunity.Mcp\\src\\SitefinityCommunity.Mcp"],
            "env": {
                "SITEFINITY_MCP_CONFIG": "C:\\Path\\To\\sitefinity-mcp.json"
            }
        }
    }
}
```

#### VS Code

Create `.vscode/mcp.json` in your workspace (or run **MCP: Add Server** from the Command Palette):

```json
{
    "servers": {
        "sitefinity-mcp": {
            "command": "dotnet",
            "args": ["run", "--project", "C:\\GitHub\\SitefinityCommunity.Mcp\\src\\SitefinityCommunity.Mcp"],
            "env": {
                "SITEFINITY_MCP_CONFIG": "C:\\Path\\To\\sitefinity-mcp.json"
            }
        }
    }
}
```

Then open Chat in VS Code and approve the MCP trust prompt when asked.

### Step 7 — Verify the connection

Ask Claude: *"Check if Sitefinity is running"* — it will call `sitefinity_check_status`. If the API keys don't match, you'll get a clear error message rather than a cryptic 401.

**Key validation behavior:**
- **Keys match** — tools work normally
- **Keys don't match** — tools return: "API key mismatch..." error
- **Sitefinity unreachable** — tools proceed with a warning (so you can still read local logs when debugging a down server)
- **Blank keys** — rejected on both sides (MCP server won't start; Sitefinity won't register endpoints)

### Step 8 (optional) — Install the Sitefinity skills

The repo ships curated skills that teach AI agents how to think about Sitefinity widgets and page composition. Skills are installable into Claude Code, Cursor, Codex, and GitHub Copilot.

Run the installer and it walks you through two choices: scope (project or global) and which agents to install into (detected ones are pre-selected).

```powershell
# Interactive — prompts for scope and agents
.\install-skills.ps1

# Non-interactive / CI
.\install-skills.ps1 -Scope project -Target "C:\Proj" -Agents claude,cursor -Force
.\install-skills.ps1 -Scope global  -Agents claude -Force
```

**How it installs:** a canonical copy goes to `<root>/.agents/skills/<name>/`, then each selected agent's skills directory gets a symlink back to the canonical copy. Update the canonical copy once and every agent sees it.

Per-agent paths:

| Agent | Skills directory |
|---|---|
| Claude Code | `.claude/skills/` |
| Cursor | `.cursor/skills/` |
| Codex | `.codex/skills/` |
| GitHub Copilot | `.github/copilot/skills/` |

**Windows note:** creating directory symlinks needs Developer Mode (Settings → System → For developers) or an admin shell. If symlinks are unavailable the installer falls back to plain copies automatically — updates just won't auto-propagate.

Currently bundled:
- **sitefinity-widget-expert** — MVC widget development, designer attributes, view conventions, JSON persistence
- **sitefinity-page-inspector** — Walks the MCP tools needed to inspect a page's widgets and their configured properties
- **sitefinity-poco-generator** — Generates strongly-typed C# POCO classes (with a `DynamicContent` hydration constructor) from Module Builder dynamic types
- **sitefinity-debloat-repo** — Stop committing `bin/`, `AdminApp/`, `packages/`, and DB backups: make them reproducible build artifacts (restore from nuget.org + AdminApp mirror + copy-local custom DLLs) and optionally purge the bloat from git history

---

## Available Tools

<a id="available-tools"></a>

| Tool | Description |
|------|-------------|
| `sitefinity_read_error_log` | Last N entries from Error.log |
| `sitefinity_read_trace_log` | Last N entries from Trace.log |
| `sitefinity_list_log_files` | All .log files with size and modified date |
| `sitefinity_read_log_file` | Read any log file by name |
| `sitefinity_search_logs` | Regex search across all logs with context |
| `sitefinity_get_last_error` | Most recent error with full details |
| `sitefinity_check_status` | Check if Sitefinity is bootstrapped |
| `sitefinity_get_site_info` | Sitefinity version, .NET version, project name, languages, multisite info |
| `sitefinity_list_modules` | All installed modules with type, status, startup type |
| `sitefinity_list_dynamic_types` | Module Builder types grouped by module with field counts |
| `sitefinity_get_type_fields` | Field definitions for a specific dynamic type |
| `sitefinity_list_page_routes` | All CMS page routes via Sitemap API (fast, cached). Includes URL evaluation warnings for pages with dynamic routing |
| `sitefinity_list_api_routes` | ServiceStack REST API routes and OData entity sets |
| `sitefinity_get_page_details` | Full page detail by ID, URL path, slug, or title. Returns page metadata, template name, and every widget on the page with its configured properties (including Level 2 Settings children) |
| `sitefinity_get_widget_properties` | Full property details for a single widget by GUID + page identifier. Returns both Level 1 properties and Level 2 Settings children (designer field values, content, etc.) with higher truncation limits |
| `sitefinity_get_page_widget_tree` | Page composition as a placeholder tree in sibling render order. Layout controls own nested `_Col00/_Col01` child placeholders; widget Properties is a merged Level 1 + Level 2 view (Level 2 wins). Empty columns are pre-created from layout captions so structure is visible |
| `sitefinity_list_content` | Paged live content items for any Sitefinity type (News, Blog, Module Builder types, etc.). Returns Id, Title, UrlName, Status, DateCreated, LastModified so widgets can reference real content Ids |
| `sitefinity_list_templates` | All CMS page templates — Id, Name, Framework (MVC/WebForms), ParentTemplateId, Culture |
| `sitefinity_list_taxonomies` | All classifications (Categories, Tags, custom) plus a sample of top-level taxa keyed by taxonomy Id |
| `sitefinity_list_forms` | All Sitefinity forms with field count and submission count |
| `sitefinity_get_form_fields` | Field definitions for a given form — returns the **developer Name** (the `FieldName` the Sitefinity API uses for entry values, e.g. `FormTextBox_C001`), Title, FieldType, IsRequired, and Choices. Pass `debug=true` to also dump the raw Properties/ChildProperties tree (useful when Name/Title come back empty on an unfamiliar Sitefinity version) |
| `sitefinity_list_form_responses` | Form submissions, newest-first, with an optional case-insensitive `searchTerm` that filters entries by any field value (or IP / UserAgent). Sensitive-named field values (password/secret/apiKey/token/...) are redacted **before** search matching so sensitive values cannot leak via search |
| `sitefinity_list_config_sections` | List the names of all registered Sitefinity configuration sections (systemConfig, securityConfig, multisiteConfig, …) — discover a valid name before reading one |
| `sitefinity_get_config_section` | Flattened name/value dump of a single config section. Credential-like values (keys, passwords, connection strings, `[SecretData]`) are **always** redacted — in every environment, with no flag to reveal them |
| `sitefinity_where_used` | Reverse "where used" lookup: find every page and template that references a widget type, content item, or page template. Run this before deleting or refactoring a shared resource |
| `sitefinity_get_permissions` | Inspect effective per-role permissions (granted/denied actions, parent inheritance) on a page or content item — answers "why can't this role see/edit this?" |
| `sitefinity_clear_cache` | **Write.** Clear Sitefinity caches (`output`, `whole`, or single `page`) to see widget/template changes fast. Gated by `allowWriteOperations` + admin switch; never permitted for prod-like environments |
| `sitefinity_recycle_app` | **Write.** Recycle the Sitefinity application so code/config/binding changes take effect. Gated by `allowWriteOperations` + admin switch; never permitted for prod-like environments |
| `sitefinity_list_environments` | Show configured environments |
| `sitefinity_set_default_environment` | Switch active environment |

### Page Inspector

The page tools give your AI assistant visibility into Sitefinity's CMS page structure — something that's otherwise locked inside the database and only visible through the Sitefinity backend UI.

**`sitefinity_list_page_routes`** — Uses Sitefinity's **Sitemap API** (`FrontendSiteMap` provider) for performance. The sitemap is an in-memory cached representation of the page tree, so listing hundreds of pages is fast without hitting `PageManager` queries. Returns each page's URL, title, depth in the tree, and flags pages that use dynamic URL evaluation (which can cause routing surprises).

**`sitefinity_get_page_details`** — Returns everything about a single page: metadata (ID, title, URL, template name, published status) and **every widget placed on the page** with their configured properties. Each widget includes its GUID, CLR type, placeholder location, caption, whether it's a layout control, Level 1 properties, and Level 2 Settings children (the actual designer field values). Accepts flexible lookup by:
- **Page ID** (Guid) — `fefefa59-f39a-4ac9-bf2f-a54d005f135d`
- **URL path** — `/about/team`
- **URL slug** — `team`
- **Page title** — `Our Team` (exact match preferred, partial match with warning)

**`sitefinity_get_widget_properties`** — Returns full property details for a single widget by its GUID and the page it's on (both from `sitefinity_get_page_details` results). Returns both Level 1 properties (ControllerName, ID, Settings) and Level 2 Settings children (SharedContentID, ProviderName, Model JSON, etc.) with higher truncation limits than the page-level view. Use this when you need to inspect the actual configured values of a specific widget.

**`sitefinity_get_page_widget_tree`** — The full page *composition*: every widget on the page returned as a placeholder tree in sibling render order. Layout controls own child placeholders named `{ControlId}_Col00`, `_Col01`, … — their internal columns nest as child `Placeholders` on the layout `WidgetNode`. Each widget's `Properties` is a *merged* view of Level 1 (ORM) + Level 2 (Settings children), with Level 2 winning on conflict (what the widget designer actually saved). Empty columns are pre-created from the layout's `grid-8+4` caption so the LLM can see intended structure. Pass `includeLayoutControls=false` to flatten layout nodes.

### Live Content Queries

These three tools give the LLM direct visibility into real content, templates, and classifications so it can generate widget configs and code against *actual* Ids — not placeholder strings.

**`sitefinity_list_content`** — Paged list of live content items for any type full name (e.g., `Telerik.Sitefinity.News.Model.NewsItem` or a Module Builder dynamic type). Returns Id, Title, UrlName, Status, DateCreated, LastModified. Use `sitefinity_list_dynamic_types` first to discover available type names.

**`sitefinity_list_templates`** — Every page template (MVC and WebForms), including template Id, parent template, and culture. Handy when generating a new page definition that needs to pin to a real template.

**`sitefinity_list_taxonomies`** — Every classification (Categories, Tags, and any custom ones) plus a sample of top-level taxa keyed by taxonomy Id. So a widget configured with category/tag filters can reference the real taxon Ids.

### Forms & Submissions

**`sitefinity_list_forms`** — All Sitefinity forms with their field count and submission count.

**`sitefinity_get_form_fields`** — Field definitions for one form (by Id or Name). Returns each field's **developer name** (the `FieldName` the Sitefinity API uses when reading entry values — e.g. `FormTextBox_C001`), its display Title, FieldType, IsRequired flag, and Choices (for dropdowns/radios). Pass `debug=true` to additionally return a raw Properties/ChildProperties tree dump — useful for diagnosing empty `Name`/`Title` on unfamiliar Sitefinity versions where the metadata lives at a different path under the control's Settings tree.

**`sitefinity_list_form_responses`** — Form submissions for a form, ordered newest-first. Pass `searchTerm` to return only entries where any field value (or `IpAddress` / `UserAgent`) contains the term (case-insensitive substring). The response includes `TotalCount` (all entries on the form), `MatchedCount` (entries after the search filter), and echoes the `SearchTerm` you sent. Use `take`/`skip` to page through the matched set. Any field whose name looks sensitive (`Password`, `ApiKey`, `Secret`, `Token`, …) is scrubbed **before** leaving Sitefinity *and before* search matching runs, so sensitive values can never leak via a crafted search term.

### Config, Permissions & Where-Used

**`sitefinity_list_config_sections`** / **`sitefinity_get_config_section`** — Configuration lives across the database and `.config` files and is otherwise only visible through the admin UI. List the registered section names, then dump one as a flattened list of dotted/indexed name/value paths. Credential-like values (keys, passwords, connection strings, tokens, `[SecretData]`/encrypted properties) are **always** redacted on the plugin side before transit — in every environment, with no flag to reveal them.

**`sitefinity_where_used`** — Sitefinity has no built-in "where used" view. Pass a Guid (content item or template id) or a widget/controller type name (e.g. `ContentBlock`, `MvcControllerProxy`) and it scans every page and template for references, returning each host page/template, the widget carrying the reference, and why it matched. The kind is auto-detected; override it with `kind` when needed. Use it before deleting or refactoring a shared resource.

**`sitefinity_get_permissions`** — Resolves the effective permissions on a page or content item: which roles are granted or denied which actions (View, Modify, Delete, Create, ChangePermissions, …) across each permission set, and whether the object inherits from its parent. Pass a page identifier (Guid, URL, or title), or a content item Guid plus its `typeFullName`. Answers "why can't this role see or edit this?"

### Maintenance (Write Operations)

**`sitefinity_clear_cache`** and **`sitefinity_recycle_app`** are the only write tools in the server — the inner loop of widget/template development. `clear_cache` invalidates the `output` cache (default), the `whole` Sitefinity cache, or a single `page`'s output cache; `recycle_app` restarts the application so code/config/binding changes take effect.

Because they change state, they are gated on **both** sides and both must opt in:

1. **MCP server side** — the target environment must set `"allowWriteOperations": true` in `sitefinity-mcp.json`. `EffectiveAllowWriteOperations` is prod-guarded, so any environment whose name starts with `prod` is always refused regardless of the flag. The tool refuses before any network call when this is off.
2. **Plugin side** — **Allow Write Operations** must be checked in Sitefinity Admin > Advanced > McpSettings (default off). When it's off, the `/mcp/cache/clear` and `/mcp/app/recycle` endpoints return HTTP 403.

### Secret Redaction

Every string returned by log tools, widget tools, form response tools, the config reader, and list_content flows through a deny-list + pattern scanner. Values keyed by `Password`, `ApiKey`, `Secret`, etc. become `[REDACTED]`; embedded JWTs, AWS keys, GitHub PATs, Slack tokens, OpenAI keys, Azure connection strings, and `Password=...` connection-string fragments are replaced with `[REDACTED:<kind>]` tags. **Redaction is unconditional — there is no flag to disable it, in any environment including dev.** A raw secret in the LLM context is a leak (it can be logged, cached, or absorbed into model training data), so the server never emits one.

---

## Security Model

**Enabled flag** — The `Enabled` checkbox in Sitefinity Admin > Advanced > McpSettings acts as a kill switch with two enforcement layers:

1. **Startup gate** — `McpInit.Register()` checks `Enabled` and `ApiKey` before registering the ServiceStack plugin. If either is disabled/blank, the `/RestApi/mcp/*` routes don't exist at all (404, no attack surface). Requires app pool recycle to toggle.
2. **Runtime gate** — The `[McpApiKey]` request filter attribute checks `Enabled` on every request. If someone disables MCP in admin after startup, requests are immediately blocked without an app pool recycle.

**Encryption at rest** — The API key in Sitefinity's config is marked with `[SecretData]`, so it's stored encrypted in `McpConfig.config`. Sitefinity decrypts it transparently when the property is read in code.

**Blank key protection** — Blank, empty, and whitespace-only keys are rejected at every checkpoint:
- MCP server config validation (`IsNullOrWhiteSpace`) — server won't start
- Sitefinity startup (`McpInit.Register`) — plugin won't register endpoints
- Sitefinity request filter (`McpApiKeyAttribute`) — requests blocked at runtime

**Write operations are double-gated** — The two state-changing tools (`sitefinity_clear_cache`, `sitefinity_recycle_app`) require **both** `"allowWriteOperations": true` in the per-environment config *and* the **Allow Write Operations** admin switch (default off). The server refuses before any network call when its flag is off; the plugin returns HTTP 403 when its switch is off. Prod-like environment names can never write, regardless of either flag.

**No secret opt-out** — Secret redaction is unconditional. There is no `allowRawSecrets` flag (it was removed) and no environment — including dev — can disable scrubbing. A raw secret in an LLM context is a leak, so the server never emits one.

---

## Architecture

**Two-component design:**

1. **MCP Server** (this repo) — .NET console app using the official [ModelContextProtocol SDK](https://www.nuget.org/packages/ModelContextProtocol). Communicates with Claude Code via stdio.
2. **Sitefinity Plugin** (source files) — `.cs` files you drop into any Sitefinity web app. Registers ServiceStack endpoints at `/RestApi/mcp/*` for remote log access. Compiles against your existing assemblies — no DLL conflicts.

```
┌─────────────┐    stdio     ┌──────────────────┐
│ Claude Code  │◄───────────►│   MCP Server     │
│ (MCP Client) │             │ (.NET console)   │
└─────────────┘              └────────┬─────────┘
                                      │
                        ┌─────────────┼─────────────┐
                        │             │             │
                   Local logs    HTTP + API Key     │
                   (if logsPath  (X-MCP-API-Key)    │
                    is set)       │                  │
                        │         ▼                  ▼
                        │  ┌─────────────┐   ┌─────────────┐
                        │  │ Sitefinity  │   │ Sitefinity  │
                        │  │ (Dev)       │   │ (Staging)   │
                        │  │ /RestApi/   │   │ /RestApi/   │
                        │  │  mcp/*      │   │  mcp/*      │
                        │  └─────────────┘   └─────────────┘
                        │     Plugin ▲          Plugin ▲
                        │     source │          source │
                        ▼     files  │          files  │
                  ┌──────────┐       │                 │
                  │ Log files│  Sitefinity APIs:       │
                  │ on disk  │  SystemManager,         │
                  └──────────┘  ModuleBuilder,         │
                                MultisiteManager       │
                                AppSettings            │
```

**Local mode** (dev): MCP server reads log files directly from disk via `logsPath`.
**Remote mode** (staging/prod): MCP server calls plugin REST endpoints at `/RestApi/mcp/*`, authenticated with `X-MCP-API-Key` header. The plugin queries Sitefinity's internal APIs and returns results as JSON.

---

## Adding New Tools

Create a class, annotate it, inject services — done. Zero changes to Program.cs:

```csharp
[McpServerToolType]
public sealed class ContentTools(ISitefinityStatusService status)
{
    [McpServerTool(Name = "sitefinity_list_pages", ReadOnly = true)]
    [Description("List Sitefinity CMS pages.")]
    public async Task<string> ListPages(CancellationToken ct = default)
    {
        var s = await status.CheckStatusAsync(ct);
        if (!s.IsReady) return $"Sitefinity not ready: {s.Summary}";
        // ... call Sitefinity OData API ...
    }
}
```

---

## Testing

### Setup

1. Copy `tests/test-config.example.json` to `tests/test-config.json`
2. Fill in your Sitefinity dev URL and API key (this file is gitignored)

### Running Tests

```bash
# All tests (unit + integration)
dotnet test

# Unit tests only (no Sitefinity needed)
dotnet test --filter "Category=Unit"

# Integration tests only (requires running Sitefinity)
dotnet test --filter "Category=Integration"
```

Integration tests skip automatically if `test-config.json` is missing or
Sitefinity is unreachable — they won't fail your build.

---

## Author

Built by **Steve McNiven-Scott** ([@SitefinitySteve](https://www.sitefinitysteve.com) | [GitHub](https://github.com/sitefinitysteve)) — Sitefinity MVP and long-time community contributor. Building developer tools for the Sitefinity community.

## License

MIT

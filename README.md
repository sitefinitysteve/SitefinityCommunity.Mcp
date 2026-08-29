# SitefinityCommunity.Mcp

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server for **Sitefinity CMS**. Gives Claude Code (and any MCP client) direct access to Sitefinity logs, diagnostics, and CMS status.

[Buy Me A Coffee](https://buymeacoffee.com/stevewgw)

Inspired by [Laravel Boost](https://github.com/nicepkg/laravel-boost) — designed as an extensible framework where adding new tools requires only creating a new class file.

## Why this exists

MCP servers are popping up across the web development ecosystem — Laravel has Boost, Rails and Next.js have their own — and Sitefinity deserves one too.

I built this for myself. I work in Sitefinity every day and wanted the same AI-assisted workflow that other frameworks already have. Sitefinity's official repos exist on GitHub, but community PRs tend to sit — and when you need something for your day-to-day, you can't wait indefinitely. This scratches my own itch first: if a feature would save me time on a real project, it gets built. That means it stays practical and actively maintained, not theoretical.

It's open source and community-driven — contributions, ideas, and feedback are welcome. No guarantees on timelines (this is a side project), but because I use it daily, useful improvements tend to land quickly.

## Features

- **Log Tools** — Read any Sitefinity log (defaults to `Error.log`), regex-search across every log file with context, list log files with size and modified date
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
- **Incident Investigation** — One tool correlating Sitefinity logs, the IIS access log, the Windows event logs, and HTTPERR across one window, in both UTC and server-local time
- **Capability Toggles** — Admins switch off any capability (or an individual incident log source) from Sitefinity's admin UI; enforced plugin-side, effective immediately, all enabled by default
- **API Key Validation** — Proactive key matching between MCP server and Sitefinity plugin

## Installation

There are two components to set up: the **MCP server** (runs on your dev machine) and the **Sitefinity plugin** (drops into your Sitefinity web app). Follow these steps in order.

### Step 1 — Install the MCP server

```bash
npm install -g sitefinity-comm-mcp
```

That's it — no .NET required. The package downloads a self-contained binary for your platform (Windows x64, Linux x64, macOS x64/arm64) and puts `sitefinity-comm-mcp` on your PATH. Upgrade with `npm update -g sitefinity-comm-mcp`.

Prefer the .NET ecosystem? `dotnet tool install -g SitefinityCommunity.Mcp` installs the same command via NuGet. Contributors can still clone and `dotnet build`.

### Step 2 — Install the plugin into your Sitefinity project

The companion plugin exposes REST endpoints at `/RestApi/mcp/*` that the MCP server calls. It's distributed as source files (not a NuGet package) so it compiles against your existing Sitefinity assemblies — no DLL binding conflicts across versions.

Run the installer from your Sitefinity web app root (it verifies a `web.config` is present before touching anything; re-run it (or its alias `update-plugin`) any time to refresh an existing install after a CLI update), or point at it with `--target`. It writes the plugin sources (embedded in the CLI) and registers them in your `.csproj`:

```bash
cd C:\Path\To\SitefinityWebApp
sitefinity-comm-mcp install-plugin
```

```bash
# or, from anywhere:
sitefinity-comm-mcp install-plugin --target "C:\Path\To\SitefinityWebApp"
```

(Working from a repo checkout instead? `.\install-plugin.ps1 -Target ...` does the same thing.)

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
sitefinity-comm-mcp generate-key
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
        "sitefinity-comm-mcp": {
            "type": "stdio",
            "command": "sitefinity-comm-mcp",
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
        "sitefinity-comm-mcp": {
            "command": "sitefinity-comm-mcp",
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

The repo ships **16 curated skills** that teach AI agents how to think about Sitefinity widgets, pages, data access, and tooling. They follow the open [Agent Skills](https://agentskills.io) standard (`skills/<name>/SKILL.md`), so they install into Claude Code, Codex, Cursor, and GitHub Copilot.

#### Recommended — `npx skills` (no clone, cross-platform)

The [`skills`](https://github.com/vercel-labs/skills) CLI auto-discovers every `skills/*/SKILL.md` in the repo — no clone, no manifest, works on Windows / macOS / Linux. It also drives updates.

```powershell
# Install all 16 skills into the current project (auto-detects your agent)
npx skills add github:sitefinitysteve/SitefinityCommunity.Mcp --skill "*" -y

# Pick specific agents (repeat -a per agent; a comma list is rejected)
npx skills add github:sitefinitysteve/SitefinityCommunity.Mcp --skill "*" -a claude-code -a codex -a cursor -a github-copilot -y

# Install globally (user-level) instead of per-project
npx skills add github:sitefinitysteve/SitefinityCommunity.Mcp --skill "*" -g -y

# Install a single skill
npx skills add github:sitefinitysteve/SitefinityCommunity.Mcp --skill sitefinity-best-practices -y

# Update everything you've installed to the latest committed versions
npx skills update -y
```

Valid agent names are `claude-code`, `codex`, `cursor`, and `github-copilot`. Claude Code lands the skills in `.claude/skills/`; the other agents share a canonical `.agents/skills/` copy that the CLI wires up for you. Add `--copy` to copy files instead of symlinking (Windows without Developer Mode).

#### Fallback — `install-skills.ps1` (cloned repo / offline)

If you've already cloned the repo, or you're offline, the bundled PowerShell installer does the same job locally. It walks you through two choices: scope (project or global) and which agents to install into (detected ones are pre-selected).

```powershell
# Interactive — prompts for scope and agents
.\install-skills.ps1

# Non-interactive / CI
.\install-skills.ps1 -Scope project -Target "C:\Proj" -Agents claude,cursor -Force
.\install-skills.ps1 -Scope global  -Agents claude -Force
```

**How it installs:** a canonical copy goes to `<root>/.agents/skills/<name>/`, then each selected agent's skills directory (`.claude/skills/`, `.cursor/skills/`, `.codex/skills/`, `.github/copilot/skills/`) gets a symlink back to it — update the canonical copy once and every agent sees it. On Windows, directory symlinks need Developer Mode (Settings → System → For developers) or an admin shell; without them the installer falls back to plain copies automatically. To update, `git pull` then re-run `install-skills.ps1`.

#### Bundled skills

Start with **`sitefinity-best-practices`** — it's the read-this-first entry point that establishes context and routes you to the right deep skill.

| Skill | Purpose |
|---|---|
| **`sitefinity-best-practices`** | **Read-this-first foundation; routes you to the right skill** |
| `sitefinity-widget-expert` | Build and troubleshoot MVC widgets: controllers, designer, persistence |
| `sitefinity-designer-attributes` | Full reference for autogenerated widget designer field attributes |
| `sitefinity-servicestack-api` | Build JSON/REST `/RestApi` services; fix DateTime serialization |
| `sitefinity-odata-services` | Query native and dynamic module content via the built-in `/api/default` OData service - zero code |
| `sitefinity-adminapp-extensions` | Angular AdminApp custom field editors for the backend UI |
| `sitefinity-vue3-vite8-guide` | Add a Vue 3 + Vite 8 + Tailwind frontend to Sitefinity |
| `sitefinity-poco-generator` | Generate strongly-typed C# POCOs from Module Builder types |
| `sitefinity-page-inspector` | Inspect page widgets and their configured property values |
| `sitefinity-page-controls-map` | Quick map of how pages store widgets and properties |
| `sitefinity-page-surgery` | Write migration code to change pages, widgets, templates |
| `sitefinity-database-structure` | Grounded reference for the Sitefinity CMS database structure |
| `sitefinity-binding-doctor` | Diagnose and fix .NET assembly binding YSOD errors |
| `sitefinity-cli-build` | Build, test, and package a Sitefinity solution from the CLI |
| `sitefinity-debloat-repo` | Stop committing build artifacts; make them reproducible; purge git bloat |
| `sitefinity-toolbox-icons` | Pick toolbox icon CSS classes for new MVC widgets |

---

## Available Tools

<a id="available-tools"></a>

| Tool | Description |
|------|-------------|
| `sitefinity_read_log_file` | The newest parsed entries from a log file — `fileName` defaults to `Error.log`, so no arguments gives the last 10 errors with stack traces; `count: 1` gives just the most recent. `Trace.log` and any name from `sitefinity_list_log_files` also work |
| `sitefinity_search_logs` | Regex search across all logs with context |
| `sitefinity_list_log_files` | All .log files with size and modified date |
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
| `sitefinity_get_config_section` | Flattened name/value dump of a single config section — **overrides only** by default, with `pathFilter` / `maxEntries` / `includeDefaults`. Credential-like values (keys, passwords, connection strings, `[SecretData]`) are **always** redacted — in every environment, with no flag to reveal them |
| `sitefinity_search_settings` | Full-text search across ALL Advanced Settings via the backend `advanced-settings-search` Lucene index (Sitefinity 14.1+) — find which section a setting lives in, then dump it with `sitefinity_get_config_section` |
| `sitefinity_where_used` | Reverse "where used" lookup across every page and template — find a widget type, content item, template, or (with `kind=property`) any property-value substring. Template hits expand to the pages that ride them, so you see what actually breaks. Run before deleting or refactoring a shared resource |
| `sitefinity_get_permissions` | Effective per-role permissions on a page or content item, deny-resolved — surfaces whether it's **public**, viewable by authenticated users, and whether it inherits from a parent, plus granted/denied actions per set |
| `sitefinity_clear_cache` | **Write.** Clear Sitefinity caches (`output`, `whole`, or single `page`) to see widget/template changes fast. Gated by `allowWriteOperations` + admin switch; never permitted for prod-like environments |
| `sitefinity_recycle_app` | **Write.** Recycle the Sitefinity application so code/config/binding changes take effect. Gated by `allowWriteOperations` + admin switch; never permitted for prod-like environments |
| `sitefinity_investigate_incident` | **The outage tool.** Correlates Sitefinity logs + IIS W3C access logs + Windows Application/System event logs + http.sys HTTPERR across one time window, with every timestamp in both UTC and server-local. Three modes: **discovery** (no args — find candidate crash moments over the last N hours), **window** (`time` — full reconstruction of that moment), **search** (`query` — sweep every source for a substring, e.g. one user's request trail) |
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

**`sitefinity_list_config_sections`** / **`sitefinity_get_config_section`** — Configuration lives across the database and `.config` files and is otherwise only visible through the admin UI. List the registered section names, then dump one as a flattened list of dotted/indexed name/value paths.

By default this returns **overrides only** — the values someone actually changed. Sitefinity hands back a fully defaults-merged object graph, so including defaults makes `ContentViewConfig` alone expand to roughly 375,000 entries / 79 MB, which is large enough to kill the stdio transport outright. Defaults are detected through the config model's own metadata (`ConfigElement.Source`, `ConfigProperty.DefaultValue`, `ConfigProperty.SkipOnExport`). Use `pathFilter` to narrow to a subtree (e.g. `contentViewControls[NewsBackend]`), `maxEntries` to raise the 500-entry cap (max 5000), and `includeDefaults` when you genuinely need the unset values too. The response always reports the true `TotalCount` even when truncated. Credential-like values (keys, passwords, connection strings, tokens, `[SecretData]`/encrypted properties) are **always** redacted on the plugin side before transit — in every environment, with no flag to reveal them.

**`sitefinity_where_used`** — Sitefinity has no built-in "where used" view. Pass a Guid (content item or template id), a widget/controller type name (e.g. `ContentBlock`, `MvcControllerProxy`), or — with `kind=property` — any substring to match inside widget property values (a CSS class, URL, or snippet). It scans every page **and** template, and because a widget that lives on a template renders on every page riding that template, template-hosted matches are expanded into the affected pages (transitively through template inheritance) — so the result shows what actually breaks if you change it. Each hit reports the host page/template, the matching widget (origin, placeholder, and the matched property/snippet), why it matched, and which template an inherited hit came from. The kind is auto-detected; override it with `kind` when needed. Use it before deleting or refactoring a shared resource.

**`sitefinity_get_permissions`** — Resolves the effective permissions on a page or content item, decoding the runtime grant/deny bitmasks into real access (deny wins over grant) per role. Answers the headline questions directly: **is it public** (the Everyone role can View), is it viewable by any authenticated user, does it **inherit** permissions and from which parent, and what each role can actually do (View, Modify, Delete, Create, ChangePermissions, …) across each permission set. Pass a page identifier (Guid, URL, or title), or a content item Guid plus its `typeFullName`.

### Incident Investigation

**`sitefinity_investigate_incident`** answers "the site went down around 11:00 — what happened?" in one call. Sitefinity's own log only ever tells part of the story: when the app pool itself dies, the interesting evidence lives in the Windows **System** event log (WAS 5009/5010/5011/5117) and in **HTTPERR**, where http.sys records the 503s that never reached the site at all. This tool reads all four sources together:

| Source | What it contributes | Clock |
|--------|--------------------|-------|
| Sitefinity logs | Parsed Error/Trace entries in the window, errors first | server-local |
| IIS W3C access log | Per-minute request counts, status histogram (with sub-status, `503.2` vs `500.0`), every 5xx, the slowest requests | **always UTC** |
| Windows Event Log (Application + System) | App-pool crashes, `Application Error` 1000, `.NET Runtime` 1026. Security is never read | UTC |
| HTTPERR (`C:\Windows\System32\LogFiles\HTTPERR`) | http.sys 503s by reason — `AppOffline`, `QueueFull`, `Timer_ConnectionIdle` — the requests that never reached the site log | **always UTC** |

Because those clocks disagree, **every entry carries both `TimestampUtc` and `TimestampLocal`**, and the response reports `ServerTimeZoneId` plus the UTC offset that applied *at the queried instant* (not "now", which would be wrong across a DST boundary).

Three modes, picked by which arguments you pass — in practice you just ask, and the assistant picks:

| Ask | Mode | Call |
|-----|------|------|
| "The site went down sometime this week — when?" | discovery | `sitefinity_investigate_incident()` |
| "What happened at 11am?" | window | `sitefinity_investigate_incident(time: "11:00")` |
| "Trace everything steve@medportal.ca did in the last 3 days" | search | `sitefinity_investigate_incident(query: "steve@medportal.ca", lookbackHours: 72)` |

1. **Discovery** — no `time`, no `query`. "The site crashed sometime this week, when?" Scans the cheap high-signal sources over `lookbackHours` (default 72, max 336) and returns clustered candidate moments, newest first, each with a headline signal like `WAS 5011 worker process crash` or `HTTPERR 503 burst (AppOffline x142)`. Signals within 10 minutes fold into one candidate. It deliberately does **not** scan the IIS access log — sweeping it over multiple days is far too expensive.
2. **Window** — pass `time` (`"11:00"`, `"2026-08-27 11:00"`, or full ISO 8601; read as **server-local** unless it carries an explicit offset or `Z`). Returns the full correlated reconstruction of `time ± windowMinutes` (default 15, max 120).
3. **Search** — pass `query` without a `time`. Sweeps every source over `lookbackHours` for a case-insensitive plain substring — one user's full request trail (`query: "steve@medportal.ca"`), an order id, a URL path. The IIS log *is* scanned here; the line ceiling and time budget bound it.

Passing `time` **and** `query` filters that window to matching entries. For IIS in that mode you get the matching requests at **all** status codes (not just 5xx — the whole request trail is the point), while the aggregates still cover the unfiltered window so traffic context survives.

**Guardrails.** Raw IIS log lines are never returned — only aggregates plus capped lists (20 Sitefinity entries, 25 per event-log channel, 25 5xx, 10 slowest, 25 HTTPERR, 50 matched requests, 20 candidates), each reporting its true total and a `Truncated` flag. Scanning stops at 2,000,000 lines or a 30-second wall-clock budget, whichever comes first, and says so in `Warnings` rather than hanging. The endpoint is plain synchronous request/response — no background jobs.

**Privacy stance.** Query strings are parsed and redacted per parameter (deny-listed names lose their values, then the whole string is pattern-scanned), and the `cs(Cookie)` / `cs(Authorization)` columns are **never read at all** — a redacted credential is still a credential-shaped string in an LLM context. Client IPs and `cs-username` **are** returned deliberately: correlating an outage to who was hitting what is the entire purpose. Query matching runs **after** redaction, so `query` can never be used as an oracle to confirm a value the redactor removed.

**Prerequisites (OS-level).** The app pool identity needs permission to read three things outside the web root. Nothing fails hard when it can't — the response carries a warning with the exact fix — but you get less evidence. Run these elevated on the web server, substituting your app pool name and log paths:

```powershell
# 1. Windows event logs (Application + System) — add the app pool identity to Event Log Readers
net localgroup "Event Log Readers" "IIS APPPOOL\YourAppPoolName" /add

# 2. IIS W3C access log folder (site id from Admin > Advanced > McpSettings > Incident > IIS Log Path,
#    or the auto-detected %SystemDrive%\inetpub\logs\LogFiles\W3SVC{siteId})
icacls "C:\inetpub\logs\LogFiles\W3SVC1" /grant "IIS APPPOOL\YourAppPoolName:(OI)(CI)(RX)"

# 3. HTTPERR folder
icacls "C:\Windows\System32\LogFiles\HTTPERR" /grant "IIS APPPOOL\YourAppPoolName:(OI)(CI)(RX)"
```

Then recycle the app pool so the token picks up the new group membership. The IIS log folder is auto-detected as `%SystemDrive%\inetpub\logs\LogFiles\W3SVC{siteId}` from `HostingEnvironment.ApplicationID`; override it with **IIS Log Path** in Sitefinity Admin > Advanced > McpSettings > **Incident** when the site logs elsewhere or the site id can't be resolved (virtual applications).

**Turning sources off.** The same **Incident** node has **Allow IIS Logs**, **Allow Event Logs** and **Allow HTTPERR** checkboxes (all on by default). Unchecking one doesn't fail the call — that source is skipped and the response says so in `Warnings`, exactly like a permissions failure. Unchecking **Enabled** on the Incident node blocks the endpoint entirely (HTTP 403). See [Capability Toggles](#capability-toggles).

### Maintenance (Write Operations)

**`sitefinity_clear_cache`** and **`sitefinity_recycle_app`** are the only write tools in the server — the inner loop of widget/template development. `clear_cache` invalidates the `output` cache (default), the `whole` Sitefinity cache, or a single `page`'s output cache; `recycle_app` restarts the application so code/config/binding changes take effect.

Because they change state, they are gated on **both** sides and both must opt in:

1. **MCP server side** — the target environment must set `"allowWriteOperations": true` in `sitefinity-mcp.json`. `EffectiveAllowWriteOperations` is prod-guarded, so any environment whose name starts with `prod` is always refused regardless of the flag. The tool refuses before any network call when this is off.
2. **Plugin side** — **Allow Write Operations** must be checked in Sitefinity Admin > Advanced > McpSettings (default off). When it's off, the `/mcp/cache/clear` and `/mcp/app/recycle` endpoints return HTTP 403.

### Capability Toggles

<a id="capability-toggles"></a>

A Sitefinity administrator can switch off any capability area independently, in **Admin > Settings > Advanced > McpSettings**. Each one is an expandable node with an **Enabled** checkbox.

**Everything is enabled by default** — upgrading an existing install changes nothing until you uncheck something.

| Node | Turning it off blocks |
|------|-----------------------|
| **Logs** | `sitefinity_read_log_file`, `sitefinity_search_logs`, `sitefinity_list_log_files` (remote mode; a `logsPath` environment still reads the filesystem locally) |
| **Metadata** | Site info, modules, dynamic types, routes, page details, widget properties, widget tree, templates, taxonomies |
| **Content** | `sitefinity_list_content` |
| **Forms** | `sitefinity_list_forms`, `sitefinity_get_form_fields`, `sitefinity_list_form_responses` — plus **Allow Responses** (keep definitions readable, block submissions) and **Excluded Fields** (strip named fields from every submission) |
| **Config Reader** | `sitefinity_list_config_sections`, `sitefinity_get_config_section`, `sitefinity_search_settings` — plus **Excluded Sections**, which hides named sections (`*` wildcards supported) from the list, direct reads, and settings search |
| **Where Used** | `sitefinity_where_used` |
| **Permissions** | `sitefinity_get_permissions` |
| **Incident** | `sitefinity_investigate_incident` — plus **Allow IIS Logs** / **Allow Event Logs** / **Allow HTTPERR** for its individual sources, and the **IIS Log Path** override |

Cache clear and recycle need no node of their own — the existing **Allow Write Operations** checkbox already gates them.

**Changes apply on the next request — no app pool recycle.** (Unlike the top-level `Enabled` kill switch, which also skips route registration at startup.)

**The plugin enforces this, not the client.** A disabled capability returns **HTTP 403** with a `{ "Disabled": "<name>", "Reason": "…" }` body to *anything* that calls it — this MCP server, curl, a script, anything. As a convenience the MCP server also reads the current state from `/mcp/ping` and refuses a disabled tool up front with "This tool is disabled by the Sitefinity administrator (Admin > Advanced > McpSettings > Forms)", saving a round trip; that check is a shortcut, never the boundary, so a stale cache can't grant access. `/mcp/ping` itself is never blocked — it's how the server learns what's off.

### Secret Redaction

**Everything this server hands to an LLM is scrubbed first.** Two mirrored redactors do the work — `McpSecretRedactor.cs` inside Sitefinity (so secrets never leave the server) and `Security/SecretRedactor.cs` in the MCP server (for logs read straight off the filesystem in local mode). Both apply the same two layers:

1. **Field-name deny-list** — a value keyed by `Password`, `ApiKey`, `Secret`, `Token`, `Authorization`, or anything containing `*secret*` / `*password*` is replaced wholesale with `[REDACTED]`.
2. **Value-pattern scanner** — embedded JWTs, bearer headers, AWS keys, GitHub PATs, Slack and OpenAI tokens, Azure storage keys, App Insights instrumentation keys, and `Password=…` connection-string fragments become `[REDACTED:<kind>]` regardless of what they're keyed by.

This covers log tools, page/widget properties, form submissions, `list_content`, the config reader, the settings search, and every incident source.

**Redaction is unconditional. There is no flag to disable it, in any environment, including dev.** A raw secret in an LLM context is a leak — it can be logged, cached, or absorbed into model training data — so the server never emits one. The config reader over-redacts on purpose: anything credential-shaped is withheld even if it wasn't actually a secret.

**Search can't be used as an oracle.** Wherever a search or filter term is matched — `sitefinity_list_form_responses`'s `searchTerm`, `sitefinity_investigate_incident`'s `query` — matching runs **after** redaction. You cannot confirm a redacted value by searching for it.

**Incident sources specifically:**

- IIS **query strings** are split into `name=value` pairs and deny-listed *per parameter name* before the whole string is pattern-scanned, so `?token=abc` loses its value even though the URL as a whole looks innocuous.
- `cs(Cookie)` and `cs(Authorization)` are **never read at all** — not read-then-redacted. A redacted credential is still a credential-shaped string sitting in a context window.
- Raw IIS log lines are never returned; only aggregates and capped, redacted lists.

**Admins can hide specific data outright**, beyond what the redactor catches: **Forms > Excluded Fields** strips named form fields (`SSN, HealthCard`) from every submission, and **Config Reader > Excluded Sections** hides whole config sections (`*` wildcards supported) from the section list, direct reads, and settings search. Both are removed server-side *before* redaction and *before* any search term is matched, so — like redaction — they can't be discovered by searching for their values. **Forms > Allow Responses** goes further: uncheck it and form definitions stay readable while submissions are refused entirely.

**One deliberate exception:** `cs-username` and client IPs (`c-ip`) **are** returned. Correlating an outage to who was hitting what is the entire point of the tool, and they aren't credentials. If that's not acceptable for your site, uncheck **Allow IIS Logs** under Admin > Advanced > McpSettings > Incident.

---

## Security Model

**Enabled flag** — The `Enabled` checkbox in Sitefinity Admin > Advanced > McpSettings acts as a kill switch with two enforcement layers:

1. **Startup gate** — `McpInit.Register()` checks `Enabled` and `ApiKey` before registering the ServiceStack plugin. If either is disabled/blank, the `/RestApi/mcp/*` routes don't exist at all (404, no attack surface). Requires app pool recycle to toggle.
2. **Runtime gate** — The `[McpApiKey]` request filter attribute checks `Enabled` on every request. If someone disables MCP in admin after startup, requests are immediately blocked without an app pool recycle.

**Per-capability toggles** — Each capability (Logs, Metadata, Content, Forms, Config Reader, Where Used, Permissions, Incident) has its own `Enabled` checkbox under **McpSettings**, all on by default. The plugin refuses a disabled capability with HTTP 403 on every request regardless of client. See [Capability Toggles](#capability-toggles).

**Encryption at rest** — The API key in Sitefinity's config is marked with `[SecretData]`, so it's stored encrypted in `McpConfig.config`. Sitefinity decrypts it transparently when the property is read in code.

**Blank key protection** — Blank, empty, and whitespace-only keys are rejected at every checkpoint:
- MCP server config validation (`IsNullOrWhiteSpace`) — server won't start
- Sitefinity startup (`McpInit.Register`) — plugin won't register endpoints
- Sitefinity request filter (`McpApiKeyAttribute`) — requests blocked at runtime

**Brute-force throttle** — Ten failed authentication attempts from one IP inside a five-minute window freeze that IP for fifteen minutes, answered with HTTP 429 and a `Retry-After` header. Missing keys count as failures (they're probes). A **correct key always wins**: it is checked before the freeze is enforced, so it unfreezes the IP and resets the counter — nobody can lock out your MCP server by spraying bad keys from the same egress address. The bucket is the direct connection IP; `X-Forwarded-For` is deliberately ignored, since trusting it would let one client evade the throttle by rotating a header. The whole path fails open — a throttle bug degrades to a plain 401, never a 500.

**Constant-time key comparison** — The presented key is compared to the configured one over the full length of both, accumulating inequality instead of returning at the first mismatch, so response timing can't be used to recover the key a character at a time.

In fairness: a key from `generate-key` is 256 bits of `RandomNumberGenerator` output, so it is not realistically brute-forcible over HTTP with or without these measures. They exist for the cases that actually happen — a short or hand-typed key, a key reused from somewhere else, credential-stuffing noise — and to keep an unauthenticated endpoint from being a free oracle.

### Request Audit Log

<a id="request-audit-log"></a>

Every MCP request — accepted **or** rejected — appends one line to `App_Data\Sitefinity\Logs\McpAudit.log`. It's **on by default**; turn it off with **Audit Requests** in Sitefinity Admin > Advanced > McpSettings.

```
2026-08-28T20:41:07Z | ip=203.0.113.9 | xff=- | GET /mcp/forms | Take=25 | auth=valid
2026-08-28T20:41:09Z | ip=198.51.100.4 | xff=- | GET /mcp/logs | | auth=invalid-key attempted: len=25 prefix=PROD-K sha256=e6d28673f272
2026-08-28T20:41:11Z | ip=198.51.100.4 | xff=- | GET /mcp/config | | auth=missing-key
2026-08-28T20:43:22Z | ip=198.51.100.4 | xff=- | GET /mcp/logs | | auth=throttled
```

**Requests only — never results.** It answers "who called what, from where, and did they get in", not "what data came back".

**The two IP fields.** `ip=` is the direct TCP connection address: trustworthy, and what the throttle keys on. `xff=` is the `X-Forwarded-For` header verbatim, or `-` when absent. If your site is directly exposed, **block on `ip=`**. Behind a proxy, load balancer or Cloudflare, `ip=` will be the proxy, so use `xff=` as your lead — but verify it against your proxy's own logs before acting, because the header is caller-supplied and can be forged.

**Rejected keys are fingerprinted, not recorded.** An `invalid-key` line carries `len=`, a 6-character `prefix=`, and the first 12 hex of the key's SHA-256. To identify a mystery key, hash your known keys and compare:

```bash
printf '%s' "$SOME_KEY" | openssl dgst -sha256 | cut -c1-12   # compare with sha256= in the log
```

That's usually enough to spot a stale key from a rotation or a prod key aimed at dev. It's also exactly *why* raw keys are never logged: the invalid keys that show up in practice are nearly-valid ones, so writing them verbatim would turn the audit file into a store of live credentials. **A valid key is never fingerprinted at all** — no prefix, no hash, nothing.

**Everything else is scrubbed too.** Query strings run through the same per-parameter deny-list and pattern scanner as the rest of the server, so `?token=…` lands as `token=[REDACTED]`. Newlines and pipes are stripped from every field so a crafted URL can't forge log lines.

**Operationally boring on purpose.** The file rolls at 10 MB (`McpAudit.1.log` … `McpAudit.3.log`, oldest dropped). The whole path fails open: a locked file, a full disk or a missing Logs folder silently disables auditing rather than affecting the request.

**And the audit trail is itself MCP-inspectable** — it lives in the standard Sitefinity Logs folder, so you can just ask: *"search the MCP audit log for invalid-key attempts today"* and `sitefinity_search_logs` / `sitefinity_read_log_file("McpAudit.log")` will read it like any other log.

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

## Releasing

Releases are cut by tag (see [RELEASING.md](RELEASING.md)): pushing a `vX.Y.Z` tag makes CI build the
self-contained binaries and create the GitHub release. **The npm package is then published manually**
(`cd npm && npm publish --access public`, authenticated interactively with 2FA) — deliberately, so no
publish token ever lives in CI. Publish order matters: GitHub release first, npm second, because the
npm shim's installer downloads the binaries from that release.

## Author

Built by **Steve McNiven-Scott** ([@SitefinitySteve](https://www.sitefinitysteve.com) | [GitHub](https://github.com/sitefinitysteve)) — Sitefinity MVP and long-time community contributor. Building developer tools for the Sitefinity community.

## License

MIT

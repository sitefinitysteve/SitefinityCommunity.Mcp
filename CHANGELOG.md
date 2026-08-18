# Changelog

All notable changes to **SitefinityCommunity.Mcp** are documented here. This project follows [Semantic Versioning](https://semver.org/).

## [2.0.2] — 2026-08-18

### Fixed

- **npm install failed when Git for Windows' GNU tar shadowed the system tar.** The installer extracted with absolute Windows paths, which GNU tar parses as a remote rsh host (`C:` → host `C`). Extraction now runs with relative paths from the package directory, which every tar accepts.

### Added

- npm README: `claude mcp add` one-liner for registering the server with Claude Code.

## [2.0.1] — 2026-08-18

### Fixed

- **ARM support in the npm distribution.** `npm install -g sitefinity-comm-mcp` failed outright on Windows-on-ARM and ARM Linux — the release pipeline only built x64 (plus macOS arm64) binaries and the installer's platform map rejected everything else. Releases now ship `win-arm64` and `linux-arm64` self-contained binaries as well, and the installer maps all six platforms.

## [2.0.0] — 2026-08-18

Goes all-in on the modern MCP spec — structured tool output with published schemas, human-readable tool titles, a new full-text search across all Advanced Settings — and makes installation a one-liner. Major version because the tool output contract changed: data tools now return `structuredContent` and report failures as protocol errors.

### Added

- **`sitefinity_search_settings`** — full-text search across ALL Advanced Settings via the backend `advanced-settings-search` Lucene index (Sitefinity 14.1+). Answers "which section is setting X in?" — the question a section dump can't, because it requires already knowing the section. Each hit carries the setting's caption, breadcrumb path, and owning section, ready to feed into `sitefinity_get_config_section` + `pathFilter`. Implemented fully reflectively on the plugin side (the search API drifts across Sitefinity versions); when the index is disabled or missing, the response sets `IndexAvailable: false` and explains how to enable it instead of erroring. Result values are secret-redacted — the index stores config values.
- **Structured tool output (`UseStructuredContent`).** The 16 data-returning tools (config, content, forms, pages, permissions, taxonomies, templates, where-used, maintenance) now return typed response models: each publishes an `outputSchema` in `tools/list` and returns `structuredContent` alongside the text, so clients can consume results without parsing JSON out of a text block. The 6 human-formatted tools (logs, status, routes, environments, site info) intentionally stay textual.
- **Human-readable `Title` on all 32 tools** (e.g. "Search Advanced Settings") for clients that render tool lists.
- **One-line installation.** The server is now a packaged dotnet tool: `dotnet tool install -g SitefinityCommunity.Mcp` puts `sitefinity-mcp` on PATH — no clone, no build. Releases also attach self-contained single-file binaries (win-x64, linux-x64, osx-x64/arm64) that run without any .NET install.

### Changed

- **Tool errors are now protocol errors.** Data tools throw `McpException` instead of returning `"Error: ..."` strings — clients see a proper `isError` result with the same guidance text (plugin-missing hints, write-gate refusals, validation messages).
- **`ToolOutputLimiter` also bounds `structuredContent`.** An oversized structured payload is serialized into the same JSON-RPC frame as text and can kill the transport just as easily; since a sliced JSON object is useless, an over-limit structured payload is dropped whole with an explanatory note while the (truncated) text carries the data.

### Fixed

- **`sitefinity_list_page_routes` regained its diagnostic detail.** Each page line again carries `[Published]`/`[Draft]` status and a `[URL eval: Mode]` flag when URL evaluation routes sub-paths into the page; both route tools print "No issues detected." when the warnings list is empty.
- `McpSettingsSearchService` registered in `McpServicePlugin` (service registration is an explicit list, not assembly scan).
- Settings search queries are built the way Sitefinity's own query builder does it — an OR `SearchQueryGroup` of per-field `SearchTerm`s (the Lucene service compiles `SearchGroup`, not `Text`), executed under `RunWithElevatedPrivilege` because the secured `advanced-settings-search` publishing point silently returns an empty set for non-backend identities. Result paths are rendered as readable breadcrumbs (`contentViewConfig > ContentViewControls > … > Fields > HighContrast`).

### Upgrading

The plugin changed (new service + registration), so redeploy the plugin source (`install-plugin.ps1` — a NEW file must be registered in the `.csproj`, so use the installer, not a bare copy) and recycle. Restart the MCP server for the client-side changes. **Behavior change for API consumers:** data tools now return results in `structuredContent` (camelCase keys per SDK convention) and report failures as `isError` results instead of `"Error: ..."` text.

## [1.8.0] — 2026-08-18

Fixes a config dump large enough to kill the MCP connection outright, and adds a transport-level backstop so no tool can do that again.

`sitefinity_get_config_section("ContentViewConfig")` died with `MCP error -32000: Connection closed`. The request itself was fine — HTTP 200 in 12 seconds — but the response was **78,805,879 bytes across 375,544 entries**, which then roughly doubled through indented re-serialization before being escaped into a single JSON-RPC stdout frame. The stdio transport does not degrade gracefully at that size; it drops the connection, and the caller gets no indication of which tool was responsible.

The amplification is the interesting part: `ContentViewConfig.config` on disk is only **153 KB**, because the file stores just the deltas. `Config.Get(sectionType)` returns the fully defaults-merged object graph, and the walker emitted every declared property of every nested element whether or not anyone had ever set it — **58% of entries had an empty value**, and 27% of paths were validator scaffolding like `…fields[clientCacheProfileElement].profileChoiceFieldDefinition.validator.usSocialSecurityNumberViolationMessage`.

### Fixed

- **`sitefinity_get_config_section` returns overrides only by default.** The plugin now prunes defaults using the config model's own metadata rather than guessing: `ConfigElement.Source` (`NotSet | Default | FileSystem | Database | Import`) prunes whole default-sourced subtrees, `ConfigProperty.DefaultValue` drops leaves still holding their declared default, and `ConfigProperty.SkipOnExport` drops values Sitefinity itself excludes from an export. This collapses a section back toward what a human actually changed — which is also the view you want when debugging. Only an explicit `Default` prunes; `NotSet` keeps walking, so a Sitefinity version that does not populate `Source` degrades to the per-leaf check instead of hiding real values. The section root is never pruned, so an untouched section still reports its shape.
- **The dump is bounded at the source.** Entries are capped (default 500, max 5000) *during* the walk, so the Sitefinity worker process no longer materializes 375k objects and a 79 MB string — a real memory spike on a live site, and worse on prod than dev.
- **Indentation is dropped on large results**, which was silently doubling the payload for no benefit to the reader.

### Added

- **`pathFilter` on `sitefinity_get_config_section`** — case-insensitive substring match on the entry path, e.g. `contentViewControls[NewsBackend]` or `smtp`. The single biggest lever on a large section.
- **`maxEntries`** — raise or lower the 500-entry cap (max 5000).
- **`includeDefaults`** — opt back into the full defaults-merged dump when you genuinely need unset values.
- **Honest truncation reporting.** The response carries `TotalCount` (true match count, counted past the cap), `ReturnedCount`, `Truncated`, and `DefaultsSkipped`, so "there are 4,210 of these" is distinguishable from "there are 12". Counting continues past the cap; only materialization stops.
- **`ConfigProperty.IsSecret` is now a redaction trigger** — the config model's own first-class secret marker, layered on top of the existing deny-list, path heuristics, `[SecretData]` attribute scan, and connection-string shape detection. Redaction remains unconditional, with no flag to reveal secrets in any environment.
- **A transport-level output limiter (`ToolOutputLimiter`), applied to every tool call.** Results over 250,000 characters are truncated with an explanation of what happened and what to narrow, instead of dropping the connection. Tools are still expected to bound their own output — this only ensures that failing to do so degrades into a readable result rather than a dead session. Override with `SITEFINITY_MCP_MAX_TOOL_OUTPUT_CHARS`.

### Changed

- **MCP SDK upgraded from `0.8.0-preview.1` to `2.2.0` (stable).** The only code change was the filter registration API: `AddCallToolFilter` now hangs off `WithRequestFilters(f => f.AddCallToolFilter(...))`. Verified end-to-end over stdio: initialize negotiates protocol `2025-06-18`, all 31 tools and the resource are discovered, and tool calls run through the API-key filter as before. The write tools already carry the modern `Destructive`/`Idempotent` annotations the current spec surfaces to clients.

### Upgrading

The plugin changed, so redeploy the plugin source into your Sitefinity project (`install-plugin.ps1`) and recycle the app pool. The MCP server picks up the client-side changes on restart. All new parameters are optional — but note the **default behaviour change**: `sitefinity_get_config_section` now returns overrides only. Pass `includeDefaults: true` (ideally with a `pathFilter`) for the previous full-graph view.

## [1.7.0] — 2026-07-07

Makes log search usable against large production log sets. A search over prod logs previously loaded every rolled `*.log` file fully into memory, collected every match with no cap, and serialized the whole result back — routinely blowing past the client's 30-second timeout before returning anything. Search is now bounded, streamed, and newest-first.

### Fixed

- **`sitefinity_search_logs` no longer times out on large logs.** The Sitefinity plugin's search streams each file line-by-line (flat memory regardless of file size), searches files **newest-first**, and **stops after a match cap** (default 200, max 1000) instead of scanning the entire rolled log set. A common pattern like a username now returns the most recent hits quickly instead of exhausting the request.
- **Search gets its own timeout.** `RemoteLogProvider` used a flat 30-second HTTP timeout for every call; search now gets a dedicated 120-second timeout so a legitimately larger scan can finish, while cheap metadata calls keep the tight 30s.

### Added

- **`fileName` parameter on `sitefinity_search_logs`** — restrict a search to a single file (e.g. `Error.log`) instead of every rolled `*.log`. The biggest single speed-up for a targeted production dig.
- **`maxMatches` parameter on `sitefinity_search_logs`** — override the default 200-match cap (up to 1000). The tool now reports when a result set was truncated at the cap, so you know to narrow the pattern or raise the limit.
- Matching request fields (`FileName`, `MaxMatches`) on the plugin's `POST /mcp/logs/search` endpoint.

### Upgrading

The plugin changed, so redeploy the plugin source into your Sitefinity project (`install-plugin.ps1`) and recycle the app for the server-side cap/streaming to take effect. The MCP server picks up the client-side changes (new parameters, longer search timeout) on restart. All new parameters are optional — existing callers are unaffected.

## [1.6.0] — 2026-07-03

Grows the bundled skill catalog to 16 and makes the repo installable through the open Agent Skills standard, so skills can be added and updated with a single `npx` command — no clone required.

### New skills

- **`sitefinity-adminapp-extensions`** — Extend the Sitefinity backend UI with Angular AdminApp extensions: custom field editors that override content-edit fields, NgModule registration, authenticated OData calls, dev server, and bundle deployment.
- **`sitefinity-best-practices`** — The read-this-first foundation for any Sitefinity task: establishes context, states the ground rules, verifies Sitefinity/.NET versions, and routes you to the right deep skill.
- **`sitefinity-servicestack-api`** — Build JSON/REST APIs inside a Sitefinity MVC site: ServiceStack services, `/RestApi` routes, role/identity security, and fixing the `DateTime` "Invalid Date" serialization trap.
- **`sitefinity-odata-services`** — Query native content types and Module Builder dynamic types via Sitefinity's built-in `/api/default` OData service with `$filter`/`$select`/`$expand` and per-type anonymous access — zero code.

### Improved skills

- **`sitefinity-designer-attributes`** / **`sitefinity-widget-expert`** — Corrected the `IndexRenderMode` namespace, clarified `[DataType(customDataType: KnownFieldTypes.CheckBox)]` usage, and flagged the `ExternalDataChoiceAttribute` custom-choice approach as an ASP.NET Core renderer-only extension point (MVC uses `[Choice(ServiceUrl = ...)]`).
- **`sitefinity-page-controls-map`** — Genericized examples so they no longer reference project-specific widget names, and documented that custom grid templates produce their own captions.
- **`sitefinity-toolbox-icons`** — Replaced a project-specific icon assignment table with guidance on tracking your own assignments; genericized the registration example.
- **`sitefinity-vue3-vite8-guide`** — Generalized the LinkModel-to-JSON section for any JS frontend and cross-linked the config-island pattern.

### Skills distribution

The repo is now installable via the open [Agent Skills](https://agentskills.io) standard — `npx skills add github:sitefinitysteve/SitefinityCommunity.Mcp` (all skills, a single skill, project or global) and `npx skills update` for updates, across Claude Code / Codex / Cursor / GitHub Copilot. README Step 8 was rewritten to lead with `npx skills` (with `install-skills.ps1` kept as the cloned-repo/offline fallback) and now documents the full 16-skill catalog.

## [1.5.0] — 2026-06-26

A read-heavy release that opens up Sitefinity configuration, permissions, and cross-site reference data — plus the first (carefully gated) write tools — and removes the last way a raw secret could ever leave the server.

### New tools

- **`sitefinity_list_config_sections`** — List the names of every registered Sitefinity configuration section (systemConfig, securityConfig, multisiteConfig, projectConfig, …) so you can discover a valid name before reading one.
- **`sitefinity_get_config_section`** — Flattened name/value dump of a single config section, with nested elements and dictionaries expressed as dotted/indexed paths. Credential-like values are **always** redacted (see *Security*).
- **`sitefinity_where_used`** — Reverse "where used" lookup across every page **and** template. Pass a Guid (content item / template id), a widget/controller type name (e.g. `ContentBlock`, `MvcControllerProxy`), or — with `kind=property` — any substring to find inside widget property values (a CSS class, URL, or snippet). A widget that lives on a template is expanded into the pages that ride that template (transitively through template inheritance), so the result shows what actually breaks if you change it. Each hit reports the host page/template, the matching widget (with its origin, placeholder, and the matched property/snippet), why it matched, and — for inherited hits — which template it came from. The kind is auto-detected and overridable. The safety check before deleting or refactoring a shared resource.
- **`sitefinity_get_permissions`** — Effective permissions on a page or content item, with the runtime grant/deny bitmasks decoded into real access (deny wins over grant) per principal. Surfaces the headline questions directly: **is it public** (the Everyone role can View), is it viewable by any authenticated user, does it **inherit** permissions and from which parent, and what each role can actually do (View, Modify, Delete, Create, ChangePermissions, …) across each permission set. Pass a page identifier (Guid, URL, or title), or a content item Guid plus its `typeFullName`.
- **`sitefinity_clear_cache`** *(write)* — Clear the `output` (default), `whole`, or a single `page`'s output cache to see widget/template changes without a full recycle.
- **`sitefinity_recycle_app`** *(write)* — Recycle the Sitefinity application so code, config, and binding changes take effect.

### New plugin endpoints

Exposed under `/RestApi/mcp/*` by new ServiceStack services (`McpConfigService`, `McpWhereUsedService`, `McpPermissionsService`, `McpMaintenanceService`):

- `GET /mcp/config` — registered configuration section names
- `GET /mcp/config/{SectionName}` — flattened, redacted section dump
- `GET /mcp/where-used` — reverse reference lookup
- `GET /mcp/permissions` — effective per-role permissions
- `POST /mcp/cache/clear` — clear cache *(write)*
- `POST /mcp/app/recycle` — recycle the application *(write)*

### Write-operation gating (opt-in, double-gated)

The two state-changing tools are the first write tools in the server and are refused unless **both** sides opt in:

1. **MCP server** — the target environment must set `"allowWriteOperations": true` in `sitefinity-mcp.json`. The check is prod-guarded (`EffectiveAllowWriteOperations`): any environment whose name starts with `prod` is always refused regardless of the flag, and the tool refuses before any network call when the flag is off.
2. **Sitefinity plugin** — the **Allow Write Operations** switch in Admin > Advanced > McpSettings must be on (default off). When it's off, `/mcp/cache/clear` and `/mcp/app/recycle` return HTTP 403.

### Removed — exposing secrets is no longer an option, anywhere

- **`allowRawSecrets` is gone entirely.** Secret redaction is now unconditional in every environment, including dev. Logs (`LocalLogProvider.ReadFileAsync` / `SearchAsync`), widget properties, form responses, config dumps, and content all run through the redactor with no flag to opt out. A raw secret in an LLM context is a leak — it can be logged, cached, or absorbed into model training data — so the server never emits one.

### Security

- Credential-like config values — keys, passwords, connection strings, tokens, and `[SecretData]`/encrypted properties — are redacted on the plugin side before transit and can never be revealed through `sitefinity_get_config_section`, in any environment.
- Write tools are double-gated (server flag + admin switch) and permanently disabled for prod-like environment names.

### Other changes

- The "where used" scan was reworked for comprehensive, whole-site coverage of pages and templates; its HTTP timeout was raised from 30s to 120s to accommodate the heavier full-site scan (including a cold app-pool start on a large site).
- Documentation (README, CLAUDE.md, AGENTS.md) updated for the new tools, endpoints, and the write-operation security model.

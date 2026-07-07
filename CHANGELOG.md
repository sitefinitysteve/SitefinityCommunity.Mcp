# Changelog

All notable changes to **SitefinityCommunity.Mcp** are documented here. This project follows [Semantic Versioning](https://semver.org/).

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

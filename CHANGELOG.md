# Changelog

All notable changes to **SitefinityCommunity.Mcp** are documented here. This project follows [Semantic Versioning](https://semver.org/).

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

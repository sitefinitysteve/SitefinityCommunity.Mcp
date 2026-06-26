# Changelog

All notable changes to **SitefinityCommunity.Mcp** are documented here. This project follows [Semantic Versioning](https://semver.org/).

## [1.5.0] — 2026-06-26

A read-heavy release that opens up Sitefinity configuration, permissions, and cross-site reference data, plus the first (carefully gated) write tools — and removes the last way a raw secret could ever leave the server.

### Added

**New MCP tools**

- **`sitefinity_list_config_sections`** — List the names of every registered Sitefinity configuration section (systemConfig, securityConfig, multisiteConfig, projectConfig, …) so you can discover a valid name before reading one.
- **`sitefinity_get_config_section`** — Flattened name/value dump of a single config section, with nested elements and dictionaries expressed as dotted/indexed paths. Credential-like values are **always** redacted (see Security).
- **`sitefinity_where_used`** — Reverse "where used" lookup. Pass a Guid (content item / template id) or a widget/controller type name (e.g. `ContentBlock`, `MvcControllerProxy`) and it scans every page and template for references, returning each host, the widget carrying the reference, and why it matched. Kind is auto-detected and overridable. The safety check before deleting or refactoring a shared resource.
- **`sitefinity_get_permissions`** — Effective per-role permissions on a page or content item: granted/denied actions (View, Modify, Delete, Create, ChangePermissions, …) across each permission set, plus whether the object inherits from its parent. Answers "why can't this role see or edit this?"
- **`sitefinity_clear_cache`** *(write)* — Clear the `output` (default), `whole`, or a single `page`'s output cache to see widget/template changes without a full recycle.
- **`sitefinity_recycle_app`** *(write)* — Recycle the Sitefinity application so code, config, and binding changes take effect.

**New plugin endpoints** (`/RestApi/mcp/*`): `GET /mcp/config`, `GET /mcp/config/{SectionName}`, `GET /mcp/where-used`, `GET /mcp/permissions`, `POST /mcp/cache/clear`, `POST /mcp/app/recycle`. New plugin services: `McpConfigService`, `McpWhereUsedService`, `McpPermissionsService`, `McpMaintenanceService`.

**Write-operation gating** — A new opt-in path for the first state-changing tools, refused unless **both** sides agree:

- `"allowWriteOperations": true` per environment in `sitefinity-mcp.json` (`EffectiveAllowWriteOperations`, prod-guarded — never honored for names starting with `prod`).
- **Allow Write Operations** switch in Sitefinity Admin > Advanced > McpSettings (default off; the plugin returns HTTP 403 when off).

### Changed

- The "where used" scan was reworked for comprehensive, whole-site coverage of pages and templates.
- README, CLAUDE.md, and AGENTS.md updated for the new tools, endpoints, and the write-operation security model.

### Removed

- **`allowRawSecrets` removed entirely — exposing secrets is no longer an option, anywhere.** Secret redaction is now unconditional in every environment, including dev. Logs (`LocalLogProvider.ReadFileAsync` / `SearchAsync`), widget properties, form responses, config dumps, and content all run through the redactor with no flag to opt out. A raw secret in an LLM context is a leak (it can be logged, cached, or absorbed into model training data), so the server never emits one.

### Security

- Credential-like config values — keys, passwords, connection strings, tokens, and `[SecretData]`/encrypted properties — are redacted on the plugin side before transit and can never be revealed through `sitefinity_get_config_section`, in any environment.
- Write tools are double-gated (server flag + admin switch) and permanently disabled for prod-like environment names.

## Previous releases

See the [GitHub releases page](https://github.com/sitefinitysteve/SitefinityCommunity.Mcp/releases) for 1.4.1 and earlier.

[1.5.0]: https://github.com/sitefinitysteve/SitefinityCommunity.Mcp/compare/v1.4.1...v1.5.0

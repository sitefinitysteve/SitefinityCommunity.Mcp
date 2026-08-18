# sitefinity-comm-mcp

**MCP server for Sitefinity CMS** — give Claude Code, VS Code, Cursor, or any [Model Context Protocol](https://modelcontextprotocol.io) client direct, read-safe access to your Sitefinity site: logs, configuration, pages, widgets, forms, content types, permissions, and status.

A community project — not affiliated with or endorsed by Progress Software.

## Installation

```bash
npm install -g sitefinity-comm-mcp
```

No .NET required. The package downloads a self-contained binary for your platform (Windows x64, Linux x64, macOS x64/arm64).

## Quick start

**1. Install the companion plugin** into your Sitefinity web app (it exposes the REST endpoints the server talks to, as source files that compile against your own Sitefinity assemblies):

```bash
cd C:\Path\To\SitefinityWebApp
sitefinity-comm-mcp install-plugin
```

(Or from anywhere: `sitefinity-comm-mcp install-plugin --target "C:\Path\To\SitefinityWebApp"`. The installer verifies a `web.config` is present before touching anything. After a CLI update, `sitefinity-comm-mcp update-plugin` refreshes an existing install — same operation, friendlier name.)

**2. Generate a shared API key** and set it in both places (your config file below, and Sitefinity Admin → Settings → Advanced → McpSettings):

```bash
sitefinity-comm-mcp generate-key
```

**3. Create `sitefinity-mcp.json`** describing your environments:

```json
{
    "defaultEnvironment": "dev",
    "environments": {
        "dev": {
            "url": "https://dev.example.com",
            "sitefinityApiKey": "your-generated-key"
        }
    }
}
```

**4. Register the server** with your MCP client. Claude Code one-liner (run in your project):

```bash
claude mcp add sitefinity-comm-mcp --env SITEFINITY_MCP_CONFIG="C:\Path\To\sitefinity-mcp.json" -- sitefinity-comm-mcp
```

Or by hand in `.mcp.json`:

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

## What you get

32 tools, including:

| Area | Tools |
|---|---|
| **Logs** | Read and regex-search error/trace logs — bounded and streamed, safe on large production log sets |
| **Configuration** | Dump any config section (overrides-only by default), full-text search across all Advanced Settings |
| **Pages & widgets** | Page details, widget trees in render order, per-widget properties |
| **Content** | Module Builder types and fields, live content queries, templates, taxonomies |
| **Forms** | Form definitions and paged, searchable submissions |
| **Diagnostics** | Site info, modules, routes, effective permissions, where-used reverse lookup, health status |

Structured output (MCP `structuredContent` + published schemas) on all data tools. Multi-environment: every tool takes an optional `environment` parameter (dev / staging / prod).

## Safety by design

- **Read-only by default.** The only write tools (cache clear, app recycle) require explicit opt-in on *both* the server config and the Sitefinity admin, and are always refused for prod-named environments.
- **Secrets never reach the model.** Credential-shaped values — keys, passwords, connection strings, tokens, encrypted `[SecretData]` properties — are unconditionally redacted on the Sitefinity side before transit, in every environment, with no override flag.
- **Authenticated.** Every plugin endpoint requires a shared API key, validated proactively.

## Documentation

Full setup guide, tool reference, plugin details, and changelog:
**[github.com/sitefinitysteve/SitefinityCommunity.Mcp](https://github.com/sitefinitysteve/SitefinityCommunity.Mcp)**

## Requirements

- Sitefinity CMS (classic .NET Framework 4.8 sites; plugin compiles against your project's own assemblies)
- Node.js ≥ 18 (for this installer only — the server itself is a native binary)

## License

MIT

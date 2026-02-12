# SitefinityCommunity.Mcp

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server for **Sitefinity CMS**. Gives Claude Code (and any MCP client) direct access to Sitefinity logs, diagnostics, and CMS status.

Inspired by [Laravel Boost](https://github.com/nicepkg/laravel-boost) — designed as an extensible framework where adding new tools requires only creating a new class file.

## Why this exists

Sitefinity developers deserve the same AI-assisted workflow that every other framework already has. Laravel has Boost, Rails has its own MCP servers, Next.js devs have first-class tooling — but Sitefinity? Nothing.

The official Sitefinity team moves at their own pace, and the developer experience tooling we need keeps not showing up. So instead of waiting, this project builds it. Community-driven, open source, and designed by someone who ships Sitefinity code every day and knows what's actually painful.

If you've ever wished Claude could just *read your Sitefinity logs*, or know what modules and content types you have installed, or check if your site is even running — that's exactly what this does.

## Features

- **Log Tools** — Read error/trace logs, search across all log files with regex, get the last error
- **Site Info** — Sitefinity version, .NET version, project name, configured languages, multisite info
- **Module Inspector** — List all installed modules with type, status, and startup type
- **Content Model** — Browse Module Builder dynamic types and their field definitions
- **Status Check** — Verify if Sitefinity is bootstrapped and ready
- **Multi-Environment** — Switch between dev/staging/prod environments on the fly
- **Dual-Mode Logs** — Local filesystem access for dev, HTTP via companion plugin for remote servers
- **Auto-Discovery** — New tools are picked up automatically via `[McpServerToolType]` attribute
- **API Key Validation** — Proactive key matching between MCP server and Sitefinity plugin

## Quick Start

### 1. Create a config file

Create `sitefinity-mcp.json` (keep this gitignored — it contains keys):

```json
{
    "defaultEnvironment": "dev",
    "environments": {
        "dev": {
            "url": "https://dev.example.com",
            "logsPath": "C:\\Path\\To\\Sitefinity\\App_Data\\Sitefinity\\Logs",
            "sitefinityApiKey": "your-dev-api-key"
        },
        "staging": {
            "url": "https://staging.example.com",
            "sitefinityApiKey": "your-staging-api-key"
        }
    }
}
```

- `logsPath` set = local log reading (same machine as Sitefinity)
- `logsPath` empty = remote log reading via companion plugin HTTP endpoints
- `sitefinityApiKey` is **required** for every environment

### 2. Setting Up API Keys

API keys must match on both sides. Use the built-in generator to create a cryptographically secure key:

```bash
dotnet run --project src/SitefinityCommunity.Mcp -- generate-key
```

This prints a new 256-bit Base64 key and setup instructions. Then:

1. **Set it in `sitefinity-mcp.json`** — as `sitefinityApiKey` for each environment
2. **Set the same key in Sitefinity** — Admin > Settings > Advanced > McpSettings > API Key
3. **Check "Enabled"** in McpSettings (it's `false` by default — you must opt in)

The key is encrypted at rest in Sitefinity's config via `[SecretData]`.

The MCP server validates keys by calling `GET /RestApi/mcp/ping` with the configured key. If the keys don't match, all tool calls return a clear error message instead of cryptic 401s.

**Key validation behavior:**
- **Keys match** — tools work normally
- **Keys don't match** — tools return: "API key mismatch..." error
- **Sitefinity unreachable** — tools proceed with a warning (so you can still read local logs when debugging a down server)
- **Blank keys** — rejected on both sides (MCP server won't start; Sitefinity won't register endpoints)

### Security Model

**Enabled flag** — The `Enabled` checkbox in Sitefinity Admin > Advanced > McpSettings acts as a kill switch with two enforcement layers:

1. **Startup gate** — `McpInit.Register()` checks `Enabled` and `ApiKey` before registering the ServiceStack plugin. If either is disabled/blank, the `/RestApi/mcp/*` routes don't exist at all (404, no attack surface). Requires app pool recycle to toggle.
2. **Runtime gate** — The `[McpApiKey]` request filter attribute checks `Enabled` on every request. If someone disables MCP in admin after startup, requests are immediately blocked without an app pool recycle.

**Encryption at rest** — The API key in Sitefinity's config is marked with `[SecretData]`, so it's stored encrypted in `McpConfig.config`. Sitefinity decrypts it transparently when the property is read in code.

**Blank key protection** — Blank, empty, and whitespace-only keys are rejected at every checkpoint:
- MCP server config validation (`IsNullOrWhiteSpace`) — server won't start
- Sitefinity startup (`McpInit.Register`) — plugin won't register endpoints
- Sitefinity request filter (`McpApiKeyAttribute`) — requests blocked at runtime

### 3. Configure Claude Code

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

### 4. Use the tools

Once configured, these tools are available in Claude Code:

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
| `sitefinity_list_environments` | Show configured environments |
| `sitefinity_set_default_environment` | Switch active environment |

## Architecture

**Two-component design:**

1. **MCP Server** (this project) — .NET console app using the official [ModelContextProtocol SDK](https://www.nuget.org/packages/ModelContextProtocol). Communicates with Claude Code via stdio.
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

## Companion Sitefinity Plugin

For remote servers (staging, production), install the companion plugin into your Sitefinity web app:

```powershell
.\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp"
```

This copies the plugin source files into `Code\Mcp\SitefinityCommunity\` in your project. Then add one line to `Global.asax.cs` in your `Bootstrapper_Initialized` handler:

```csharp
SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();
```

Configure in **Sitefinity Admin > Settings > Advanced > McpSettings**:
- **API Key** — must match `sitefinityApiKey` in your `sitefinity-mcp.json`
- **Enabled** — `false` by default. Must be explicitly enabled. Uncheck to disable all MCP endpoints (requires app pool recycle; runtime requests are also blocked immediately)

Source files compile against your existing Sitefinity assemblies — no DLL binding issues across Sitefinity versions. See the [plugin README](src/SitefinityCommunity.Mcp.SitefinityPlugin/README.md) for the full explanation.

## Author

Built by **Steve McNiven-Scott** ([@SitefinitySteve](https://www.sitefinitysteve.com) | [GitHub](https://github.com/sitefinitysteve)) — Sitefinity MVP and long-time community contributor. Building developer tools for the Sitefinity community.

## License

MIT

# SitefinityCommunity.Mcp

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server for **Sitefinity CMS**. Gives Claude Code (and any MCP client) direct access to Sitefinity logs, diagnostics, and CMS status.

Inspired by [Laravel Boost](https://github.com/nicepkg/laravel-boost) — designed as an extensible framework where adding new tools requires only creating a new class file.

## Features

- **Log Tools** — Read error/trace logs, search across all log files with regex, get the last error
- **Status Check** — Verify if Sitefinity is bootstrapped and ready
- **Multi-Environment** — Switch between dev/staging/prod environments on the fly
- **Dual-Mode Logs** — Local filesystem access for dev, HTTP via companion plugin for remote servers
- **Auto-Discovery** — New tools are picked up automatically via `[McpServerToolType]` attribute

## Quick Start

### 1. Create a config file

Create `sitefinity-mcp.json` (keep this gitignored — it contains keys):

```json
{
    "apiKey": "your-mcp-api-key",
    "defaultEnvironment": "dev",
    "environments": {
        "dev": {
            "url": "https://dev.example.com",
            "logsPath": "C:\\Path\\To\\Sitefinity\\App_Data\\Sitefinity\\Logs"
        },
        "staging": {
            "url": "https://staging.example.com",
            "sitefinityApiKey": "staging-plugin-key"
        }
    }
}
```

- `logsPath` set = local log reading (same machine as Sitefinity)
- `logsPath` empty = remote log reading via companion plugin HTTP endpoints

### 2. Configure Claude Code

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

### 3. Use the tools

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
| `sitefinity_list_environments` | Show configured environments |
| `sitefinity_set_default_environment` | Switch active environment |

## Architecture

**Two-component design:**

1. **MCP Server** (this project) — .NET console app using the official [ModelContextProtocol SDK](https://www.nuget.org/packages/ModelContextProtocol). Communicates with Claude Code via stdio.
2. **Sitefinity Plugin** (source files) — `.cs` files you drop into any Sitefinity web app. Registers ServiceStack endpoints at `/RestApi/mcp/*` for remote log access. Compiles against your existing assemblies — no DLL conflicts.

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

Set your API key in **Sitefinity Admin → Settings → Advanced → McpSettings** and you're done.

Source files compile against your existing Sitefinity assemblies — no DLL binding issues across Sitefinity versions. See the [plugin README](src/SitefinityCommunity.Mcp.SitefinityPlugin/README.md) for the full explanation.

## License

MIT

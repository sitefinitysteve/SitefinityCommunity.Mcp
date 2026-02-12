# SitefinityCommunity.Mcp — Claude Code Instructions

## What This Project Is

An MCP (Model Context Protocol) server that gives Claude Code direct access to Sitefinity CMS logs, diagnostics, and status. It runs as a .NET console app communicating via stdio, and exposes tools that Claude Code can call.

**Two components:**
1. **MCP Server** (`src/SitefinityCommunity.Mcp/`) — .NET 10 console app using the official ModelContextProtocol SDK
2. **Sitefinity Plugin** (`src/SitefinityCommunity.Mcp.SitefinityPlugin/`) — `.cs` source files dropped into a Sitefinity web app that expose REST endpoints at `/RestApi/mcp/*`

## Project Structure

```
SitefinityCommunity.Mcp/
├── CLAUDE.md                          ← You are here
├── README.md                          ← User-facing docs
├── install-plugin.ps1                 ← Copies plugin files to a Sitefinity project
├── SitefinityCommunity.Mcp.slnx       ← Solution file
└── src/
    ├── SitefinityCommunity.Mcp/       ← THE MCP SERVER
    │   ├── Program.cs                 ← Entry point, DI, MCP server config, tool filter
    │   ├── Configuration/
    │   │   └── SitefinityMcpConfig.cs ← Config model (loads from sitefinity-mcp.json)
    │   ├── Models/
    │   │   ├── LogEntry.cs            ← Parsed log entry
    │   │   ├── LogFileInfo.cs         ← Log file metadata
    │   │   ├── LogSearchResult.cs     ← Search match result
    │   │   └── SitefinityHealthResponse.cs
    │   ├── Services/
    │   │   ├── IEnvironmentResolver.cs    ← Resolves named environments
    │   │   ├── EnvironmentResolver.cs     ← Tracks active default environment
    │   │   ├── ILogProvider.cs            ← Abstract log access interface
    │   │   ├── ILogProviderFactory.cs     ← Creates local/remote providers
    │   │   ├── LogProviderFactory.cs      ← Factory implementation
    │   │   ├── LocalLogProvider.cs        ← Reads logs from filesystem
    │   │   ├── RemoteLogProvider.cs       ← Reads logs via HTTP plugin endpoints
    │   │   ├── LogParsingService.cs       ← Parses Sitefinity's 40-dash-separated log format
    │   │   ├── ISitefinityStatusService.cs
    │   │   ├── SitefinityStatusService.cs ← Polls /RestApi/systemstatus
    │   │   └── ApiKeyValidationService.cs ← Validates API keys via /RestApi/mcp/ping
    │   └── Tools/                     ← MCP TOOLS (auto-discovered)
    │       ├── LogTools.cs            ← read_error_log, read_trace_log, list_log_files, etc.
    │       ├── EnvironmentTools.cs    ← list_environments, set_default_environment
    │       └── SitefinityStatusTools.cs ← check_status
    │
    └── SitefinityCommunity.Mcp.SitefinityPlugin/  ← SITEFINITY PLUGIN (source files)
        ├── McpInit.cs                 ← Registration (checks Enabled + ApiKey before registering)
        ├── McpConfig.cs               ← Sitefinity config section (Admin > Advanced > McpSettings)
        ├── McpApiKeyAttribute.cs      ← Request filter validating X-MCP-API-Key header
        ├── McpServicePlugin.cs        ← ServiceStack plugin registration
        ├── McpLogRequest.cs           ← Request/response DTOs
        ├── McpLogService.cs           ← ServiceStack service handlers
        └── README.md                  ← Plugin installation guide
```

## How to Build and Run

```bash
# Build
dotnet build

# Run (requires SITEFINITY_MCP_CONFIG env var)
SITEFINITY_MCP_CONFIG=/path/to/sitefinity-mcp.json dotnet run --project src/SitefinityCommunity.Mcp
```

The server communicates via stdio (stdin/stdout) using the MCP protocol. All logging goes to stderr.

## Configuration File (sitefinity-mcp.json)

```json
{
    "defaultEnvironment": "dev",
    "environments": {
        "dev": {
            "url": "https://dev.example.com",
            "logsPath": "C:\\Path\\To\\App_Data\\Sitefinity\\Logs",
            "sitefinityApiKey": "must-match-sitefinity-config"
        },
        "staging": {
            "url": "https://staging.example.com",
            "sitefinityApiKey": "must-match-sitefinity-config"
        }
    }
}
```

- **`sitefinityApiKey`** — Required for every environment. Must match the key in Sitefinity Admin > Settings > Advanced > McpSettings
- **`logsPath`** — When set, reads logs directly from filesystem (local mode). When omitted, uses HTTP via the plugin (remote mode)
- **`url`** — Required. The Sitefinity site URL

## How to Add a New Tool

Tools are auto-discovered via `[McpServerToolType]` — no changes to `Program.cs` needed.

### Step 1: Create a tool class in `Tools/`

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

[McpServerToolType]
public sealed class MyNewTools
{
    private readonly IEnvironmentResolver _resolver;

    // Constructor injection — any registered service can be injected
    public MyNewTools(IEnvironmentResolver resolver)
    {
        this._resolver = resolver;
    }

    [McpServerTool(Name = "sitefinity_my_tool", ReadOnly = true)]
    [Description("What this tool does — Claude reads this to decide when to use it.")]
    public async Task<string> MyTool(
        [Description("Target environment name (uses default if omitted)")] string? environment = null,
        CancellationToken ct = default)
    {
        var (envName, config) = this._resolver.Resolve(environment);
        // ... your logic ...
        return "result text";
    }
}
```

### Conventions

- Tool names: `sitefinity_` prefix, snake_case (e.g. `sitefinity_read_error_log`)
- Return type: `string` or `Task<string>` — plain text, not JSON
- Include `string? environment = null` parameter on tools that target a specific environment
- Include `CancellationToken ct = default` for async operations
- Use `[Description]` on the class method AND on parameters — Claude uses these descriptions
- Set `ReadOnly = true` for tools that don't modify state
- Wrap external calls in try/catch and return error messages as strings (don't let exceptions propagate)

### Step 2: If your tool needs a new service

1. Create interface + implementation in `Services/`
2. Register in `Program.cs`: `builder.Services.AddSingleton<IMyService, MyService>();`

### Step 3: If your tool needs a new REST endpoint in Sitefinity

1. Add a request DTO to `SitefinityPlugin/McpLogRequest.cs`
2. Add a handler to `SitefinityPlugin/McpLogService.cs`
3. Copy updated files to the installed location in the Sitefinity project

## Key Architecture Decisions

### Dual-Mode Log Access

Each environment can use either:
- **Local mode** (`logsPath` set) — `LocalLogProvider` reads `.log` files directly from disk. Used for dev where the MCP server runs on the same machine as Sitefinity.
- **Remote mode** (`logsPath` not set) — `RemoteLogProvider` makes HTTP calls to `/RestApi/mcp/*` endpoints exposed by the Sitefinity plugin. Used for staging/prod.

The `LogProviderFactory` picks the right one based on config.

### Security: API Keys and Enabled Flag

**API Key Validation:**
The `ApiKeyValidationService` proactively validates that the MCP server's key matches Sitefinity's key by calling `GET /RestApi/mcp/ping`. Results are cached for 5 minutes per environment. The `CallToolFilter` in `Program.cs` runs this check before every tool call:
- **Valid** — proceed normally
- **InvalidKey** — return clear error message (no tools execute)
- **Unreachable** — warn but allow (so local log reading works when Sitefinity is down)

**Enabled Flag (Sitefinity Admin > Advanced > McpSettings):**
Two-layer enforcement ensures MCP endpoints can be fully disabled:

1. **Startup gate** (`McpInit.Register`) — If `Enabled` is `false` OR `ApiKey` is blank, the ServiceStack plugin is never registered. Routes don't exist (404). Config section is always registered so the admin UI stays accessible. Requires app pool recycle to take effect.
2. **Runtime gate** (`[McpApiKey]` attribute) — Checks `Enabled` on every request. Blocks immediately if disabled in admin, even without a recycle.

**Blank key rejection at every layer:**
- MCP server startup: `SitefinityMcpConfig.Validate()` uses `IsNullOrWhiteSpace` — rejects `""`, `null`, `"   "`
- Sitefinity startup: `McpInit.Register()` checks `IsNullOrWhiteSpace(config.ApiKey)` — skips plugin registration
- Sitefinity runtime: `McpApiKeyAttribute` checks `IsNullOrWhiteSpace` on both the config key and the request header

**Important for new plugin services:** Always apply `[McpApiKey]` at the class level on any new ServiceStack service. This ensures the `Enabled` and key checks are enforced automatically on all endpoints in that service.

### Plugin Source Files (Not a NuGet Package)

Sitefinity bundles pinned versions of ServiceStack, Newtonsoft.Json, etc. that change across versions. A precompiled DLL would cause binding redirect hell. Source files compile against whatever assemblies the host Sitefinity project references — zero conflicts.

### Log Parsing

Sitefinity logs use a format where entries are separated by 40-dash lines (`----------------------------------------`). The `LogParsingService` uses source-generated regexes to extract structured fields (timestamp, severity, type, URL, stack trace, etc.) from the raw text blocks.

## Available Services (for DI)

| Interface | Implementation | Purpose |
|-----------|---------------|---------|
| `IEnvironmentResolver` | `EnvironmentResolver` | Resolve environment by name, track default |
| `ILogProviderFactory` | `LogProviderFactory` | Create local/remote log provider per environment |
| `ILogProvider` | `LocalLogProvider` / `RemoteLogProvider` | List, read, search log files |
| `LogParsingService` | (concrete) | Parse Sitefinity log format into structured entries |
| `ISitefinityStatusService` | `SitefinityStatusService` | Check if Sitefinity is bootstrapped |
| `IApiKeyValidationService` | `ApiKeyValidationService` | Validate API keys via ping endpoint |
| `IHttpClientFactory` | (framework) | Create HTTP clients for remote calls |
| `SitefinityMcpConfig` | (concrete) | Loaded config singleton |

## Plugin Endpoint Reference

All endpoints require `X-MCP-API-Key` header. Protected by `[McpApiKey]` attribute.

| Route | Method | Description |
|-------|--------|-------------|
| `/mcp/ping` | GET | Lightweight key validation — returns `{ status: "ok" }` |
| `/mcp/logs` | GET | List all log files with metadata |
| `/mcp/logs/{FileName}` | GET | Read a log file (optional `MaxLines` query param) |
| `/mcp/logs/search` | POST | Search all logs with regex pattern |
| `/mcp/logs/last-error` | GET | Most recent error log entry |

## Coding Conventions

- **`this.` prefix** — Always use `this.` when accessing instance members
- **Properties at bottom** — Class organization: constructor, methods, properties
- **File-scoped namespaces** — Use `namespace X;` not `namespace X { }`
- **Primary constructors** — Prefer primary constructors for tool classes with simple DI
- **Nullable enabled** — Project has `<Nullable>enable</Nullable>`
- **No manual JSON serialization** — Use `System.Text.Json` with source generators where applicable
- **Target framework** — .NET 10 (`net10.0`)
- **MCP SDK** — `ModelContextProtocol` v0.8.0-preview.1

## Testing Locally

1. Set a matching API key in `sitefinity-mcp.json` and in Sitefinity Admin > Advanced > McpSettings
2. Run the MCP server with the config file path
3. Verify with Claude Code — tools like `sitefinity_list_environments` don't need Sitefinity running
4. Log tools need either a local `logsPath` or a running Sitefinity with the plugin installed

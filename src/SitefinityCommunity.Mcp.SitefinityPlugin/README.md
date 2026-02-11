# SitefinityCommunity.Mcp — Sitefinity Plugin

Source files that add MCP REST endpoints to any Sitefinity CMS installation. These compile directly into your Sitefinity web app — no separate DLL needed.

## Installation

### 1. Copy files into your Sitefinity project

Copy all `.cs` files from this folder into your Sitefinity web app project (e.g., `SitefinityWebApp/`).

### 2. Register in Global.asax

In your `Global.asax.cs`, inside the `Bootstrapper_Initialized` handler:

```csharp
// Register MCP config section (admin UI settings)
Config.RegisterSection<SitefinityCommunity.Mcp.SitefinityPlugin.McpConfig>();

// Register MCP ServiceStack endpoints
SystemManager.RegisterServiceStackPlugin(new SitefinityCommunity.Mcp.SitefinityPlugin.McpServicePlugin());
```

### 3. Configure API key in Sitefinity admin

1. Go to **Administration → Settings → Advanced → McpSettings**
2. Set the **API Key** (must match `sitefinityApiKey` in your `sitefinity-mcp.json`)
3. **Enabled** is `true` by default — set to `false` to disable endpoints

### 4. Verify

```
GET https://your-site.com/RestApi/mcp/logs
Header: X-MCP-API-Key: your-api-key
```

Should return a JSON array of log files.

## Endpoints

| Route | Method | Description |
|-------|--------|-------------|
| `/mcp/logs` | GET | List all log files |
| `/mcp/logs/{FileName}` | GET | Read a log file |
| `/mcp/logs/search` | POST | Search logs with regex |
| `/mcp/logs/last-error` | GET | Most recent error entry |

All endpoints require `X-MCP-API-Key` header.

## Why source files instead of a NuGet DLL?

Sitefinity bundles specific versions of ServiceStack and other assemblies that vary across versions (12.x, 13.x, 14.x, 15.x). A precompiled DLL would create assembly binding conflicts. Source files compile against whatever versions your Sitefinity installation already uses.

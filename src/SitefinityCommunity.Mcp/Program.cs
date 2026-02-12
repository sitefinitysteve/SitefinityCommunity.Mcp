using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Services;

// Load and validate config before building the host
var options = SitefinityMcpConfig.Load();

var builder = Host.CreateApplicationBuilder(args);

// All logging goes to stderr — stdout is reserved for MCP protocol
builder.Logging.AddConsole(consoleLogOptions =>
{
    consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register configuration as singleton
builder.Services.AddSingleton(options);

// Register services
builder.Services.AddSingleton<IEnvironmentResolver>(sp =>
    new EnvironmentResolver(sp.GetRequiredService<SitefinityMcpConfig>()));
builder.Services.AddSingleton<ILogProviderFactory, LogProviderFactory>();
builder.Services.AddSingleton<LogParsingService>();
builder.Services.AddSingleton<ISitefinityStatusService, SitefinityStatusService>();
builder.Services.AddSingleton<IApiKeyValidationService, ApiKeyValidationService>();

// HTTP client factory for remote providers and status checks
builder.Services.AddHttpClient();

// MCP Server with stdio transport and auto-discovered tools
builder.Services
    .AddMcpServer(server =>
    {
        server.ServerInfo = new Implementation
        {
            Name = "sitefinity-mcp",
            Version = "1.0.0"
        };
        server.ServerInstructions =
            "Sitefinity CMS MCP server. Provides access to Sitefinity logs, diagnostics, and status. " +
            "All tools accept an optional 'environment' parameter to target a specific environment (dev, staging, prod). " +
            "If omitted, the current default environment is used. Use sitefinity_list_environments to see available environments " +
            "and sitefinity_set_default_environment to switch.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    // API key validation filter — runs before every tool call
    .AddCallToolFilter(next => async (context, cancellationToken) =>
    {
        var validator = context.Services!.GetRequiredService<IApiKeyValidationService>();

        // Extract the target environment from tool arguments (most tools accept an optional 'environment' param)
        // Arguments are deserialized as JsonElement from the MCP protocol JSON
        string? targetEnvironment = null;
        if (context.Params.Arguments?.TryGetValue("environment", out var envObj) == true
            && envObj is JsonElement envEl
            && envEl.ValueKind == JsonValueKind.String)
        {
            targetEnvironment = envEl.GetString();
        }

        var result = await validator.ValidateAsync(targetEnvironment, cancellationToken);

        if (result == ApiKeyValidationResult.InvalidKey)
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock
                {
                    Text = "API key mismatch. The sitefinityApiKey in your sitefinity-mcp.json " +
                           "does not match the API Key configured in Sitefinity. " +
                           "Update the key in Sitefinity Admin > Settings > Advanced > McpSettings " +
                           "to match your config file, or vice versa."
                }],
                IsError = true
            };
        }

        // Unreachable — warn but allow (Sitefinity may be down, and you may be reading local logs to debug why)
        if (result == ApiKeyValidationResult.Unreachable)
        {
            var innerResult = await next(context, cancellationToken);

            // Prepend a warning to the tool's output
            var warning = new TextContentBlock
            {
                Text = "[Warning] Could not validate API key — Sitefinity is unreachable. " +
                       "Key validation will be retried on the next call.\n\n"
            };
            var combined = new List<ContentBlock> { warning };
            combined.AddRange(innerResult.Content);
            innerResult.Content = combined;
            return innerResult;
        }

        return await next(context, cancellationToken);
    });

await builder.Build().RunAsync();

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
        var opts = context.Services!.GetRequiredService<SitefinityMcpConfig>();

        // The API key is validated at startup via config load, but we also check
        // it's present per-call as a defense-in-depth measure
        if (string.IsNullOrEmpty(opts.ApiKey))
        {
            return new CallToolResult
            {
                Content = [new TextContentBlock { Text = "MCP server error: API key not configured." }],
                IsError = true
            };
        }

        return await next(context, cancellationToken);
    });

await builder.Build().RunAsync();

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Extensions;
using SitefinityCommunity.Mcp.Services;

// CLI: install-plugin command — write the embedded Sitefinity plugin sources into a target project.
// update-plugin is an alias: the operation is identical (idempotent copy + csproj refresh), the
// alias just reads better when refreshing an existing install after a CLI update.
if (args.Length > 0 && (string.Equals(args[0], "install-plugin", StringComparison.OrdinalIgnoreCase)
    || string.Equals(args[0], "update-plugin", StringComparison.OrdinalIgnoreCase)))
{
    return SitefinityCommunity.Mcp.Cli.PluginInstaller.Run(args);
}

// CLI: generate-key command — print a new API key and setup instructions, then exit
if (args.Length > 0 && string.Equals(args[0], "generate-key", StringComparison.OrdinalIgnoreCase))
{
    var keyBytes = RandomNumberGenerator.GetBytes(32);
    var apiKey = Convert.ToBase64String(keyBytes);

    Console.WriteLine();
    Console.WriteLine("  Generated API Key:");
    Console.WriteLine($"  {apiKey}");
    Console.WriteLine();
    Console.WriteLine("  Setup Instructions:");
    Console.WriteLine();
    Console.WriteLine("  1. MCP Server config (sitefinity-mcp.json):");
    Console.WriteLine("     Add the key to each environment's settings:");
    Console.WriteLine();
    Console.WriteLine("     \"environments\": {");
    Console.WriteLine("       \"dev\": {");
    Console.WriteLine($"         \"sitefinityApiKey\": \"{apiKey}\",");
    Console.WriteLine("         ...");
    Console.WriteLine("       }");
    Console.WriteLine("     }");
    Console.WriteLine();
    Console.WriteLine("  2. Sitefinity Admin:");
    Console.WriteLine("     Settings > Advanced > McpSettings > API Key");
    Console.WriteLine("     Paste the same key and save.");
    Console.WriteLine();

    return 0;
}

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
builder.Services.AddSingleton<ISitefinityMetadataService, SitefinityMetadataService>();

// HTTP client factory for remote providers and status checks
builder.Services.AddHttpClient();

// MCP Server with stdio transport and auto-discovered tools
builder.Services
    .AddMcpServer(server =>
    {
        server.ServerInfo = new Implementation
        {
            Name = "sitefinity-comm-mcp",
            Version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion ?? "0.0.0"
        };
        server.ServerInstructions =
            "Sitefinity CMS MCP server. Provides access to Sitefinity logs, diagnostics, status, and instance metadata. " +
            "All tools accept an optional 'environment' parameter to target a specific environment (dev, staging, prod). " +
            "If omitted, the current default environment is used. Use sitefinity_list_environments to see available environments " +
            "and sitefinity_set_default_environment to switch. " +
            "Use sitefinity_get_site_info for version/project info, sitefinity_list_modules for installed modules, " +
            "sitefinity_list_dynamic_types for Module Builder types, and sitefinity_get_type_fields for field definitions.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithResourcesFromAssembly()
    // API key validation filter — runs before every tool call
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
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

        // Wraps next() to catch SitefinityBootstrappingException — thrown when a service
        // receives HTML instead of JSON because Sitefinity restarted after API key was validated.
        // Invalidates the stale "Valid" cache entry and returns a friendly message.
        async Task<CallToolResult> InvokeWithBootstrapGuard()
        {
            try
            {
                // Backstop: an oversized result does not fail gracefully on the stdio transport — it drops
                // the connection ("-32000: Connection closed") with no indication of which tool caused it.
                // Tools bound their own output; this catches any that don't.
                return ToolOutputLimiter.Apply(await next(context, cancellationToken));
            }
            catch (SitefinityBootstrappingException ex)
            {
                validator.InvalidateCache(targetEnvironment);
                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = ex.Message }],
                    IsError = true
                };
            }
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

        if (result == ApiKeyValidationResult.Unreachable)
        {
            // Tools that don't need a running Sitefinity — skip waiting
            var toolName = context.Params.Name;
            var localOnlyTools = new HashSet<string>(StringComparer.Ordinal)
            {
                "sitefinity_list_environments",
                "sitefinity_set_default_environment",
                "sitefinity_check_status" // has its own polling logic
            };

            if (localOnlyTools.Contains(toolName))
            {
                return await InvokeWithBootstrapGuard();
            }

            // Wait for Sitefinity to become ready before proceeding
            var statusService = context.Services!.GetRequiredService<ISitefinityStatusService>();
            var health = await statusService.WaitForReadyAsync(targetEnvironment, maxWaitSeconds: 90, ct: cancellationToken);

            if (health.IsReady)
            {
                // Re-validate the API key now that Sitefinity is up
                result = await validator.ValidateAsync(targetEnvironment, cancellationToken);

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

                // Ready + Valid — proceed normally, no warning needed
                return await InvokeWithBootstrapGuard();
            }

            // Still unreachable after waiting — warn but allow (so local log tools still work)
            var innerResult = await InvokeWithBootstrapGuard();
            var warning = new TextContentBlock
            {
                Text = "[Warning] Sitefinity did not become ready after waiting 90 seconds. " +
                       "Remote tools may fail. Local log tools will still work if logsPath is configured.\n\n"
            };
            var combined = new List<ContentBlock> { warning };
            combined.AddRange(innerResult.Content);
            innerResult.Content = combined;
            return innerResult;
        }

        return await InvokeWithBootstrapGuard();
    }));

await builder.Build().RunAsync();
return 0;

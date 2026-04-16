using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SitefinityCommunity.Mcp.Configuration;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tests;

/// <summary>
/// Shared xUnit fixture that boots a real DI container matching Program.cs registrations.
/// Loads config from tests/test-config.json. If the file is missing, sets IsAvailable = false
/// so integration tests skip gracefully instead of crashing.
/// </summary>
public sealed class SitefinityFixture : IAsyncLifetime
{
    private ServiceProvider? _serviceProvider;

    /// <summary>
    /// False when test-config.json is missing or Sitefinity is unreachable.
    /// Integration tests should check this and return early.
    /// </summary>
    public bool IsAvailable { get; private set; }

    public string SkipReason { get; private set; } = string.Empty;

    public ISitefinityMetadataService MetadataService
        => this._serviceProvider!.GetRequiredService<ISitefinityMetadataService>();

    public ISitefinityStatusService StatusService
        => this._serviceProvider!.GetRequiredService<ISitefinityStatusService>();

    public IApiKeyValidationService ApiKeyValidationService
        => this._serviceProvider!.GetRequiredService<IApiKeyValidationService>();

    public async Task InitializeAsync()
    {
        var configPath = FindConfigFile();
        if (configPath is null)
        {
            this.IsAvailable = false;
            this.SkipReason = "tests/test-config.json not found. Copy test-config.example.json and fill in your dev credentials.";
            return;
        }

        SitefinityMcpConfig config;
        try
        {
            var json = await File.ReadAllTextAsync(configPath);
            config = JsonSerializer.Deserialize<SitefinityMcpConfig>(json)
                ?? throw new InvalidOperationException("Failed to deserialize test-config.json");
        }
        catch (Exception ex)
        {
            this.IsAvailable = false;
            this.SkipReason = $"Failed to load test-config.json: {ex.Message}";
            return;
        }

        // Build DI container matching Program.cs registrations
        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton<IEnvironmentResolver>(new EnvironmentResolver(config));
        services.AddSingleton<ISitefinityStatusService, SitefinityStatusService>();
        services.AddSingleton<IApiKeyValidationService, ApiKeyValidationService>();
        services.AddSingleton<ISitefinityMetadataService, SitefinityMetadataService>();
        services.AddHttpClient();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

        this._serviceProvider = services.BuildServiceProvider();

        // Wait for Sitefinity to be ready (with a generous timeout for cold starts)
        try
        {
            var status = await this.StatusService.WaitForReadyAsync(maxWaitSeconds: 120);
            if (!status.IsReady)
            {
                this.IsAvailable = false;
                this.SkipReason = $"Sitefinity not ready after 120s: {status.Summary}";
                return;
            }

            // Also validate the API key
            var keyResult = await this.ApiKeyValidationService.ValidateAsync();
            if (keyResult == ApiKeyValidationResult.InvalidKey)
            {
                this.IsAvailable = false;
                this.SkipReason = "API key mismatch — update test-config.json or Sitefinity McpSettings.";
                return;
            }
        }
        catch (Exception ex)
        {
            this.IsAvailable = false;
            this.SkipReason = $"Could not connect to Sitefinity: {ex.Message}";
            return;
        }

        this.IsAvailable = true;
    }

    public Task DisposeAsync()
    {
        this._serviceProvider?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Walks up from the bin directory to find tests/test-config.json in the repo root.
    /// </summary>
    private static string? FindConfigFile()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "tests", "test-config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Also check if we're already in the tests directory
            candidate = Path.Combine(dir, "test-config.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}

[CollectionDefinition("Sitefinity")]
public class SitefinityCollection : ICollectionFixture<SitefinityFixture> { }

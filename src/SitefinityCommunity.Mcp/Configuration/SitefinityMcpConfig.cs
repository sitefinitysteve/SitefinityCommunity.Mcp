using System.Text.Json;
using System.Text.Json.Serialization;

namespace SitefinityCommunity.Mcp.Configuration;

/// <summary>
/// Root configuration loaded from the JSON config file pointed to by SITEFINITY_MCP_CONFIG env var.
/// </summary>
public sealed class SitefinityMcpConfig
{
    [JsonPropertyName("defaultEnvironment")]
    public string DefaultEnvironment { get; set; } = string.Empty;

    [JsonPropertyName("environments")]
    public Dictionary<string, EnvironmentConfig> Environments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Loads and validates configuration from the JSON file specified by SITEFINITY_MCP_CONFIG.
    /// </summary>
    public static SitefinityMcpConfig Load()
    {
        var configPath = Environment.GetEnvironmentVariable("SITEFINITY_MCP_CONFIG");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            throw new InvalidOperationException(
                "SITEFINITY_MCP_CONFIG environment variable is not set. " +
                "Set it to the path of your sitefinity-mcp.json config file.");
        }

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Config file not found at '{configPath}'. " +
                "Ensure SITEFINITY_MCP_CONFIG points to a valid sitefinity-mcp.json file.",
                configPath);
        }

        var json = File.ReadAllText(configPath);
        var options = JsonSerializer.Deserialize<SitefinityMcpConfig>(json)
            ?? throw new InvalidOperationException($"Failed to deserialize config file: {configPath}");

        options.Validate();
        return options;
    }

    private void Validate()
    {
        if (this.Environments.Count == 0)
        {
            throw new InvalidOperationException("Config error: at least one environment must be configured.");
        }

        if (string.IsNullOrWhiteSpace(this.DefaultEnvironment))
        {
            this.DefaultEnvironment = this.Environments.Keys.First();
        }

        if (!this.Environments.ContainsKey(this.DefaultEnvironment))
        {
            throw new InvalidOperationException(
                $"Config error: defaultEnvironment '{this.DefaultEnvironment}' " +
                $"not found in environments. Available: {string.Join(", ", this.Environments.Keys)}");
        }

        foreach (var (name, env) in this.Environments)
        {
            if (string.IsNullOrWhiteSpace(env.Url))
            {
                throw new InvalidOperationException($"Config error: environment '{name}' must have a 'url'.");
            }

            if (string.IsNullOrWhiteSpace(env.SitefinityApiKey))
            {
                throw new InvalidOperationException(
                    $"Config error: environment '{name}' must have a 'sitefinityApiKey'. " +
                    "Set the same key in Sitefinity Admin > Settings > Advanced > McpSettings.");
            }
        }
    }
}

/// <summary>
/// Configuration for a single Sitefinity environment (dev, staging, prod, etc.).
/// </summary>
public sealed class EnvironmentConfig
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Local filesystem path to Sitefinity logs. When set, uses LocalLogProvider.
    /// When empty, uses RemoteLogProvider via the companion plugin.
    /// </summary>
    [JsonPropertyName("logsPath")]
    public string? LogsPath { get; set; }

    /// <summary>
    /// API key for authenticating with the Sitefinity companion plugin.
    /// Must match the API key configured in Sitefinity Admin > Settings > Advanced > McpSettings.
    /// </summary>
    [JsonPropertyName("sitefinityApiKey")]
    public string SitefinityApiKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, permits state-changing tools (clear cache, recycle app pool) to run against this
    /// environment. Ignored for environments whose name starts with "prod" (case-insensitive) — those
    /// always refuse write operations as a misconfiguration guard.
    /// Default: false. The Sitefinity plugin enforces a matching server-side switch
    /// (Admin > Advanced > McpSettings > Allow Write Operations), so both layers must opt in.
    /// </summary>
    [JsonPropertyName("allowWriteOperations")]
    public bool AllowWriteOperations { get; set; }

    /// <summary>
    /// Whether this environment uses local filesystem log access.
    /// </summary>
    [JsonIgnore]
    public bool IsLocalMode => !string.IsNullOrWhiteSpace(this.LogsPath);

    /// <summary>
    /// True when state-changing (write) tools may target this environment. Always false for prod-like
    /// environments regardless of the configured flag.
    /// </summary>
    public bool EffectiveAllowWriteOperations(string environmentName) =>
        this.AllowWriteOperations && !environmentName.StartsWith("prod", StringComparison.OrdinalIgnoreCase);
}

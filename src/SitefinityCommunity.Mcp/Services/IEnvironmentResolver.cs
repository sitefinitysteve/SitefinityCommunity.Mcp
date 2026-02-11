using SitefinityCommunity.Mcp.Configuration;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Resolves named Sitefinity environments from configuration and tracks the active default.
/// </summary>
public interface IEnvironmentResolver
{
    /// <summary>
    /// Gets the configuration for the specified environment, or the current default if name is null.
    /// </summary>
    (string Name, EnvironmentConfig Config) Resolve(string? environmentName = null);

    /// <summary>
    /// Gets the current default environment name.
    /// </summary>
    string DefaultEnvironment { get; }

    /// <summary>
    /// Sets the active default environment. Returns true if successful, false if the name is unknown.
    /// </summary>
    bool SetDefault(string environmentName);

    /// <summary>
    /// Gets all configured environments.
    /// </summary>
    IReadOnlyDictionary<string, EnvironmentConfig> GetAll();
}

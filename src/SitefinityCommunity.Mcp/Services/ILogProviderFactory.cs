namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Factory that creates the appropriate ILogProvider for a given environment.
/// </summary>
public interface ILogProviderFactory
{
    /// <summary>
    /// Creates a log provider for the specified environment.
    /// Returns LocalLogProvider if logsPath is configured, RemoteLogProvider otherwise.
    /// </summary>
    ILogProvider Create(string? environmentName = null);
}

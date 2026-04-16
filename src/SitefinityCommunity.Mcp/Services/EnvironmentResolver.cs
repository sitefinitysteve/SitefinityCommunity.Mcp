using SitefinityCommunity.Mcp.Configuration;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Default <see cref="IEnvironmentResolver"/>. Resolves environments by name from the loaded
/// <see cref="SitefinityMcpConfig"/> and tracks the active default (mutable for the lifetime of
/// the process; a lock guards the default-name read/write).
/// </summary>
public sealed class EnvironmentResolver : IEnvironmentResolver
{
    private readonly SitefinityMcpConfig _options;
    private string _defaultEnvironment;
    private readonly object _lock = new();

    public EnvironmentResolver(SitefinityMcpConfig options)
    {
        this._options = options;
        this._defaultEnvironment = options.DefaultEnvironment;
    }

    public string DefaultEnvironment
    {
        get
        {
            lock (this._lock)
            {
                return this._defaultEnvironment;
            }
        }
    }

    public (string Name, EnvironmentConfig Config) Resolve(string? environmentName = null)
    {
        var name = string.IsNullOrWhiteSpace(environmentName) ? this.DefaultEnvironment : environmentName;

        if (!this._options.Environments.TryGetValue(name, out var config))
        {
            var available = string.Join(", ", this._options.Environments.Keys);
            throw new ArgumentException(
                $"Unknown environment '{name}'. Available environments: {available}");
        }

        return (name, config);
    }

    public bool SetDefault(string environmentName)
    {
        if (!this._options.Environments.ContainsKey(environmentName))
        {
            return false;
        }

        lock (this._lock)
        {
            this._defaultEnvironment = environmentName;
        }

        return true;
    }

    public IReadOnlyDictionary<string, EnvironmentConfig> GetAll() => this._options.Environments;
}

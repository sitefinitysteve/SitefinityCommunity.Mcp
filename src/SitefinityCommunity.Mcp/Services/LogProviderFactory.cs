using Microsoft.Extensions.Logging;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Default <see cref="ILogProviderFactory"/>. Chooses <see cref="LocalLogProvider"/> when the
/// resolved environment has a <c>LogsPath</c>, otherwise builds a <see cref="RemoteLogProvider"/>
/// that hits the Sitefinity plugin's HTTP endpoints. Log output is always secret-redacted — there
/// is no opt-out in any environment.
/// </summary>
public sealed class LogProviderFactory : ILogProviderFactory
{
    private readonly IEnvironmentResolver _resolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RemoteLogProvider> _remoteLogger;

    public LogProviderFactory(
        IEnvironmentResolver resolver,
        IHttpClientFactory httpClientFactory,
        ILogger<RemoteLogProvider> remoteLogger)
    {
        this._resolver = resolver;
        this._httpClientFactory = httpClientFactory;
        this._remoteLogger = remoteLogger;
    }

    public ILogProvider Create(string? environmentName = null)
    {
        var (_, config) = this._resolver.Resolve(environmentName);

        if (config.IsLocalMode)
        {
            return new LocalLogProvider(config.LogsPath!);
        }

        return new RemoteLogProvider(config, this._httpClientFactory, this._remoteLogger);
    }
}

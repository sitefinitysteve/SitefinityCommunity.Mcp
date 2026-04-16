using Microsoft.Extensions.Logging;

namespace SitefinityCommunity.Mcp.Services;

/// <summary>
/// Default <see cref="ILogProviderFactory"/>. Chooses <see cref="LocalLogProvider"/> when the
/// resolved environment has a <c>LogsPath</c>, otherwise builds a <see cref="RemoteLogProvider"/>
/// that hits the Sitefinity plugin's HTTP endpoints. Threads the effective redaction flag into
/// the local provider (prod environments always redact regardless of config).
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
        var (name, config) = this._resolver.Resolve(environmentName);
        var allowRaw = config.EffectiveAllowRawSecrets(name);

        if (config.IsLocalMode)
        {
            return new LocalLogProvider(config.LogsPath!, allowRaw);
        }

        return new RemoteLogProvider(config, this._httpClientFactory, this._remoteLogger);
    }
}

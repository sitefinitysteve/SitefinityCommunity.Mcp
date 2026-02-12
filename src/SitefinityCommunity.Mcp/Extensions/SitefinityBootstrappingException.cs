namespace SitefinityCommunity.Mcp.Extensions;

/// <summary>
/// Thrown when an HTTP call to a Sitefinity MCP endpoint receives HTML instead of JSON,
/// indicating the site is still bootstrapping and redirecting to /sitefinity/status.
/// </summary>
public sealed class SitefinityBootstrappingException : Exception
{
    private const string DefaultMessage =
        "Sitefinity is still starting up (bootstrapping). Please wait a moment and try again.";

    public SitefinityBootstrappingException()
        : base(DefaultMessage) { }

    public SitefinityBootstrappingException(string message)
        : base(message) { }

    public SitefinityBootstrappingException(string message, Exception innerException)
        : base(message, innerException) { }
}

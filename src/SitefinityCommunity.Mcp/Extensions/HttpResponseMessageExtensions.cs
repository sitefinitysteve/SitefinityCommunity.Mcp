namespace SitefinityCommunity.Mcp.Extensions;

/// <summary>
/// Extension methods for detecting Sitefinity bootstrap redirects in HTTP responses.
/// Sitefinity redirects ALL requests to /sitefinity/status while bootstrapping,
/// returning HTML instead of the expected JSON. These helpers consolidate detection logic.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Returns true if the response Content-Type is HTML (text/html).
    /// </summary>
    public static bool IsHtmlResponse(this HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        return contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the HttpClient auto-followed a redirect to /sitefinity/status
    /// (the Sitefinity bootstrap loading page).
    /// </summary>
    public static bool IsBootstrapRedirect(this HttpResponseMessage response)
    {
        var finalUrl = response.RequestMessage?.RequestUri?.AbsolutePath ?? string.Empty;
        return finalUrl.Contains("/sitefinity/status", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the response indicates Sitefinity is still bootstrapping —
    /// either a redirect to /sitefinity/status or an HTML response where JSON was expected.
    /// </summary>
    public static bool IsSitefinityBootstrapping(this HttpResponseMessage response)
    {
        return response.IsBootstrapRedirect() || response.IsHtmlResponse();
    }

    /// <summary>
    /// Throws <see cref="SitefinityBootstrappingException"/> if the response indicates
    /// Sitefinity is bootstrapping. Call after <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>
    /// on any endpoint that expects JSON.
    /// </summary>
    public static void EnsureNotBootstrapping(this HttpResponseMessage response)
    {
        if (response.IsSitefinityBootstrapping())
        {
            throw new SitefinityBootstrappingException();
        }
    }
}

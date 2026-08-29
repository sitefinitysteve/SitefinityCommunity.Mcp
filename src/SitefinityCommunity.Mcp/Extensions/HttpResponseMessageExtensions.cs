using System.Net.Http.Json;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;

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

    /// <summary>
    /// Turns the plugin's capability-disabled 403 into a
    /// <see cref="SitefinityCapabilityDisabledException"/> carrying the same friendly message the
    /// tool filter uses. Call this <em>before</em>
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, which would otherwise raise a
    /// bare "403 Forbidden".
    /// <para>
    /// Any other 403 (a genuine authorization failure) is left alone for
    /// <c>EnsureSuccessStatusCode</c> to report.
    /// </para>
    /// </summary>
    /// <param name="response">Response from a Sitefinity MCP endpoint.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task EnsureCapabilityEnabledAsync(
        this HttpResponseMessage response, CancellationToken ct = default)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Forbidden || response.IsHtmlResponse())
        {
            return;
        }

        CapabilityDisabledBody? body = null;

        try
        {
            body = await response.Content.ReadFromJsonAsync<CapabilityDisabledBody>(
                SitefinityJsonOptions.Default, ct);
        }
        catch (Exception)
        {
            // Not a capability-disabled body — fall through and let the normal status handling run.
        }

        var capability = body?.Disabled;

        // ServiceStack may wrap the DTO in its ResponseStatus envelope instead of serializing it
        // raw (observed live on Sitefinity 15.4) — recover the capability name from the message.
        if (string.IsNullOrWhiteSpace(capability)
            && string.Equals(body?.ResponseStatus?.ErrorCode, "CapabilityDisabled", StringComparison.OrdinalIgnoreCase))
        {
            var message = body!.ResponseStatus!.Message ?? string.Empty;
            var match = System.Text.RegularExpressions.Regex.Match(message, "'([A-Za-z]+)' capability");
            capability = match.Success ? match.Groups[1].Value : "requested";
        }

        if (string.IsNullOrWhiteSpace(capability))
        {
            return;
        }

        throw new SitefinityCapabilityDisabledException(
            capability,
            CapabilityGate.BuildDisabledMessage(capability));
    }
}

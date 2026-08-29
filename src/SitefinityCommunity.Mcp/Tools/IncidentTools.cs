using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SitefinityCommunity.Mcp.Models;
using SitefinityCommunity.Mcp.Services;

namespace SitefinityCommunity.Mcp.Tools;

/// <summary>
/// The incident-forensics tool. One call correlates the four log sources that actually explain a
/// Sitefinity outage — Sitefinity's own logs, the IIS W3C access log, the Windows Application and
/// System event logs, and the http.sys HTTPERR log — with every timestamp normalized to both UTC and
/// server-local time.
/// </summary>
[McpServerToolType]
public sealed class IncidentTools
{
    private readonly ISitefinityMetadataService _metadataService;

    public IncidentTools(ISitefinityMetadataService metadataService)
    {
        this._metadataService = metadataService;
    }

    [McpServerTool(Name = "sitefinity_investigate_incident", Title = "Investigate Incident", ReadOnly = true, UseStructuredContent = true)]
    [Description("THE tool for \"what happened around <time>?\", \"why did the site go down?\", \"the site was " +
                 "slow/erroring this morning\", and any crash or outage investigation. It correlates four " +
                 "sources the site's own logs cannot see on their own — Sitefinity logs, the IIS W3C access " +
                 "log, the Windows Application AND System event logs (WAS 5009/5010/5011/5117 app-pool " +
                 "crashes, Application Error 1000, .NET Runtime 1026), and the http.sys HTTPERR log (the 503s " +
                 "that never reach the site log because the app pool was already dead) — and normalizes every " +
                 "timestamp to BOTH UTC and server-local time so clocks cannot be misread.\n\n" +
                 "Three modes, chosen by which arguments you pass:\n" +
                 "1. DISCOVERY (no time, no query) — \"the site crashed sometime this week, when?\". Returns " +
                 "Mode=\"candidates\": clustered candidate incident moments over lookbackHours, newest first, " +
                 "each with a headline signal like \"WAS 5011 worker process crash\". It deliberately does NOT " +
                 "scan the IIS access log (too large over multi-day ranges). Pick a candidate timestamp, then:\n" +
                 "2. WINDOW (time supplied) — Mode=\"window\": the full correlated reconstruction of that " +
                 "moment, with IIS aggregates (per-minute request counts, status histogram including " +
                 "sub-status, all 5xx, slowest requests).\n" +
                 "3. SEARCH (query, no time) — Mode=\"search\": sweeps every source over lookbackHours for a " +
                 "plain substring, e.g. query: \"steve@medportal.ca\" to find one user's trail, or an order id, " +
                 "or a URL path.\n\n" +
                 "Passing time AND query filters that window's entries to matching ones (IIS then returns the " +
                 "matching requests at ALL status codes, not just 5xx, while the aggregates still cover the " +
                 "whole window). Query is a case-insensitive plain substring, never a regex, and is matched " +
                 "AFTER secret redaction, so it cannot be used to probe for redacted values. Client IPs and " +
                 "IIS usernames ARE returned deliberately — they are what makes correlation possible.\n\n" +
                 "Individual sources (IIS, Event Log, HTTPERR) can be switched off by the Sitefinity " +
                 "administrator; a disabled source is skipped and reported in Warnings rather than failing " +
                 "the call, so always read Warnings before concluding a source was silent.")]
    public async Task<IncidentResponse> InvestigateIncident(
        [Description("The moment to centre the window on: \"11:00\", \"2026-08-27 11:00\", or full ISO 8601. " +
                     "Without an explicit offset or trailing Z it is read as SERVER-local time. " +
                     "Omit it to run discovery (or, with a query, a search) instead.")]
        string? time = null,
        [Description("Half-width of the window in minutes — the window is time +/- this. Default 15, max 120.")]
        int windowMinutes = 15,
        [Description("Case-insensitive plain substring (NOT a regex) to match against entries in every source: " +
                     "IIS username / URI / query string / client IP / referer, the whole Sitefinity entry, " +
                     "event provider + message, and the HTTPERR record. Example: \"steve@medportal.ca\".")]
        string? query = null,
        [Description("How far back discovery and search look, in hours. Default 72, max 336 (14 days). " +
                     "Ignored when a time is supplied.")]
        int lookbackHours = 72,
        [Description("Comma-separated sources to collect: sitefinity,iis,eventlog,httperr. Omit for all four. " +
                     "Narrow this when a search over a long lookback runs out of its time budget.")]
        string? sources = null,
        [Description("Target environment name (uses default if omitted)")]
        string? environment = null,
        CancellationToken ct = default)
    {
        try
        {
            return await this._metadataService.GetIncidentWindowAsync(
                time, windowMinutes, lookbackHours, query, sources, environment, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
        catch (Exception ex)
        {
            throw new McpException($"Error: {ex.Message}");
        }
    }
}

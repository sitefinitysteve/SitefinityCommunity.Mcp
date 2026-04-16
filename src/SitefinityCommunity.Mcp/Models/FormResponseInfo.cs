namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A single form submission (response / entry).
/// </summary>
public sealed class FormResponseInfo
{
    public string Id { get; set; } = string.Empty;
    public DateTime? SubmittedOn { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// Keyed by field name. Values may be redacted when the field name looks sensitive.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = [];
}

/// <summary>
/// Paged list of form responses for a given form. When the request included a search term,
/// <see cref="MatchedCount"/> reflects how many entries matched; otherwise it equals
/// <see cref="TotalCount"/>.
/// </summary>
public sealed class FormResponsesResponse
{
    public string FormId { get; set; } = string.Empty;
    public string FormName { get; set; } = string.Empty;

    /// <summary>Total submissions on the form regardless of search filter.</summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Entries that matched the search term (or equal to TotalCount when no search was applied).
    /// The <see cref="Responses"/> list is a Skip/Take window over the matched set.
    /// </summary>
    public int MatchedCount { get; set; }

    public int Take { get; set; }
    public int Skip { get; set; }

    /// <summary>Echo of the applied search term. Null when no search was provided.</summary>
    public string? SearchTerm { get; set; }

    public List<FormResponseInfo> Responses { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// A single hit from the backend advanced-settings Lucene index.
/// </summary>
public sealed class SettingsSearchResult
{
    /// <summary>The setting's display caption, when the index provides one.</summary>
    public string? Title { get; set; }

    /// <summary>Breadcrumb path to the setting within Advanced Settings.</summary>
    public string? Path { get; set; }

    /// <summary>Owning config section, when the index provides one.</summary>
    public string? Section { get; set; }

    /// <summary>Every indexed field on the document, secret-redacted on the plugin side.</summary>
    public Dictionary<string, string> Fields { get; set; } = [];
}

/// <summary>
/// Results of a full-text search over the "advanced-settings-search" backend index (Sitefinity 14.1+).
/// </summary>
public sealed class SettingsSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public int Take { get; set; }
    public int ReturnedCount { get; set; }

    /// <summary>False when the index is disabled, missing, or the search API could not be resolved.</summary>
    public bool IndexAvailable { get; set; }

    /// <summary>Which query-construction variant produced the results (diagnostic).</summary>
    public string? QueryVariant { get; set; }

    public List<SettingsSearchResult> Results { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

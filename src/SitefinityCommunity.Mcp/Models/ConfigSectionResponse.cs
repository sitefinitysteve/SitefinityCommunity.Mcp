namespace SitefinityCommunity.Mcp.Models;

/// <summary>
/// The list of configuration section names available on a Sitefinity instance.
/// Returned by the config-section discovery endpoint so the agent can pick a valid
/// name before requesting a full dump.
/// </summary>
public sealed class ConfigSectionsResponse
{
    /// <summary>Registered config section names (e.g. "systemConfig", "securityConfig").</summary>
    public List<string> Sections { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// A flattened dump of a single Sitefinity configuration section. Values that look like
/// credentials are redacted on the plugin side before transit.
/// </summary>
public sealed class ConfigSectionResponse
{
    /// <summary>The section name that was requested.</summary>
    public string SectionName { get; set; } = string.Empty;

    /// <summary>The CLR type backing the section (e.g. "Telerik.Sitefinity.Configuration.SystemConfig").</summary>
    public string SectionType { get; set; } = string.Empty;

    /// <summary>False when no section matched the requested name.</summary>
    public bool Found { get; set; }

    /// <summary>
    /// Flattened name/value pairs. Nested config elements and dictionaries are expressed as
    /// dotted / indexed paths (e.g. "Providers[OpenAccessProvider].ConnectionStringName").
    /// Capped at <see cref="MaxEntries"/>.
    /// </summary>
    public List<ConfigEntry> Entries { get; set; } = [];

    /// <summary>Entries matching the filter across the whole section, ignoring the cap.</summary>
    public int TotalCount { get; set; }

    /// <summary>Entries actually returned.</summary>
    public int ReturnedCount { get; set; }

    /// <summary>True when <see cref="TotalCount"/> exceeded the cap and entries were dropped.</summary>
    public bool Truncated { get; set; }

    /// <summary>Whether defaults-valued leaves were included in this dump.</summary>
    public bool IncludedDefaults { get; set; }

    /// <summary>The path substring filter that was applied, if any.</summary>
    public string? PathFilter { get; set; }

    /// <summary>The entry cap that was applied.</summary>
    public int MaxEntries { get; set; }

    /// <summary>Leaves suppressed because they still held their compiled-in default value.</summary>
    public int DefaultsSkipped { get; set; }

    public List<string> Warnings { get; set; } = [];
}

/// <summary>
/// A single flattened configuration value.
/// </summary>
public sealed class ConfigEntry
{
    /// <summary>Dotted / indexed path to the value within the section.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The value as a string. May be "[REDACTED]" when the path looks sensitive.</summary>
    public string Value { get; set; } = string.Empty;
}

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
    /// </summary>
    public List<ConfigEntry> Entries { get; set; } = [];

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

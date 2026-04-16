using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace SitefinityCommunity.Mcp.Security;

/// <summary>
/// Scrubs credentials, tokens, and connection-string secrets out of strings and dictionaries
/// before they reach the LLM. Defense-in-depth: anything the plugin misses, this catches.
///
/// Rules are pattern-based (not entropy-based) to keep false positives low on CMS content.
/// </summary>
public static partial class SecretRedactor
{
    public const string Placeholder = "[REDACTED]";

    /// <summary>
    /// Keys whose values are always scrubbed, regardless of value shape. Case-insensitive.
    /// Matched both by exact name and by the "contains" substrings below.
    /// </summary>
    private static readonly FrozenSet<string> ExactDenyKeys =
        new[]
        {
            "password", "pwd", "secret", "apikey", "api_key", "apitoken", "api_token",
            "token", "accesstoken", "access_token", "refreshtoken", "refresh_token",
            "clientsecret", "client_secret", "connectionstring", "connection_string",
            "privatekey", "private_key", "smtppassword", "bearer", "authorization",
            "cookie", "x-api-key", "x_api_key",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly string[] SubstringDenyFragments =
        ["password", "secret", "apikey", "clientsecret", "privatekey"];

    /// <summary>
    /// True when the key name alone is enough to warrant scrubbing its value.
    /// </summary>
    public static bool IsDeniedKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        if (ExactDenyKeys.Contains(key))
        {
            return true;
        }

        foreach (var fragment in SubstringDenyFragments)
        {
            if (key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Scrubs tokens and credential patterns out of a free-form string
    /// (log lines, HTML content, connection strings, JSON blobs).
    /// </summary>
    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? string.Empty;
        }

        var s = input;
        s = JwtPattern().Replace(s, "[REDACTED:jwt]");
        s = BearerPattern().Replace(s, m => m.Groups[1].Value + " [REDACTED:bearer]");
        s = AwsAccessKeyPattern().Replace(s, "[REDACTED:aws-key]");
        s = AwsSecretPattern().Replace(s, m => m.Groups[1].Value + "=[REDACTED:aws-secret]");
        s = InstrumentationKeyPattern().Replace(s, "InstrumentationKey=[REDACTED:appinsights]");
        s = OpenAIKeyPattern().Replace(s, "[REDACTED:openai]");
        s = SlackTokenPattern().Replace(s, "[REDACTED:slack]");
        s = GitHubTokenPattern().Replace(s, "[REDACTED:github]");
        s = AzureStorageKeyPattern().Replace(s, "AccountKey=[REDACTED:azure-storage]");
        s = ConnStringPasswordPattern().Replace(s, m => m.Groups[1].Value + "=[REDACTED]");
        return s;
    }

    /// <summary>
    /// Copies a dictionary, scrubbing values whose keys are on the deny-list
    /// and running Redact() over the remaining values.
    /// </summary>
    public static Dictionary<string, string> RedactDictionary(IDictionary<string, string> properties)
    {
        var result = new Dictionary<string, string>(properties.Count);
        foreach (var (key, value) in properties)
        {
            result[key] = IsDeniedKey(key) ? Placeholder : Redact(value);
        }
        return result;
    }

    // --- Patterns --------------------------------------------------------

    // JWT: three base64url segments separated by dots, each >= 10 chars
    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled)]
    private static partial Regex JwtPattern();

    // Authorization: Bearer <token> or Basic <token>
    [GeneratedRegex(@"\b(Authorization|Bearer|Basic)\s+[A-Za-z0-9+/=._\-]{20,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BearerPattern();

    // AWS access key IDs
    [GeneratedRegex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.Compiled)]
    private static partial Regex AwsAccessKeyPattern();

    // AWS secret access key (by context)
    [GeneratedRegex(@"\b(aws_secret_access_key|awssecretkey)\s*=\s*[A-Za-z0-9/+=]{40}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AwsSecretPattern();

    // Application Insights instrumentation key
    [GeneratedRegex(@"InstrumentationKey\s*=\s*[A-Fa-f0-9\-]{36}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex InstrumentationKeyPattern();

    // OpenAI / Anthropic-shaped secret keys
    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{20,}\b", RegexOptions.Compiled)]
    private static partial Regex OpenAIKeyPattern();

    // Slack tokens
    [GeneratedRegex(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}\b", RegexOptions.Compiled)]
    private static partial Regex SlackTokenPattern();

    // GitHub personal access tokens
    [GeneratedRegex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}\b|\bgithub_pat_[A-Za-z0-9_]{30,}\b", RegexOptions.Compiled)]
    private static partial Regex GitHubTokenPattern();

    // Azure Storage account key (base64, 88 chars)
    [GeneratedRegex(@"AccountKey\s*=\s*[A-Za-z0-9+/=]{20,}", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex AzureStorageKeyPattern();

    // SQL connection-string password fragment: "Password=...;" / "pwd=...;"
    [GeneratedRegex(@"\b(Password|Pwd)\s*=\s*([^;""'\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ConnStringPasswordPattern();
}

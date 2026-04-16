// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
//
// Mirror of SitefinityCommunity.Mcp.Security.SecretRedactor (MCP server side)
// targeting .NET Framework 4.8 (non-source-generated Regex).
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Scrubs credentials, tokens, and connection-string secrets out of strings and dictionaries
    /// before they leave the Sitefinity process. Defense-in-depth against widget/form values
    /// or log content that the LLM should never see in raw form.
    /// </summary>
    public static class McpSecretRedactor
    {
        public const string Placeholder = "[REDACTED]";

        private static readonly HashSet<string> ExactDenyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password", "pwd", "secret", "apikey", "api_key", "apitoken", "api_token",
            "token", "accesstoken", "access_token", "refreshtoken", "refresh_token",
            "clientsecret", "client_secret", "connectionstring", "connection_string",
            "privatekey", "private_key", "smtppassword", "bearer", "authorization",
            "cookie", "x-api-key", "x_api_key",
        };

        private static readonly string[] SubstringDenyFragments =
            { "password", "secret", "apikey", "clientsecret", "privatekey" };

        private static readonly Regex JwtPattern =
            new Regex(@"eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}", RegexOptions.Compiled);

        private static readonly Regex BearerPattern =
            new Regex(@"\b(Authorization|Bearer|Basic)\s+[A-Za-z0-9+/=._\-]{20,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AwsAccessKeyPattern =
            new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.Compiled);

        private static readonly Regex AwsSecretPattern =
            new Regex(@"\b(aws_secret_access_key|awssecretkey)\s*=\s*[A-Za-z0-9/+=]{40}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex InstrumentationKeyPattern =
            new Regex(@"InstrumentationKey\s*=\s*[A-Fa-f0-9\-]{36}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OpenAIKeyPattern =
            new Regex(@"\bsk-[A-Za-z0-9_\-]{20,}\b", RegexOptions.Compiled);

        private static readonly Regex SlackTokenPattern =
            new Regex(@"\bxox[baprs]-[A-Za-z0-9\-]{10,}\b", RegexOptions.Compiled);

        private static readonly Regex GitHubTokenPattern =
            new Regex(@"\b(?:ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9]{30,}\b|\bgithub_pat_[A-Za-z0-9_]{30,}\b", RegexOptions.Compiled);

        private static readonly Regex AzureStorageKeyPattern =
            new Regex(@"AccountKey\s*=\s*[A-Za-z0-9+/=]{20,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ConnStringPasswordPattern =
            new Regex(@"\b(Password|Pwd)\s*=\s*([^;""'\s]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                if (key.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public static string Redact(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input ?? string.Empty;
            }

            var s = input;
            s = JwtPattern.Replace(s, "[REDACTED:jwt]");
            s = BearerPattern.Replace(s, m => m.Groups[1].Value + " [REDACTED:bearer]");
            s = AwsAccessKeyPattern.Replace(s, "[REDACTED:aws-key]");
            s = AwsSecretPattern.Replace(s, m => m.Groups[1].Value + "=[REDACTED:aws-secret]");
            s = InstrumentationKeyPattern.Replace(s, "InstrumentationKey=[REDACTED:appinsights]");
            s = OpenAIKeyPattern.Replace(s, "[REDACTED:openai]");
            s = SlackTokenPattern.Replace(s, "[REDACTED:slack]");
            s = GitHubTokenPattern.Replace(s, "[REDACTED:github]");
            s = AzureStorageKeyPattern.Replace(s, "AccountKey=[REDACTED:azure-storage]");
            s = ConnStringPasswordPattern.Replace(s, m => m.Groups[1].Value + "=[REDACTED]");
            return s;
        }

        public static Dictionary<string, string> RedactDictionary(IDictionary<string, string> properties)
        {
            var result = new Dictionary<string, string>(properties.Count);
            foreach (var kvp in properties)
            {
                result[kvp.Key] = IsDeniedKey(kvp.Key) ? Placeholder : Redact(kvp.Value);
            }
            return result;
        }
    }
}

// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Reflection;
using Telerik.Sitefinity.Configuration;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Read-only access to Sitefinity configuration sections. There is no string-keyed section
    /// lookup in the public API, so sections are discovered by scanning loaded assemblies for
    /// <see cref="ConfigSection"/>-derived types and read through <c>Config.Get&lt;T&gt;()</c> via
    /// reflection. Every value is routed through <see cref="McpSecretRedactor"/> — config sections
    /// carry SMTP passwords, connection strings, and API keys.
    /// </summary>
    [McpApiKey]
    public class McpConfigService : Service
    {
        private const int MaxDepth = 12;

        /// <summary>
        /// GET /RestApi/mcp/config — names of all registered configuration sections.
        /// </summary>
        public McpConfigSectionsResponse Get(GetConfigSections request)
        {
            var response = new McpConfigSectionsResponse();

            try
            {
                foreach (var type in DiscoverSectionTypes())
                {
                    response.Sections.Add(type.Name);
                }

                response.Sections = response.Sections
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error enumerating config sections: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/config/{SectionName} — flattened, redacted dump of a single section.
        /// </summary>
        public McpConfigSectionResponse Get(GetConfigSection request)
        {
            if (string.IsNullOrWhiteSpace(request.SectionName))
            {
                throw HttpError.BadRequest("SectionName is required.");
            }

            var response = new McpConfigSectionResponse { SectionName = request.SectionName };

            try
            {
                var sectionType = ResolveSectionType(request.SectionName);
                if (sectionType == null)
                {
                    response.Found = false;
                    response.Warnings.Add("No configuration section matched '" + request.SectionName +
                        "'. Call /mcp/config to list valid names.");
                    return response;
                }

                response.SectionType = sectionType.FullName;

                var section = GetSectionInstance(sectionType);
                if (section == null)
                {
                    response.Found = false;
                    response.Warnings.Add("Section type resolved but the instance could not be loaded.");
                    return response;
                }

                response.Found = true;

                var element = section as ConfigurationElement;
                if (element == null)
                {
                    response.Warnings.Add("Section is not a ConfigurationElement; cannot enumerate properties.");
                    return response;
                }

                DumpElement(element, string.Empty, response.Entries,
                    new HashSet<object>(ReferenceEqualityComparer.Instance), 0, response.Warnings);

                response.Warnings.Add("Credential-like values (keys, passwords, connection strings, tokens, " +
                    "encrypted/[SecretData] properties) are ALWAYS redacted and never returned — by design, " +
                    "in every environment including dev. There is no flag to reveal them.");

                response.Entries = response.Entries
                    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HttpError(HttpStatusCode.InternalServerError, "Error reading config section: " + ex.Message);
            }

            return response;
        }

        // ── Private Helpers ──────────────────────────────────────────

        /// <summary>
        /// Every non-abstract <see cref="ConfigSection"/>-derived type loaded in the AppDomain.
        /// Self-maintaining — no hardcoded section list to drift out of date.
        /// </summary>
        private static IEnumerable<Type> DiscoverSectionTypes()
        {
            var baseType = typeof(ConfigSection);
            var results = new List<Type>();

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    types = rtle.Types.Where(t => t != null).ToArray();
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var t in types)
                {
                    if (t != null && !t.IsAbstract && baseType.IsAssignableFrom(t))
                    {
                        results.Add(t);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Matches a requested name against discovered section types by simple name or full name,
        /// case-insensitively. Accepts the camelCase form Sitefinity uses on disk (e.g. "systemConfig").
        /// </summary>
        private static Type ResolveSectionType(string name)
        {
            var trimmed = name.Trim();
            var types = DiscoverSectionTypes().ToList();

            // Exact simple-name or full-name match first
            var match = types.FirstOrDefault(t =>
                string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(t.FullName, trimmed, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                return match;
            }

            // Fold a leading-lower camelCase request ("systemConfig") to PascalCase ("SystemConfig")
            if (trimmed.Length > 0 && char.IsLower(trimmed[0]))
            {
                var pascal = char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
                match = types.FirstOrDefault(t => string.Equals(t.Name, pascal, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        /// <summary>
        /// Calls <c>Config.Get&lt;T&gt;()</c> for a runtime section type via reflection (the public API
        /// is generic-only). Falls back to <c>ConfigManager.GetManager().GetSection&lt;T&gt;()</c>.
        /// </summary>
        private static object GetSectionInstance(Type sectionType)
        {
            // Config.Get<T>() — static, no-arg generic
            try
            {
                var getMethod = typeof(Config)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Get" && m.IsGenericMethodDefinition
                                         && m.GetParameters().Length == 0);

                if (getMethod != null)
                {
                    return getMethod.MakeGenericMethod(sectionType).Invoke(null, null);
                }
            }
            catch (Exception)
            {
                // fall through to ConfigManager
            }

            try
            {
                var manager = ConfigManager.GetManager();
                var getSection = typeof(ConfigManager)
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "GetSection" && m.IsGenericMethodDefinition
                                         && m.GetParameters().Length == 0);

                if (getSection != null)
                {
                    return getSection.MakeGenericMethod(sectionType).Invoke(manager, null);
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// Recursively flattens a config element into Path=Value entries using the standard .NET
        /// configuration model (<see cref="ConfigurationElement.ElementInformation"/>), so it works
        /// for any Sitefinity section without binding to version-specific Sitefinity collection types.
        /// </summary>
        private static void DumpElement(
            ConfigurationElement element,
            string path,
            List<McpConfigEntry> entries,
            HashSet<object> visited,
            int depth,
            List<string> warnings)
        {
            if (element == null || depth > MaxDepth)
            {
                return;
            }

            if (!visited.Add(element))
            {
                return; // cycle guard
            }

            PropertyInformationCollection props;
            try
            {
                props = element.ElementInformation.Properties;
            }
            catch (Exception)
            {
                return;
            }

            foreach (PropertyInformation pi in props)
            {
                object value;
                try
                {
                    value = pi.Value;
                }
                catch (Exception)
                {
                    continue;
                }

                var childPath = string.IsNullOrEmpty(path) ? pi.Name : path + "." + pi.Name;

                var ownerType = element.GetType();

                if (value is ConfigurationElement nestedElement && !(value is IEnumerable))
                {
                    DumpElement(nestedElement, childPath, entries, visited, depth + 1, warnings);
                }
                else if (value is IEnumerable enumerable && !(value is string))
                {
                    var index = 0;
                    foreach (var item in enumerable)
                    {
                        if (item is ConfigurationElement childElement)
                        {
                            var key = TryGetElementKey(childElement) ?? index.ToString();
                            DumpElement(childElement, childPath + "[" + key + "]", entries, visited, depth + 1, warnings);
                        }
                        else if (item != null)
                        {
                            entries.Add(BuildEntry(childPath + "[" + index + "]", pi.Name, item, ownerType));
                        }

                        index++;
                    }
                }
                else
                {
                    entries.Add(BuildEntry(childPath, pi.Name, value, ownerType));
                }
            }
        }

        /// <summary>
        /// Builds a single entry, fully redacting anything that could leak a credential. This guard is
        /// UNCONDITIONAL — there is no flag to disable it, in any environment. A raw secret that reaches
        /// the LLM context is a leak (it can be logged, cached, or absorbed into training data), so the
        /// config reader must never emit one even on dev. Layers a config-specific guard on top of
        /// <see cref="McpSecretRedactor"/>:
        ///   • the standard deny-list / value-pattern scan,
        ///   • any path or leaf name signalling secrecy (key, connectionstring, password, token, …),
        ///   • properties Sitefinity marks <c>[SecretData]</c> — i.e. values stored ENCRYPTED on disk,
        ///   • whole values shaped like a connection string.
        /// Over-redaction is intentional: a config dump must never be a credential exfiltration path.
        /// </summary>
        private static McpConfigEntry BuildEntry(string path, string leafName, object value, Type ownerType)
        {
            var raw = value == null ? string.Empty : value.ToString();

            var mustRedact =
                McpSecretRedactor.IsDeniedKey(leafName)
                || IsSensitiveConfigPath(path, leafName)
                || IsSecretDataProperty(ownerType, leafName)
                || LooksLikeConnectionString(raw);

            var scrubbed = mustRedact ? McpSecretRedactor.Placeholder : McpSecretRedactor.Redact(raw);
            return new McpConfigEntry { Path = path, Value = scrubbed };
        }

        /// <summary>
        /// True when a config path or leaf name signals a credential, key, certificate, or connection
        /// string. Deliberately broad — "key" matches too, accepting some false positives on innocuous
        /// id columns in exchange for never leaking a real key.
        /// </summary>
        private static bool IsSensitiveConfigPath(string path, string leafName)
        {
            var hay = ((path ?? string.Empty) + " " + (leafName ?? string.Empty)).ToLowerInvariant();

            string[] markers =
            {
                "connectionstring", "connection", "password", "pwd", "secret", "apikey", "api_key",
                "token", "credential", "salt", "thumbprint", "certificate", "privatekey", "encrypted",
                "accountkey", "accesskey", "key",
            };

            foreach (var m in markers)
            {
                if (hay.IndexOf(m, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the value looks like a database/storage connection string (two or more of the
        /// common key=value tokens), regardless of the property name it lives under.
        /// </summary>
        private static bool LooksLikeConnectionString(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('=') < 0)
            {
                return false;
            }

            var v = value.ToLowerInvariant();
            string[] tokens =
            {
                "data source=", "server=", "initial catalog=", "database=", "accountkey=",
                "provider=", "uid=", "user id=", "password=", "pwd=", "integrated security=",
                "accountname=", "endpoint=",
            };

            var hits = 0;
            foreach (var t in tokens)
            {
                if (v.IndexOf(t, StringComparison.Ordinal) >= 0)
                {
                    hits++;
                }

                if (hits >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when the owning config property is decorated with Sitefinity's <c>[SecretData]</c>
        /// attribute — the marker for values persisted in ENCRYPTED form (the same attribute the MCP
        /// plugin uses on its own API key). Such values decrypt to plaintext when read, so they must
        /// be withheld.
        /// </summary>
        private static bool IsSecretDataProperty(Type ownerType, string propName)
        {
            if (ownerType == null || string.IsNullOrEmpty(propName))
            {
                return false;
            }

            try
            {
                var prop = ownerType.GetProperty(propName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (prop == null)
                {
                    return false;
                }

                foreach (var attr in prop.GetCustomAttributes(true))
                {
                    if (attr.GetType().Name.IndexOf("Secret", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        /// <summary>
        /// Best-effort friendly key for a collection element: Sitefinity dictionary entries expose
        /// GetKey(); otherwise probe common identifying properties.
        /// </summary>
        private static string TryGetElementKey(ConfigurationElement element)
        {
            try
            {
                var getKey = element.GetType().GetMethod("GetKey",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                if (getKey != null)
                {
                    var key = getKey.Invoke(element, null);
                    if (key != null)
                    {
                        return key.ToString();
                    }
                }
            }
            catch (Exception)
            {
            }

            foreach (var name in new[] { "Name", "Key", "Title", "ProviderName" })
            {
                try
                {
                    var prop = element.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                    var val = prop != null ? prop.GetValue(element, null) : null;

                    if (val != null && !string.IsNullOrEmpty(val.ToString()))
                    {
                        return val.ToString();
                    }
                }
                catch (Exception)
                {
                }
            }

            return null;
        }

        /// <summary>
        /// Reference-identity comparer so the cycle guard tracks element instances, not value equality.
        /// </summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}

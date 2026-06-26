// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Telerik.Sitefinity.Configuration;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Read-only access to Sitefinity configuration sections. There is no string-keyed section lookup in
    /// the public API, so sections are discovered by scanning loaded assemblies for
    /// <see cref="ConfigSection"/>-derived types, loaded via <c>Config.Get(Type)</c>, and flattened with
    /// Sitefinity's own config model (<see cref="ConfigElement.Properties"/> + the string indexer) — NOT
    /// <c>System.Configuration</c>, which Sitefinity's <see cref="ConfigElement"/> does not derive from.
    /// Every value is routed through <see cref="McpSecretRedactor"/> — config sections carry SMTP
    /// passwords, connection strings, and API keys.
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

                var element = section as ConfigElement;
                if (element == null)
                {
                    response.Warnings.Add("Section is not a Sitefinity ConfigElement; cannot enumerate properties.");
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

        // ── Section discovery / loading ──────────────────────────────

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
        /// Loads the live section instance. Sitefinity's public, non-generic <c>Config.Get(Type)</c>
        /// returns the populated <see cref="ConfigSection"/> for a runtime section type.
        /// </summary>
        private static object GetSectionInstance(Type sectionType)
        {
            try
            {
                return Config.Get(sectionType);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ── Flattening (Sitefinity config model) ─────────────────────

        /// <summary>
        /// Recursively flattens a Sitefinity <see cref="ConfigElement"/> into Path=Value entries using its
        /// <see cref="ConfigElement.Properties"/> descriptors and string indexer. A
        /// <c>ConfigElementDictionary</c> is both a ConfigElement and an <see cref="IEnumerable"/>, so the
        /// enumerable branch (checked first) walks dictionary/collection items, keyed by
        /// <see cref="ConfigElement.GetKey()"/>.
        /// </summary>
        private static void DumpElement(
            ConfigElement element,
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

            ConfigPropertyCollection props;
            try
            {
                props = element.Properties;
            }
            catch (Exception)
            {
                return;
            }

            if (props == null)
            {
                return;
            }

            var ownerType = element.GetType();

            foreach (ConfigProperty cp in props)
            {
                if (cp == null || string.IsNullOrEmpty(cp.Name))
                {
                    continue;
                }

                object value;
                try
                {
                    value = element[cp.Name];
                }
                catch (Exception)
                {
                    continue;
                }

                var childPath = string.IsNullOrEmpty(path) ? cp.Name : path + "." + cp.Name;

                if (value == null)
                {
                    entries.Add(BuildEntry(childPath, cp.Name, null, ownerType));
                    continue;
                }

                // Collection (ConfigElementDictionary, element collections, value arrays).
                if (value is IEnumerable enumerable && !(value is string))
                {
                    var index = 0;
                    foreach (var item in enumerable)
                    {
                        if (item is ConfigElement childElement)
                        {
                            var key = ElementKey(childElement) ?? index.ToString();
                            DumpElement(childElement, childPath + "[" + key + "]", entries, visited, depth + 1, warnings);
                        }
                        else if (item != null)
                        {
                            entries.Add(BuildEntry(childPath + "[" + index + "]", cp.Name, item, ownerType));
                        }

                        index++;
                    }

                    continue;
                }

                // Nested single element.
                if (value is ConfigElement nested)
                {
                    DumpElement(nested, childPath, entries, visited, depth + 1, warnings);
                    continue;
                }

                // Scalar leaf.
                entries.Add(BuildEntry(childPath, cp.Name, value, ownerType));
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

        /// <summary>Friendly key for a collection element via Sitefinity's <see cref="ConfigElement.GetKey()"/>.</summary>
        private static string ElementKey(ConfigElement element)
        {
            try
            {
                var key = element.GetKey();
                return string.IsNullOrEmpty(key) ? null : key;
            }
            catch (Exception)
            {
                return null;
            }
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

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
        private const int DefaultMaxEntries = 500;
        private const int MaxEntriesCeiling = 5000;

        // Sitefinity's config model carries the metadata needed to tell "someone set this" from "this is
        // the compiled-in default" — but the exact members drift across Sitefinity versions, and this file
        // is dropped into whatever version the host project runs. Resolve them reflectively once and
        // degrade gracefully (a missing member simply means that particular filter is not applied),
        // matching how McpMaintenanceService handles version-variant cache APIs.
        private static readonly PropertyInfo ConfigPropertyDefaultValue =
            SafeGetProperty(typeof(ConfigProperty), "DefaultValue");

        private static readonly PropertyInfo ConfigPropertySkipOnExport =
            SafeGetProperty(typeof(ConfigProperty), "SkipOnExport");

        private static readonly PropertyInfo ConfigPropertyIsSecret =
            SafeGetProperty(typeof(ConfigProperty), "IsSecret");

        private static readonly PropertyInfo ConfigElementSource =
            SafeGetProperty(typeof(ConfigElement), "Source");

        private static PropertyInfo SafeGetProperty(Type type, string name)
        {
            try
            {
                return type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception)
            {
                return null;
            }
        }

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

            var maxEntries = request.MaxEntries > 0
                ? Math.Min(request.MaxEntries, MaxEntriesCeiling)
                : DefaultMaxEntries;

            var response = new McpConfigSectionResponse
            {
                SectionName = request.SectionName,
                IncludedDefaults = request.IncludeDefaults,
                PathFilter = request.PathFilter,
                MaxEntries = maxEntries,
            };

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

                var context = new DumpContext
                {
                    IncludeDefaults = request.IncludeDefaults,
                    PathFilter = string.IsNullOrWhiteSpace(request.PathFilter) ? null : request.PathFilter.Trim(),
                    MaxEntries = maxEntries,
                    Entries = response.Entries,
                    Visited = new HashSet<object>(ReferenceEqualityComparer.Instance),
                };

                DumpElement(element, string.Empty, context, 0);

                response.TotalCount = context.TotalCount;
                response.DefaultsSkipped = context.DefaultsSkipped;
                response.ReturnedCount = response.Entries.Count;
                response.Truncated = context.TotalCount > response.Entries.Count;

                if (response.Truncated)
                {
                    response.Warnings.Add("Result truncated: " + context.TotalCount + " entries matched, " +
                        response.Entries.Count + " returned (MaxEntries=" + maxEntries + "). Narrow the result " +
                        "with PathFilter, or raise MaxEntries (ceiling " + MaxEntriesCeiling + ").");
                }

                if (!request.IncludeDefaults)
                {
                    response.Warnings.Add("Showing OVERRIDES ONLY — " + context.DefaultsSkipped + " leaves still " +
                        "holding their compiled-in default were suppressed. Sitefinity materializes a fully " +
                        "defaults-merged object graph, so a section like ContentViewConfig expands to hundreds of " +
                        "thousands of leaves that nobody ever set. Pass IncludeDefaults=true to see them (use " +
                        "PathFilter as well, or the result will be capped).");
                }

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
        private static void DumpElement(ConfigElement element, string path, DumpContext ctx, int depth)
        {
            if (element == null || depth > MaxDepth)
            {
                return;
            }

            if (!ctx.Visited.Add(element))
            {
                return; // cycle guard
            }

            // Whole-subtree prune. An element Sitefinity reports as sourced from Default holds nothing a
            // human ever touched, so the entire branch below it is scaffolding. Only an explicit "Default"
            // prunes — "NotSet" means this Sitefinity version does not populate Source reliably here, so
            // keep walking and let the per-leaf default comparison decide. Never prune the section root
            // itself (depth 0): an untouched section should still report its shape, not come back empty.
            if (!ctx.IncludeDefaults && depth > 0 && IsDefaultSourced(element))
            {
                ctx.DefaultsSkipped++;
                return;
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

                // Sitefinity's own "leave this out of an export" marker — machinery, never user intent.
                if (!ctx.IncludeDefaults && ReadBool(ConfigPropertySkipOnExport, cp))
                {
                    ctx.DefaultsSkipped++;
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
                var isSecret = ReadBool(ConfigPropertyIsSecret, cp);

                if (value == null)
                {
                    if (!ctx.IncludeDefaults)
                    {
                        ctx.DefaultsSkipped++;
                        continue;
                    }

                    ctx.Emit(childPath, cp.Name, null, ownerType, isSecret);
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
                            DumpElement(childElement, childPath + "[" + key + "]", ctx, depth + 1);
                        }
                        else if (item != null)
                        {
                            ctx.Emit(childPath + "[" + index + "]", cp.Name, item, ownerType, isSecret);
                        }

                        index++;
                    }

                    continue;
                }

                // Nested single element.
                if (value is ConfigElement nested)
                {
                    DumpElement(nested, childPath, ctx, depth + 1);
                    continue;
                }

                // Scalar leaf.
                if (!ctx.IncludeDefaults && IsDefaultValue(cp, value))
                {
                    ctx.DefaultsSkipped++;
                    continue;
                }

                ctx.Emit(childPath, cp.Name, value, ownerType, isSecret);
            }
        }

        /// <summary>
        /// True when a leaf still holds what the config model declares as its default — including the
        /// empty-string case, which carries no information whether or not a default is declared. This is
        /// the check that collapses a defaults-merged section back to the handful of values a human set.
        /// </summary>
        private static bool IsDefaultValue(ConfigProperty cp, object value)
        {
            var raw = value == null ? string.Empty : value.ToString();

            if (raw.Length == 0)
            {
                return true;
            }

            if (ConfigPropertyDefaultValue == null)
            {
                return false;
            }

            try
            {
                var declared = ConfigPropertyDefaultValue.GetValue(cp, null);
                var declaredRaw = declared == null ? string.Empty : declared.ToString();
                return string.Equals(raw, declaredRaw, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// True when Sitefinity reports this element as coming from compiled-in defaults. The
        /// <c>ConfigSource</c> enum is <c>NotSet | Default | FileSystem | Database | Import</c>; only an
        /// explicit <c>Default</c> is safe to prune on, since <c>NotSet</c> means "unknown here".
        /// </summary>
        private static bool IsDefaultSourced(ConfigElement element)
        {
            if (ConfigElementSource == null)
            {
                return false;
            }

            try
            {
                var source = ConfigElementSource.GetValue(element, null);

                if (source == null)
                {
                    return false;
                }

                return string.Equals(source.ToString(), "Default", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Reads a reflectively-resolved bool property, defaulting to false when unavailable.</summary>
        private static bool ReadBool(PropertyInfo property, object instance)
        {
            if (property == null || instance == null)
            {
                return false;
            }

            try
            {
                var value = property.GetValue(instance, null);
                return value is bool b && b;
            }
            catch (Exception)
            {
                return false;
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
        ///   • <c>ConfigProperty.IsSecret</c> — the config model's own first-class secret marker,
        ///   • properties Sitefinity marks <c>[SecretData]</c> — i.e. values stored ENCRYPTED on disk,
        ///   • whole values shaped like a connection string.
        /// Over-redaction is intentional: a config dump must never be a credential exfiltration path.
        /// </summary>
        private static McpConfigEntry BuildEntry(
            string path, string leafName, object value, Type ownerType, bool isSecretProperty)
        {
            var raw = value == null ? string.Empty : value.ToString();

            var mustRedact =
                isSecretProperty
                || McpSecretRedactor.IsDeniedKey(leafName)
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
        /// Options and running totals threaded through the recursive walk. Entries are counted before the
        /// cap is applied, so <see cref="TotalCount"/> reports the true match count even when the returned
        /// list was trimmed — the caller can tell "there are 40,000 of these" from "there are 12".
        /// </summary>
        private sealed class DumpContext
        {
            public bool IncludeDefaults { get; set; }

            public string PathFilter { get; set; }

            public int MaxEntries { get; set; }

            public List<McpConfigEntry> Entries { get; set; }

            public HashSet<object> Visited { get; set; }

            public int TotalCount { get; set; }

            public int DefaultsSkipped { get; set; }

            /// <summary>
            /// Records one matching leaf. Counting continues past the cap — only materialization stops,
            /// which is what keeps the response bounded while the reported total stays honest.
            /// </summary>
            public void Emit(string path, string leafName, object value, Type ownerType, bool isSecretProperty)
            {
                if (this.PathFilter != null &&
                    (path == null || path.IndexOf(this.PathFilter, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    return;
                }

                this.TotalCount++;

                if (this.Entries.Count >= this.MaxEntries)
                {
                    return;
                }

                this.Entries.Add(BuildEntry(path, leafName, value, ownerType, isSecretProperty));
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

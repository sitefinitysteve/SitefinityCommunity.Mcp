// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Full-text search over the backend "Advanced settings search" Lucene index (catalog
    /// <c>advanced-settings-search</c>, Sitefinity 14.1+). Answers "which section is setting X in?" —
    /// the question a flattened section dump cannot, because it requires already knowing the section.
    /// <para>
    /// Everything is resolved reflectively at runtime: the search API lives in
    /// <c>Telerik.Sitefinity.Search.Impl</c> and its shape drifts across Sitefinity versions, and the
    /// feature itself can be disabled in the admin. When the index is missing or the API cannot be
    /// resolved, the response says so and explains how to enable it rather than erroring.
    /// </para>
    /// Result field values are routed through <see cref="McpSecretRedactor"/> — the index contains
    /// config values, and config values can contain credentials.
    /// </summary>
    [McpApiKey]
    public class McpSettingsSearchService : Service
    {
        private const string IndexCatalogName = "advanced-settings-search";
        private const int DefaultTake = 25;
        private const int MaxTake = 100;

        /// <summary>
        /// GET /RestApi/mcp/settings/search — search the advanced-settings index.
        /// </summary>
        public McpSettingsSearchResponse Get(SearchSettings request)
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                throw HttpError.BadRequest("Query is required.");
            }

            var take = request.Take > 0 ? Math.Min(request.Take, MaxTake) : DefaultTake;
            var response = new McpSettingsSearchResponse
            {
                Query = request.Query,
                IndexName = IndexCatalogName,
                Take = take,
            };

            try
            {
                var service = ResolveSearchService(response.Warnings);
                if (service == null)
                {
                    response.IndexAvailable = false;
                    response.Warnings.Add(HowToEnableMessage());
                    return response;
                }

                var query = BuildSearchQuery(request.Query, take, response.Warnings);
                if (query == null)
                {
                    response.IndexAvailable = false;
                    return response;
                }

                IEnumerable resultSet;
                try
                {
                    resultSet = InvokeSearch(service, query);
                }
                catch (Exception searchEx)
                {
                    var inner = searchEx is TargetInvocationException tie ? (tie.InnerException ?? tie) : searchEx;
                    response.IndexAvailable = false;
                    response.Warnings.Add("Search failed: " + inner.Message);
                    response.Warnings.Add(HowToEnableMessage());
                    return response;
                }

                if (resultSet == null)
                {
                    response.IndexAvailable = false;
                    response.Warnings.Add("The search service returned no result set. " + HowToEnableMessage());
                    return response;
                }

                response.IndexAvailable = true;

                foreach (var doc in resultSet)
                {
                    if (doc == null)
                    {
                        continue;
                    }

                    response.Results.Add(MapDocument(doc));

                    if (response.Results.Count >= take)
                    {
                        break;
                    }
                }

                response.ReturnedCount = response.Results.Count;
            }
            catch (HttpError)
            {
                throw;
            }
            catch (Exception ex)
            {
                response.IndexAvailable = false;
                response.Warnings.Add("Settings search unavailable: " + ex.Message);
                response.Warnings.Add(HowToEnableMessage());
            }

            return response;
        }

        private static string HowToEnableMessage()
        {
            return "Advanced settings search requires the backend settings index (Sitefinity 14.1+). " +
                "Enable it in Administration > Settings > Advanced (the search box at the top builds the " +
                "'" + IndexCatalogName + "' index on first use), or check that search indexing is not disabled " +
                "for the backend. Falling back: use /mcp/config/{SectionName} with PathFilter to inspect a " +
                "known section directly.";
        }

        // ── Reflective resolution ────────────────────────────────────

        /// <summary>
        /// Resolves Sitefinity's <c>ISearchService</c> through <c>ServiceBus.ResolveService&lt;T&gt;()</c>.
        /// Both types are looked up by name so this file compiles against any Sitefinity version, including
        /// ones where the search implementation assembly is absent entirely.
        /// </summary>
        private static object ResolveSearchService(List<string> warnings)
        {
            var serviceInterface = FindType("Telerik.Sitefinity.Services.Search.ISearchService");
            if (serviceInterface == null)
            {
                warnings.Add("ISearchService type not found — this Sitefinity version may not ship the search service.");
                return null;
            }

            var serviceBus = FindType("Telerik.Sitefinity.Services.ServiceBus");
            if (serviceBus == null)
            {
                warnings.Add("ServiceBus type not found.");
                return null;
            }

            try
            {
                var resolve = serviceBus
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "ResolveService" && m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 0);

                if (resolve == null)
                {
                    warnings.Add("ServiceBus.ResolveService<T>() not found.");
                    return null;
                }

                return resolve.MakeGenericMethod(serviceInterface).Invoke(null, null);
            }
            catch (Exception ex)
            {
                warnings.Add("Could not resolve the search service: " + (ex.InnerException ?? ex).Message);
                return null;
            }
        }

        /// <summary>
        /// Builds an <c>ISearchQuery</c> against the advanced-settings catalog. The query object is an
        /// interface resolved through Sitefinity's <c>ObjectFactory</c> (the documented pattern — there
        /// is no public concrete query class), setting only the properties that exist on this version.
        /// </summary>
        private static object BuildSearchQuery(string text, int take, List<string> warnings)
        {
            var queryInterface = FindType("Telerik.Sitefinity.Services.Search.ISearchQuery");
            if (queryInterface == null)
            {
                warnings.Add("ISearchQuery type not found — cannot build a query on this Sitefinity version.");
                return null;
            }

            var objectFactory = FindType("Telerik.Sitefinity.Abstractions.ObjectFactory");
            if (objectFactory == null)
            {
                warnings.Add("ObjectFactory type not found.");
                return null;
            }

            object query;
            try
            {
                var resolve = objectFactory
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Resolve" && m.IsGenericMethodDefinition &&
                        m.GetParameters().Length == 0);

                if (resolve == null)
                {
                    warnings.Add("ObjectFactory.Resolve<T>() not found.");
                    return null;
                }

                query = resolve.MakeGenericMethod(queryInterface).Invoke(null, null);
            }
            catch (Exception ex)
            {
                warnings.Add("Could not resolve ISearchQuery: " + (ex.InnerException ?? ex).Message);
                return null;
            }

            TrySetProperty(query, "IndexName", IndexCatalogName);
            TrySetProperty(query, "Text", text);
            TrySetProperty(query, "Take", take);
            TrySetProperty(query, "Skip", 0);
            TrySetProperty(query, "OrderBy", new string[0]);

            // Field schema verified against a real advanced-settings-search Lucene segment (.fnm):
            // Title, Content, Summary, Link, Path, SectionName, FullSettingName, ContentType.
            var fields = new[] { "Title", "Content", "Summary", "SectionName", "FullSettingName" };
            TrySetProperty(query, "SearchFields", fields);
            TrySetProperty(query, "HighlightedFields", new[] { "Title", "Content" });

            // CRITICAL: the Lucene service compiles its query from SearchGroup, not Text — a query
            // with only Text set NREs inside the platform's query builder. Replicate Sitefinity's own
            // QueryBuilderBase.BuildQuery: an OR-group with one SearchTerm per field.
            if (!TryBuildSearchGroup(query, text, fields, warnings))
            {
                return null;
            }

            return query;
        }

        /// <summary>
        /// Populates <c>query.SearchGroup</c> the way Sitefinity's own <c>QueryBuilderBase.BuildQuery</c>
        /// does: a <c>SearchQueryGroup(QueryOperator.Or)</c> holding one <c>SearchTerm { Field, Value }</c>
        /// per search field. All three types are resolved reflectively
        /// (<c>Telerik.Sitefinity.Services.Search.*</c>) since they live in the version-variant search
        /// assemblies.
        /// </summary>
        private static bool TryBuildSearchGroup(object query, string text, string[] fields, List<string> warnings)
        {
            var groupType = FindType("Telerik.Sitefinity.Services.Search.SearchQueryGroup");
            var termType = FindType("Telerik.Sitefinity.Services.Search.SearchTerm");
            var operatorType = FindType("Telerik.Sitefinity.Services.Search.QueryOperator");

            if (groupType == null || termType == null || operatorType == null)
            {
                warnings.Add("SearchQueryGroup/SearchTerm/QueryOperator types not found — cannot build a query on this Sitefinity version.");
                return false;
            }

            try
            {
                var orValue = Enum.Parse(operatorType, "Or");
                var group = Activator.CreateInstance(groupType, orValue);

                var addTerm = groupType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == "AddTerm" && m.GetParameters().Length == 1);

                if (addTerm == null)
                {
                    warnings.Add("SearchQueryGroup.AddTerm not found.");
                    return false;
                }

                foreach (var field in fields)
                {
                    var term = Activator.CreateInstance(termType);
                    TrySetProperty(term, "Field", field);
                    TrySetProperty(term, "Value", text);
                    addTerm.Invoke(group, new[] { term });
                }

                TrySetProperty(query, "SearchGroup", group);
                return true;
            }
            catch (Exception ex)
            {
                warnings.Add("Could not build the search group: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        /// <summary>
        /// Invokes the first Search overload the service exposes that accepts our query — elevated.
        /// SearchConfig's <c>enableFilterByViewPermissions</c> makes the Lucene service trim results to
        /// what the CURRENT identity may view; an MCP request is anonymous (the API key is not a
        /// Sitefinity user), so an un-elevated search silently returns zero hits. The admin UI only works
        /// because a backend user is logged in. <c>SystemManager.RunWithElevatedPrivilege</c> is the
        /// platform's own answer to that; when unavailable, fall back to a direct (un-elevated) call.
        /// </summary>
        private static IEnumerable InvokeSearch(object service, object query)
        {
            var method = service.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Search" && m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType.IsInstanceOfType(query));

            if (method == null)
            {
                return null;
            }

            var runner = new ElevatedSearchRunner(service, method, query);

            if (runner.TryRunElevated())
            {
                return runner.Result;
            }

            return method.Invoke(service, new[] { query }) as IEnumerable;
        }

        /// <summary>
        /// Executes one search call inside <c>SystemManager.RunWithElevatedPrivilege</c>, resolved
        /// reflectively so this file compiles against any Sitefinity version. Failures rethrow the
        /// original exception (unwrapped) so the caller's variant loop sees the real error.
        /// </summary>
        private sealed class ElevatedSearchRunner
        {
            private readonly object _service;
            private readonly MethodInfo _method;
            private readonly object _query;

            public ElevatedSearchRunner(object service, MethodInfo method, object query)
            {
                this._service = service;
                this._method = method;
                this._query = query;
            }

            public IEnumerable Result { get; private set; }

            /// <summary>The delegate body — signature matches RunWithElevatedPrivilegeDelegate(params object[]).</summary>
            public void Run(object[] args)
            {
                this.Result = this._method.Invoke(this._service, new[] { this._query }) as IEnumerable;
            }

            public bool TryRunElevated()
            {
                var systemManager = FindType("Telerik.Sitefinity.Services.SystemManager");
                if (systemManager == null)
                {
                    return false;
                }

                try
                {
                    var run = systemManager
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "RunWithElevatedPrivilege" && m.GetParameters().Length == 2);

                    if (run == null)
                    {
                        return false;
                    }

                    var delegateType = run.GetParameters()[0].ParameterType;
                    var del = Delegate.CreateDelegate(delegateType, this, nameof(this.Run));

                    run.Invoke(null, new object[] { del, new object[0] });
                    return true;
                }
                catch (TargetInvocationException tie)
                {
                    // Surface the search's own failure, not the reflection wrapper.
                    throw tie.InnerException ?? tie;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        // ── Result mapping ───────────────────────────────────────────

        /// <summary>
        /// Maps a result document to name/value pairs without assuming this version's field schema.
        /// Documents expose <c>IField</c>s with Name/Value; anything unreadable is skipped. Values are
        /// redacted — the settings index stores config values, which can include credentials.
        /// </summary>
        private static McpSettingsSearchResult MapDocument(object doc)
        {
            var result = new McpSettingsSearchResult();

            var fields = ReadProperty(doc, "Fields") as IEnumerable;
            if (fields != null)
            {
                foreach (var field in fields)
                {
                    if (field == null)
                    {
                        continue;
                    }

                    var name = ReadProperty(field, "Name") as string;
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var rawValue = ReadProperty(field, "Value");
                    var value = rawValue == null ? string.Empty : Flatten(rawValue);

                    var scrubbed = McpSecretRedactor.IsDeniedKey(name)
                        ? McpSecretRedactor.Placeholder
                        : McpSecretRedactor.Redact(value);

                    result.Fields[name] = scrubbed;
                }
            }

            // Promote the fields that make a result scannable, wherever this version stores them.
            // FullSettingName is the value the admin UI itself navigates with — the closest thing the
            // index has to a breadcrumb identity for the setting.
            result.Title = FirstOf(result.Fields, "Title", "title");
            result.Path = FirstOf(result.Fields, "FullSettingName", "Path", "Link", "path");
            result.Section = FirstOf(result.Fields, "SectionName", "Section", "ContentType");

            return result;
        }

        private static string FirstOf(Dictionary<string, string> fields, params string[] names)
        {
            foreach (var name in names)
            {
                string value;
                if (fields.TryGetValue(name, out value) && !string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
        }

        /// <summary>Renders a field value (possibly an array of terms) as a single display string.</summary>
        private static string Flatten(object value)
        {
            if (value is string s)
            {
                return s;
            }

            if (value is IEnumerable e)
            {
                return string.Join(", ", e.Cast<object>().Where(o => o != null).Select(o => o.ToString()));
            }

            return value.ToString();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try
                {
                    t = asm.GetType(fullName, false);
                }
                catch (Exception)
                {
                }

                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static void TrySetProperty(object target, string name, object value)
        {
            try
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(target, value, null);
                }
            }
            catch (Exception)
            {
            }
        }

        private static object ReadProperty(object target, string name)
        {
            try
            {
                var prop = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return prop == null ? null : prop.GetValue(target, null);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}

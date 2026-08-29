// ============================================================================
// SitefinityCommunity.Mcp - Sitefinity Plugin
// Drop this file into your Sitefinity web app project.
// ============================================================================

using ServiceStack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Script.Serialization;
using System.Web.UI;
using Telerik.Sitefinity.Forms.Model;
using Telerik.Sitefinity.Modules.Forms;
using Telerik.Sitefinity.Modules.Forms.Web.UI.Fields;

namespace SitefinityCommunity.Mcp.SitefinityPlugin
{
    /// <summary>
    /// Forms + Form Responses endpoints. Field values in responses are scrubbed via McpSecretRedactor
    /// if a field name looks sensitive (password, secret, apikey, etc.).
    /// </summary>
    [McpApiKey]
    public class McpFormsService : Service
    {
        private const int DefaultTake = 50;
        private const int MaxTake = 500;

        /// <summary>
        /// GET /RestApi/mcp/forms — all forms with metadata counts.
        /// </summary>
        public McpFormsResponse Get(ListForms request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Forms);

            var response = new McpFormsResponse();

            try
            {
                var manager = FormsManager.GetManager();
                var forms = manager.GetForms().ToList();

                foreach (var form in forms)
                {
                    try
                    {
                        var entryCount = 0;
                        try
                        {
                            entryCount = manager.GetFormEntries(form).Count();
                        }
                        catch (Exception) { /* some forms have no entry type bound */ }

                        response.Forms.Add(new McpFormInfo
                        {
                            Id = form.Id.ToString(),
                            Name = form.Name ?? string.Empty,
                            Title = form.Title != null ? form.Title.ToString() : (form.Name ?? string.Empty),
                            Description = form.Description != null ? form.Description.ToString() : string.Empty,
                            IsPublished = TryReadIsPublished(form),
                            FieldCount = form.Controls != null ? form.Controls.Count : 0,
                            EntryCount = entryCount,
                            LastModified = form.LastModified,
                        });
                    }
                    catch (Exception ex)
                    {
                        response.Warnings.Add("Skipped form " + form.Id + ": " + ex.Message);
                    }
                }

                response.Forms = response.Forms.OrderBy(f => f.Title).ToList();
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error listing forms: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/forms/{FormIdentifier}/fields — field definitions for one form.
        /// </summary>
        public McpFormFieldsResponse Get(GetFormFields request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Forms);

            if (string.IsNullOrWhiteSpace(request.FormIdentifier))
            {
                throw HttpError.BadRequest("FormIdentifier is required.");
            }

            var response = new McpFormFieldsResponse();
            var manager = FormsManager.GetManager();

            var form = ResolveForm(manager, request.FormIdentifier);
            if (form == null)
            {
                throw HttpError.NotFound("Form not found: " + request.FormIdentifier);
            }

            response.FormId = form.Id.ToString();
            response.FormName = form.Name ?? string.Empty;
            response.FormTitle = form.Title != null ? form.Title.ToString() : (form.Name ?? string.Empty);

            if (form.Controls == null)
            {
                return response;
            }

            // Optional diagnostic dump — lets the caller see the exact Properties tree
            // that Sitefinity is returning on their version. Populated before the field
            // parse so we capture the raw state even if parsing throws mid-way.
            if (request.Debug)
            {
                try
                {
                    response.DebugDump = BuildPropertiesDump(form);
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Debug dump failed: " + ex.Message);
                }
            }

            foreach (var control in form.Controls)
            {
                try
                {
                    // Field metadata on modern Sitefinity MVC forms lives deep in the ControlProperty
                    // tree at Settings → Model → MetaField. Legacy WebForms forms stored the same keys
                    // at MetaField.* (top-level). We try the MVC path first and fall back to the
                    // legacy path so both generations of forms work.
                    var name = GetNested(control, "Settings.Model.MetaField.FieldName", "MetaField.FieldName", "Name");
                    var title = GetNested(control, "Settings.Model.MetaField.Title", "MetaField.Title", "Title");

                    // Required state — modern path is Settings.Model.ValidatorDefinition.Required,
                    // legacy is MetaField.IsRequired. Strings stored in Sitefinity are case-inconsistent
                    // ("True" / "true"), so compare OrdinalIgnoreCase.
                    var requiredRaw = GetNested(control,
                        "Settings.Model.ValidatorDefinition.Required",
                        "Settings.Model.MetaField.IsRequired",
                        "MetaField.IsRequired");

                    var field = new McpFormFieldInfo
                    {
                        Id = control.Id.ToString(),
                        Name = name ?? string.Empty,
                        Title = title ?? string.Empty,
                        FieldType = GetFieldType(control),
                        IsRequired = string.Equals(requiredRaw, "True", StringComparison.OrdinalIgnoreCase),
                        PlaceHolder = GetNested(control,
                            "Settings.Model.PlaceholderText",
                            "Settings.Model.Placeholder",
                            "PlaceholderText",
                            "Placeholder") ?? string.Empty,
                        DefaultValue = GetNested(control,
                            "Settings.Model.MetaField.DefaultValue",
                            "MetaField.DefaultValue",
                            "Value") ?? string.Empty,
                    };

                    var choicesRaw = GetNested(control,
                        "Settings.Model.Choices",
                        "Settings.Model.MetaField.Choices",
                        "Choices");
                    if (!string.IsNullOrEmpty(choicesRaw))
                    {
                        foreach (var line in choicesRaw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = line.Trim();

                            if (trimmed.Length > 0)
                            {
                                field.Choices.Add(trimmed);
                            }
                        }
                    }

                    response.Fields.Add(field);
                }
                catch (Exception ex)
                {
                    response.Warnings.Add("Skipped control " + control.Id + ": " + ex.Message);
                }
            }

            return response;
        }

        /// <summary>
        /// GET /RestApi/mcp/forms/{FormIdentifier}/responses — paged form submissions (redacted).
        /// </summary>
        public McpFormResponsesResponse Get(ListFormResponses request)
        {
            McpCapabilities.EnsureEnabled(McpCapabilities.Forms);

            if (string.IsNullOrWhiteSpace(request.FormIdentifier))
            {
                throw HttpError.BadRequest("FormIdentifier is required.");
            }

            var take = request.Take > 0 ? Math.Min(request.Take, MaxTake) : DefaultTake;
            var skip = request.Skip > 0 ? request.Skip : 0;
            var searchTerm = request.SearchTerm != null ? request.SearchTerm.Trim() : string.Empty;
            var hasSearch = !string.IsNullOrEmpty(searchTerm);

            var response = new McpFormResponsesResponse
            {
                Take = take,
                Skip = skip,
                SearchTerm = hasSearch ? searchTerm : null,
            };

            var manager = FormsManager.GetManager();
            var form = ResolveForm(manager, request.FormIdentifier);
            if (form == null)
            {
                throw HttpError.NotFound("Form not found: " + request.FormIdentifier);
            }

            response.FormId = form.Id.ToString();
            response.FormName = form.Name ?? string.Empty;

            try
            {
                // Resolve the list of developer field-names ONCE — it's the same for every entry
                // on this form, and extracting it per-entry was the old code's main cost.
                var fieldNames = new List<string>();
                if (form.Controls != null)
                {
                    foreach (var control in form.Controls)
                    {
                        var fn = GetNested(control,
                            "Settings.Model.MetaField.FieldName",
                            "MetaField.FieldName",
                            "Name");

                        if (!string.IsNullOrEmpty(fn) && !fieldNames.Contains(fn))
                        {
                            fieldNames.Add(fn);
                        }
                    }
                }

                // Order newest-first so Skip/Take pagination is deterministic. DateCreated alone is
                // not a total order (bulk imports share a timestamp), so tie-break on Id — otherwise
                // the provider's OFFSET/FETCH can shift rows across page boundaries between calls.
                var query = manager.GetFormEntries(form)
                    .OrderByDescending(e => e.DateCreated)
                    .ThenByDescending(e => e.Id);

                if (!hasSearch)
                {
                    // No search term — push paging down to the provider so we read only one page of
                    // rows, not the whole table. TotalCount comes from a SQL COUNT, and with no filter
                    // MatchedCount equals it.
                    response.TotalCount = query.Count();
                    response.MatchedCount = response.TotalCount;

                    foreach (var entry in query.Skip(skip).Take(take))
                    {
                        response.Responses.Add(BuildResponseInfo(entry, fieldNames));
                    }
                }
                else
                {
                    // Search must run against *redacted* values (so sensitive fields can't be
                    // discovered via search), which can't be expressed in SQL — so we materialize,
                    // redact, then match. TotalCount is all entries; MatchedCount is the post-filter
                    // total; the window [skip, skip+take) applies to the matched set.
                    var entries = query.ToList();
                    response.TotalCount = entries.Count;

                    var matchedCount = 0;
                    foreach (var entry in entries)
                    {
                        var info = BuildResponseInfo(entry, fieldNames);

                        if (!InfoMatchesSearch(info, searchTerm))
                        {
                            continue;
                        }

                        // This entry counts as a match — decide whether it falls in the paging window.
                        if (matchedCount >= skip && response.Responses.Count < take)
                        {
                            response.Responses.Add(info);
                        }

                        matchedCount++;
                    }

                    response.MatchedCount = matchedCount;
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Error reading form responses: " + ex.Message);
            }

            return response;
        }

        /// <summary>
        /// Builds a single <see cref="McpFormResponseInfo"/> for a form entry, reading each
        /// declared field value off the entry, applying redaction, and truncating long values
        /// to 2000 chars. Redaction happens *before* the value is added so any downstream
        /// consumer — including the search matcher — sees the sanitized text.
        /// </summary>
        private static McpFormResponseInfo BuildResponseInfo(FormEntry entry, List<string> fieldNames)
        {
            var info = new McpFormResponseInfo
            {
                Id = entry.Id.ToString(),
                SubmittedOn = entry.DateCreated,
                IpAddress = TryGetEntryString(entry, "IpAddress"),
                UserAgent = TryGetEntryString(entry, "UserAgent"),
            };

            if (fieldNames == null)
            {
                return info;
            }

            foreach (var fieldName in fieldNames)
            {
                try
                {
                    var value = GetEntryValue(entry, fieldName);
                    var text = value == null ? string.Empty : value.ToString();

                    if (McpSecretRedactor.IsDeniedKey(fieldName))
                    {
                        text = McpSecretRedactor.Placeholder;
                    }
                    else
                    {
                        text = McpSecretRedactor.Redact(text);
                    }

                    if (text.Length > 2000)
                    {
                        text = text.Substring(0, 2000) + "... (truncated)";
                    }

                    info.Values[fieldName] = text;
                }
                catch (Exception) { /* skip individual field errors */ }
            }

            return info;
        }

        /// <summary>
        /// Case-insensitive substring search across every field value on a response, plus IP
        /// and UserAgent. Run against *redacted* values so sensitive fields can never be
        /// discovered via search.
        /// </summary>
        private static bool InfoMatchesSearch(McpFormResponseInfo info, string term)
        {
            if (info == null || string.IsNullOrEmpty(term))
            {
                return false;
            }

            var comparison = StringComparison.OrdinalIgnoreCase;

            if (!string.IsNullOrEmpty(info.IpAddress) && info.IpAddress.IndexOf(term, comparison) >= 0)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(info.UserAgent) && info.UserAgent.IndexOf(term, comparison) >= 0)
            {
                return true;
            }

            if (info.Values != null)
            {
                foreach (var kvp in info.Values)
                {
                    if (!string.IsNullOrEmpty(kvp.Value) && kvp.Value.IndexOf(term, comparison) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private FormDescription ResolveForm(FormsManager manager, string identifier)
        {
            Guid formGuid;
            if (Guid.TryParse(identifier, out formGuid))
            {
                try
                {
                    var byId = manager.GetForm(formGuid);

                    if (byId != null)
                    {
                        return byId;
                    }
                }
                catch (Exception) { /* not by id */ }
            }

            var forms = manager.GetForms().ToList();
            return forms.FirstOrDefault(f =>
                    string.Equals(f.Name, identifier, StringComparison.OrdinalIgnoreCase))
                ?? forms.FirstOrDefault(f =>
                    f.Title != null && string.Equals(f.Title.ToString(), identifier, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Tries each dotted path against the control in order and returns the first non-empty
        /// value. Used to express "new MVC path first, legacy WebForms path second" lookup chains
        /// in a single call so the field-extraction code stays readable.
        /// </summary>
        private static string GetNested(FormControl control, params string[] paths)
        {
            if (paths == null)
            {
                return null;
            }

            foreach (var path in paths)
            {
                var value = GetPropertyValue(control, path);
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static readonly string[] ModelPropertyNames = { "Model", "Settings" };

        /// <summary>
        /// Reads a form-control property value. Form widget settings are stored as a tree of
        /// <c>ControlProperty</c> entities — flat scalars at the top (Name, Title, ControllerName, Model)
        /// plus composite properties whose own <c>ChildProperties</c> collection holds nested values.
        /// The canonical example is `MetaField`, which holds `FieldName`, `Title`, `IsRequired`,
        /// `DefaultValue`, etc. as child properties — NOT as a flat `MetaField.FieldName` key.
        ///
        /// Lookup order for a dotted path like `MetaField.FieldName`:
        ///   1. Direct flat property match (rare, but some configs emit `MetaField.FieldName` literally).
        ///   2. ChildProperties traversal — find `MetaField`, then walk its children to `FieldName`.
        ///   3. JSON fallback — some MVC widgets store their full model as a serialized `Model`
        ///      blob; parse it and navigate the dotted path there.
        /// Reflection is used on `ChildProperties` / `Name` / `Value` so the code is agnostic to
        /// the exact `ControlProperty` type location across Sitefinity versions.
        /// </summary>
        private static string GetPropertyValue(FormControl control, string name)
        {
            if (control == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            try
            {
                if (control.Properties == null)
                {
                    return null;
                }

                // 1. Direct flat property match (covers legacy keys and simple scalars)
                foreach (var prop in control.Properties)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value ?? string.Empty;
                    }
                }

                // 2. ChildProperties traversal — "MetaField.FieldName" finds the MetaField
                // property then walks its child collection for "FieldName". Handles arbitrary
                // nesting depth.
                var parts = name.Split('.');
                if (parts.Length > 1)
                {
                    object cursor = null;
                    foreach (var prop in control.Properties)
                    {
                        if (string.Equals(prop.Name, parts[0], StringComparison.OrdinalIgnoreCase))
                        {
                            cursor = prop;
                            break;
                        }
                    }

                    for (int i = 1; cursor != null && i < parts.Length; i++)
                    {
                        cursor = FindChildProperty(cursor, parts[i]);
                    }

                    if (cursor != null)
                    {
                        var valueProp = cursor.GetType().GetProperty("Value");
                        if (valueProp != null)
                        {
                            var resolved = valueProp.GetValue(cursor, null) as string;
                            if (!string.IsNullOrEmpty(resolved))
                            {
                                return resolved;
                            }
                        }
                    }
                }

                // 3. Model / Settings JSON fallback — some MVC field widgets serialize their full
                // model into a single property instead of (or in addition to) the ChildProperties tree.
                string modelJson = null;
                foreach (var modelPropName in ModelPropertyNames)
                {
                    foreach (var prop in control.Properties)
                    {
                        if (string.Equals(prop.Name, modelPropName, StringComparison.OrdinalIgnoreCase))
                        {
                            modelJson = prop.Value;
                            break;
                        }
                    }

                    if (!string.IsNullOrEmpty(modelJson))
                    {
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(modelJson))
                {
                    return NavigateJsonPath(modelJson, name);
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// Produces a human-readable dump of every FormControl on a form — ObjectType,
        /// ControllerName, and the full Properties + ChildProperties tree (recursive).
        /// Values longer than 400 chars are truncated and any `Model`/`Settings` JSON blob
        /// is pretty-printed inline. This is diagnostic output intended for copy-paste into
        /// a bug report when Name/Title extraction isn't working on a given Sitefinity build.
        /// </summary>
        private static string BuildPropertiesDump(FormDescription form)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Form Properties Dump ===");
            sb.AppendLine("FormId: " + form.Id);
            sb.AppendLine("FormName: " + (form.Name ?? ""));
            sb.AppendLine("Controls: " + (form.Controls != null ? form.Controls.Count : 0));
            sb.AppendLine();

            if (form.Controls == null)
            {
                return sb.ToString();
            }

            int idx = 0;
            foreach (var control in form.Controls)
            {
                idx++;
                sb.AppendLine("── Control " + idx + " ──");
                sb.AppendLine("  Id:         " + control.Id);
                sb.AppendLine("  Caption:    " + (control.Caption ?? ""));
                sb.AppendLine("  ObjectType: " + (control.ObjectType ?? ""));
                sb.AppendLine("  Properties: " + (control.Properties != null ? control.Properties.Count : 0));

                if (control.Properties != null)
                {
                    foreach (var prop in control.Properties)
                    {
                        DumpProperty(sb, prop, 1);
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Recursively walks a ControlProperty and its ChildProperties collection,
        /// indenting by depth. Uses reflection on `ChildProperties` so it works regardless
        /// of which assembly/namespace the Sitefinity version uses for ControlProperty.
        /// </summary>
        private static void DumpProperty(System.Text.StringBuilder sb, object prop, int depth)
        {
            if (prop == null)
            {
                return;
            }

            var indent = new string(' ', depth * 2);

            string propName = null;
            string propValue = null;
            try
            {
                var t = prop.GetType();
                var nameProp = t.GetProperty("Name");
                var valueProp = t.GetProperty("Value");
                propName = nameProp != null ? nameProp.GetValue(prop, null) as string : null;
                propValue = valueProp != null ? valueProp.GetValue(prop, null) as string : null;
            }
            catch (Exception) { /* best-effort */ }

            var displayValue = propValue == null ? "(null)" : propValue;
            if (displayValue.Length > 400)
            {
                displayValue = displayValue.Substring(0, 400) + "... (truncated, " + propValue.Length + " chars total)";
            }

            // Escape newlines so the dump stays readable on one line per property
            displayValue = displayValue.Replace("\r", "\\r").Replace("\n", "\\n");

            sb.AppendLine(indent + "- " + (propName ?? "(unnamed)") + " = " + displayValue);

            // Recurse into ChildProperties
            try
            {
                var childrenProp = prop.GetType().GetProperty("ChildProperties");
                if (childrenProp == null)
                {
                    return;
                }

                var children = childrenProp.GetValue(prop, null) as System.Collections.IEnumerable;
                if (children == null)
                {
                    return;
                }

                foreach (var child in children)
                {
                    DumpProperty(sb, child, depth + 1);
                }
            }
            catch (Exception) { /* ignore — dump is best-effort */ }
        }

        /// <summary>
        /// Reflects `ChildProperties` off a ControlProperty and returns the child whose `Name`
        /// matches (case-insensitive). Returns null if either side of the traversal is missing —
        /// callers are expected to bail out gracefully.
        /// </summary>
        private static object FindChildProperty(object parentProperty, string childName)
        {
            if (parentProperty == null || string.IsNullOrEmpty(childName))
            {
                return null;
            }

            try
            {
                var childrenProp = parentProperty.GetType().GetProperty("ChildProperties");
                if (childrenProp == null)
                {
                    return null;
                }

                var children = childrenProp.GetValue(parentProperty, null) as System.Collections.IEnumerable;
                if (children == null)
                {
                    return null;
                }

                foreach (var child in children)
                {
                    if (child == null)
                    {
                        continue;
                    }

                    var nameProp = child.GetType().GetProperty("Name");
                    var name = nameProp != null ? nameProp.GetValue(child, null) as string : null;

                    if (string.Equals(name, childName, StringComparison.OrdinalIgnoreCase))
                    {
                        return child;
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// Parses a JSON blob into a dictionary and navigates a dotted path to return a leaf
        /// value as a string. Returns null when any segment can't be resolved.
        /// </summary>
        private static string NavigateJsonPath(string json, string dottedPath)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(dottedPath))
            {
                return null;
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                var root = serializer.Deserialize<Dictionary<string, object>>(json);

                object current = root;
                var parts = dottedPath.Split('.');
                foreach (var part in parts)
                {
                    var dict = current as Dictionary<string, object>;
                    if (dict == null)
                    {
                        return null;
                    }

                    object value = null;
                    foreach (var kvp in dict)
                    {
                        if (string.Equals(kvp.Key, part, StringComparison.OrdinalIgnoreCase))
                        {
                            value = kvp.Value;
                            break;
                        }
                    }

                    if (value == null)
                    {
                        return null;
                    }

                    current = value;
                }

                return current == null ? null : current.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Derives the field's concrete type. MVC form fields all wrap the same MvcControllerProxy,
        /// so their `ObjectType` is useless — the real type is in the `ControllerName` property
        /// (e.g. `Telerik.Sitefinity.Frontend.Forms.Mvc.Controllers.TextFieldController` → `TextField`).
        /// </summary>
        private static string GetFieldType(FormControl control)
        {
            var objectType = control != null ? (control.ObjectType ?? string.Empty) : string.Empty;

            // Any proxy-based widget — use ControllerName instead
            if (objectType.IndexOf("MvcControllerProxy", StringComparison.OrdinalIgnoreCase) >= 0
                || objectType.IndexOf("MvcWidgetProxy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var controllerName = GetPropertyValue(control, "ControllerName");
                if (!string.IsNullOrEmpty(controllerName))
                {
                    var simple = ExtractFieldType(controllerName);

                    if (simple.EndsWith("Controller", StringComparison.Ordinal))
                    {
                        simple = simple.Substring(0, simple.Length - "Controller".Length);
                    }

                    return simple;
                }
            }

            return ExtractFieldType(objectType);
        }

        private static string ExtractFieldType(string objectType)
        {
            if (string.IsNullOrEmpty(objectType))
            {
                return string.Empty;
            }

            var lastDot = objectType.LastIndexOf('.');
            return lastDot >= 0 ? objectType.Substring(lastDot + 1) : objectType;
        }

        private static string TryGetEntryString(FormEntry entry, string name)
        {
            try
            {
                var val = GetEntryValue(entry, name);
                return val == null ? string.Empty : val.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Reads a dynamic field off a FormEntry without colliding with ServiceStack's
        /// extension-method overload of GetValue. Routes through Telerik's IDataItem contract,
        /// falling back to reflection on public instance properties for versions that don't
        /// expose GetValue there.
        /// </summary>
        private static object GetEntryValue(FormEntry entry, string fieldName)
        {
            if (entry == null || string.IsNullOrEmpty(fieldName))
            {
                return null;
            }

            // Invoke GetValue via reflection to sidestep ServiceStack's
            // AppMetadataUtils.GetValue<T>(MetadataPropertyType, string) extension-method
            // collision, which shadows DataItemBase.GetValue(string) in this compilation unit.
            try
            {
                var t = entry.GetType();
                for (var cur = t; cur != null; cur = cur.BaseType)
                {
                    var method = cur.GetMethod("GetValue",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly,
                        null, new[] { typeof(string) }, null);

                    if (method != null)
                    {
                        return method.Invoke(entry, new object[] { fieldName });
                    }
                }
            }
            catch (Exception) { /* fall through to property probe */ }

            try
            {
                var prop = entry.GetType().GetProperty(fieldName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);

                if (prop != null)
                {
                    return prop.GetValue(entry, null);
                }
            }
            catch (Exception) { /* best-effort */ }

            return null;
        }

        /// <summary>
        /// FormDescription exposes publishing state under different property names across
        /// Sitefinity versions (ApprovalWorkflowState, Status, etc.) — probe via reflection
        /// instead of binding to one.
        /// </summary>
        private static bool TryReadIsPublished(FormDescription form)
        {
            if (form == null)
            {
                return false;
            }

            try
            {
                var t = form.GetType();
                var awsProp = t.GetProperty("ApprovalWorkflowState");

                if (awsProp != null)
                {
                    var aws = awsProp.GetValue(form, null);

                    if (aws != null)
                    {
                        var valueProp = aws.GetType().GetProperty("Value");
                        var state = valueProp != null ? valueProp.GetValue(aws, null) as string : aws.ToString();

                        if (!string.IsNullOrEmpty(state))
                        {
                            return string.Equals(state, "Published", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }

                var statusProp = t.GetProperty("Status");
                if (statusProp != null)
                {
                    var status = statusProp.GetValue(form, null);
                    if (status != null)
                    {
                        return string.Equals(status.ToString(), "Live", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception) { /* best-effort */ }
            return false;
        }
    }
}

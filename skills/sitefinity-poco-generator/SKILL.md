---
name: sitefinity-poco-generator
description: Use this skill when the user needs a strongly-typed C# POCO class generated from a Sitefinity Module Builder dynamic content type. Produces a class that can be serialized to / deserialized from the Sitefinity Web Services API and offers both shallow (Id-only) and full (eager-loaded) hydration via constructors. Covers related-type expansion, taxonomy/classification fields, and multilingual fields.
---

You are a Sitefinity POCO generator. Your job: take the output of the `sitefinity_get_module_structure` MCP tool and produce a complete, compilable C# class (or set of classes for nested types) that round-trips cleanly with Sitefinity's Web Services API.

## Workflow

1. **Always list modules first.** Call `sitefinity_list_dynamic_types` before anything else. This gives you the authoritative list of modules and types available on the target environment. Cache the result for the rest of the session — don't call it again unless the user changes environment.
2. **Resolve the user's module name against that list.** Do case-insensitive matching on both the module title (e.g. `"Session"`) and the CLR type name (e.g. `"Sessions"` / full `Telerik.Sitefinity.DynamicTypes.Model.Sessions.Session`):
   - **Exact match (1 result):** use it, echo back `"Using module: <ModuleName>"` so the user can catch a mistake.
   - **Multiple matches:** stop and ask. Show each match as a numbered list with the module title, top-level type name, and field count. Example:
     ```
     Multiple modules match "session":
       1. Session        (CLR: Telerik.Sitefinity.DynamicTypes.Model.Sessions.Session, 14 fields)
       2. SessionNotes   (CLR: Telerik.Sitefinity.DynamicTypes.Model.SessionNotes.SessionNote, 6 fields)
     Which one?
     ```
   - **No match:** show the 3–5 closest candidates from the list (Levenshtein or substring match) and ask the user to pick or re-spell. Do not guess.
   - **User gave no module name yet:** print a short summary of the first ~20 modules and ask which one.
3. **Ask where to save the files.** Prompt the user for a target folder (absolute or relative to repo root). If the folder doesn't exist, offer to create it. Suggest a default like `Models/Sitefinity/{ModuleName}/` if the user is unsure.
4. **Locate the `.csproj`** in or above the target folder (walk up the tree). Inspect the first line to decide whether it's SDK-style (starts with `<Project Sdk="Microsoft.NET.Sdk...">`) or legacy (starts with `<?xml ...>` + `<Project ToolsVersion="...">`).
5. **Call `sitefinity_get_module_structure <ResolvedModuleName>`** using the name you confirmed in step 2 — this returns every type in the module nested by parent/child with fields + CLR type hints. If the module has 8 nested types, you will generate 8 classes.
6. **Confirm the plan with the user** before writing: number of classes, target folder, whether the shared helpers (`SitefinityJson`, `SitefinityImage`, `SitefinityAddress`) already exist and should be skipped.
7. **Write one `.cs` file per type** into the target folder, preserving the parent/child relationship as a child collection property on the parent.
8. **Update the `.csproj`** if it's legacy — add a `<Compile Include="..."/>` entry per new file into an `<ItemGroup>`. If SDK-style, skip this step (files are auto-included via the implicit compile glob).
9. **Emit the companion `SitefinityApiClient`** (or extend an existing one) only if the user asks for "fully working hydration code" — otherwise just the POCOs.

Do not invent fields that weren't in the tool output. If the tool shows `(no fields)` on a type, emit an empty class with a comment noting fields weren't discoverable — do not guess.

## Class Shape

Use this template for every generated class:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace {Namespace};

/// <summary>
/// POCO for the {TypeName} dynamic type.
/// Source CLR: {TypeFullName}
/// </summary>
public sealed class {TypeName}
{
    // ── Identity ────────────────────────────────────────────────
    [JsonPropertyName("Id")]
    public Guid Id { get; set; }

    // ── Fields (from sitefinity_get_module_structure) ───────────
    // For each field in the tool output, emit a property named exactly
    // as the `Name` reported by the tool and typed per its `ClrType` hint.
    // See "Field → property mapping" below.

    // ── Child types (nested hierarchy) ──────────────────────────
    // For every child type reported by the tool, emit a collection:
    // public IList<{ChildTypeName}> {ChildCollectionName} { get; set; } = new List<{ChildTypeName}>();

    // ── Hydration ───────────────────────────────────────────────
    public {TypeName}() { }

    /// <summary>Shallow: Id only. Remaining properties remain default. Use when
    /// you only need to reference this item (e.g. for a relation) and don't want
    /// the round trip cost of a full fetch.</summary>
    public {TypeName}(Guid id) { this.Id = id; }

    /// <summary>Full: populate every property from a Sitefinity REST JSON envelope.
    /// Accepts the object Sitefinity returns under the top-level `Item` or `Items[n]`
    /// key — pass that dictionary in directly.</summary>
    public {TypeName}(IDictionary<string, object> json)
    {
        if (json == null) return;
        if (json.TryGetValue("Id", out var id)) this.Id = Guid.Parse(id.ToString());
        // ... one assignment per field, using SitefinityJson helpers below
    }
}
```

## Field → property mapping

Use the `ClrType` reported by the tool as the starting point. If `ClrType` is missing, fall back to the UI name in `FieldType` with this table:

| MCP `FieldType` / `ClrType` | C# property type                                   | Notes                                  |
|------------------------------|----------------------------------------------------|----------------------------------------|
| `ShortText`, `LongText`, `string` | `string`                                      | Trailing whitespace preserved.         |
| `YesNo`, `bool`              | `bool`                                             |                                        |
| `Number`, `decimal?`         | `decimal?`                                         | Sitefinity allows blank number fields. |
| `DateTime`, `DateTime?`      | `DateTime?`                                        | API emits either ISO 8601 or `/Date(ms)/`. Use `SitefinityJson.ParseDate`. |
| `Multilingual`, `Lstring`    | `Dictionary<string,string>` keyed by culture code  | Key examples: `en`, `es-ES`.           |
| `Choices`                    | `string` (single) or `IList<string>` (multi)       | Tool does not distinguish — default to `IList<string>` and document. |
| `Classification`             | `IList<Guid>` of taxon Ids                         | Resolve names via `sitefinity_list_taxonomies` if display needed. |
| `RelatedData`                | `IList<{RelatedDataType simple name}>`             | Use a POCO if you've generated one for the related type; fall back to `IList<Guid>` if you haven't. |
| `RelatedMedia`, `Multimedia` | `IList<SitefinityImage>`                           | See the shared image helper below.     |
| `Address`                    | `SitefinityAddress`                                | See the shared address helper below.   |

Mark a property nullable when the tool's output does NOT include `[required]`. Required fields get non-nullable types.

## Hydration via Sitefinity Web Services

Sitefinity exposes dynamic types at:

```
GET  /api/default/{collectionName}({id})            — single item
GET  /api/default/{collectionName}                  — collection, supports OData $filter, $top, $skip
POST /api/default/{collectionName}                  — create
PUT  /api/default/{collectionName}({id})            — update
DELETE /api/default/{collectionName}({id})          — delete
```

The `collectionName` follows Sitefinity's pluralization of the type name — confirm it via `sitefinity_list_api_routes`. Authentication is either cookie (authenticated admin) or bearer token from `/Sitefinity/Authenticate/OpenID/connect/token` depending on project setup.

### Generated `SitefinityApiClient` template (only on explicit request)

```csharp
public sealed class SitefinityApiClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SitefinityApiClient(HttpClient http, string baseUrl)
    {
        this._http = http;
        this._baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>Full hydration — fetches every property from the REST API.</summary>
    public async Task<T> GetByIdAsync<T>(string collectionName, Guid id, CancellationToken ct = default)
        where T : class, new()
    {
        var url = $"{this._baseUrl}/api/default/{collectionName}({id})";
        var response = await this._http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(cancellationToken: ct);
        if (envelope?.TryGetValue("Item", out var item) == true && item is IDictionary<string, object> dict)
        {
            return (T)Activator.CreateInstance(typeof(T), new object[] { dict });
        }
        return null;
    }

    /// <summary>Partial — populates a {Type} with just the Id for reference use.</summary>
    public static T Reference<T>(Guid id) where T : class
        => (T)Activator.CreateInstance(typeof(T), new object[] { id });
}
```

### Shared helpers to include once per namespace

```csharp
internal static class SitefinityJson
{
    // Sitefinity REST returns dates as either ISO 8601 or "/Date(1696608000000)/".
    private static readonly System.Text.RegularExpressions.Regex MsDate =
        new(@"^/Date\((-?\d+)(?:[+-]\d{4})?\)/$");

    public static DateTime? ParseDate(object raw)
    {
        if (raw == null) return null;
        var s = raw.ToString();
        if (string.IsNullOrEmpty(s)) return null;
        var m = MsDate.Match(s);
        if (m.Success) return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(m.Groups[1].Value)).UtcDateTime;
        return DateTime.TryParse(s, out var dt) ? dt : (DateTime?)null;
    }

    public static string GetString(IDictionary<string, object> d, string key)
        => d != null && d.TryGetValue(key, out var v) && v != null ? v.ToString() : null;

    public static bool GetBool(IDictionary<string, object> d, string key)
        => d != null && d.TryGetValue(key, out var v) && v != null && bool.TryParse(v.ToString(), out var b) && b;

    public static decimal? GetDecimal(IDictionary<string, object> d, string key)
        => d != null && d.TryGetValue(key, out var v) && v != null
            && decimal.TryParse(v.ToString(), out var dec) ? dec : (decimal?)null;

    public static Guid? GetGuid(IDictionary<string, object> d, string key)
        => d != null && d.TryGetValue(key, out var v) && v != null
            && Guid.TryParse(v.ToString(), out var g) ? g : (Guid?)null;

    public static IList<Guid> GetGuidList(IDictionary<string, object> d, string key)
    {
        var list = new List<Guid>();
        if (d != null && d.TryGetValue(key, out var v) && v is System.Collections.IEnumerable en)
            foreach (var x in en) if (Guid.TryParse(x?.ToString(), out var g)) list.Add(g);
        return list;
    }
}

public sealed class SitefinityImage
{
    public Guid Id { get; set; }
    public string Title { get; set; }
    public string Url { get; set; }
    public string AlternativeText { get; set; }
}

public sealed class SitefinityAddress
{
    public string Street { get; set; }
    public string City { get; set; }
    public string StateProvince { get; set; }
    public string Country { get; set; }
    public string Zip { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
```

## Project-file integration

When writing generated files:

### Legacy `.csproj` (most Sitefinity web-app projects)

These have explicit `<Compile Include="..."/>` entries. After writing each `.cs` file, append it to the project's main `<ItemGroup>` that contains other `<Compile>` elements. Use the repo-relative path with backslashes. Example insertion:

```xml
<ItemGroup>
  <Compile Include="Properties\AssemblyInfo.cs" />
  <!-- ... existing entries ... -->
  <Compile Include="Models\Sitefinity\Session\Session.cs" />
  <Compile Include="Models\Sitefinity\Session\Procomp.cs" />
  <Compile Include="Models\Sitefinity\Session\ProcompSessionDate.cs" />
</ItemGroup>
```

Rules:
- Preserve existing indentation (usually 2 spaces).
- Insert entries in alphabetical order within the same `<ItemGroup>` if the existing entries are sorted; otherwise append at the end.
- **Never** add to an `<ItemGroup Condition="...">` block — use the unconditional one.
- If multiple `<ItemGroup>`s contain `<Compile>` elements, use the one with the most entries.
- Before writing, grep the csproj to confirm the file isn't already listed — skip duplicates.

### SDK-style `.csproj`

Detectable by the first line: `<Project Sdk="Microsoft.NET.Sdk">` (or similar). These use an implicit `**/*.cs` compile glob, so no csproj edit is needed — dropping the file in the folder is enough. Tell the user this skipped the csproj step so they're not surprised.

## Parent/child hierarchy handling

If `sitefinity_get_module_structure` returns a tree like:

```
── Session (root)
  ── Procomps
    ── ProcompSessionDates
```

Generate three classes and wire the child collections:

```csharp
public sealed class Session {
    public Guid Id { get; set; }
    public string Title { get; set; }
    public IList<Procomp> Procomps { get; set; } = new List<Procomp>();
}

public sealed class Procomp {
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }  // Back-reference to Session.Id
    public string Title { get; set; }
    public IList<ProcompSessionDate> ProcompSessionDates { get; set; } = new List<ProcompSessionDate>();
}

public sealed class ProcompSessionDate {
    public Guid Id { get; set; }
    public Guid ParentId { get; set; }  // Back-reference to Procomp.Id
    public DateTime? Schedule { get; set; }
}
```

The child collection property name should match the MCP tool's reported type name (pluralized if it isn't already). Confirm the parent's back-reference property name via the REST API response — it's typically `ParentId` but can vary.

## What to output

When the user asks "generate a POCO for the Session module":

1. Call `sitefinity_list_dynamic_types` and match `"Session"` against the result. If it's unambiguous, continue; if not, follow the disambiguation rules in step 2 of the Workflow section above.
2. Call `sitefinity_get_module_structure <ResolvedName>` with the name confirmed in step 1.
3. Print a one-line plan: `I'll generate 8 classes for the Session module (Session + 7 nested types).`
4. Ask for the target folder if the user hasn't given one.
5. Emit one `.cs` file per type under the user-specified folder.
6. End with a summary of:
   - Module resolved to (in case disambiguation happened)
   - Classes generated and their file paths
   - Fields that were mapped ambiguously (e.g. `Choices` → default to `IList<string>`)
   - Fields that couldn't be mapped and the type you fell back to

When the user asks "and give me the API client code too":

7. Additionally emit `SitefinityApiClient.cs`, `SitefinityJson.cs`, `SitefinityImage.cs`, `SitefinityAddress.cs` — only if they don't already exist in the project.

## What NOT to do

- **Don't invent fields.** If the MCP output didn't list it, it doesn't exist on that type.
- **Don't generate a hand-written deserializer when `System.Text.Json` with `[JsonPropertyName]` will do.** The exception is date handling — always route `DateTime?` fields through `SitefinityJson.ParseDate` because Sitefinity can emit either format.
- **Don't skip the shallow constructor.** Even if the user only asks for full hydration, include it — it's essential when one POCO holds a collection of another POCO as references without wanting to fetch them all.
- **Don't assume pluralization.** Confirm collection names via `sitefinity_list_api_routes` before writing an HTTP call. Sitefinity's REST routing is not always `{typename}s`.
- **Don't silently swallow mapping ambiguities.** If `Choices` could be single or multi, say so in the summary so the user can adjust.

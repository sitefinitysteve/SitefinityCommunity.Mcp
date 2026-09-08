---
name: sitefinity-best-practices
description: Use this skill when starting ANY Sitefinity CMS task, when you are unsure which Sitefinity skill applies, when you need general Sitefinity MVC development guidance, when choosing a data-access approach for Sitefinity content, or when you need to verify which Sitefinity and .NET versions a project targets before writing code - it is the read-this-first foundation that establishes context, states the ground rules, and routes you to the right deep skill.
---

You are working on a classic Progress Sitefinity CMS site (.NET Framework 4.8, ASP.NET MVC / Feather-era widgets). This is the foundation skill: read it first, establish the environment's actual versions and shape, then follow the ground rules below and hand off to the specialized companion skill for the real work. It is deliberately short - a router and a rulebook, not an encyclopedia.

## First: establish context - never assume versions

Sitefinity spans a decade of releases and two entirely different rendering stacks. Guessing wastes time and produces wrong code. Before writing anything, pin down what the project actually is, in this order:

1. **If the SitefinityCommunity MCP server is connected**, ask the live site:
   - `sitefinity_get_site_info` - Sitefinity version, project name/paths, environment.
   - `sitefinity_list_modules` - which modules are installed (News, Events, Module Builder types present, etc.).
2. **Otherwise, read the assembly version off disk**:
   ```powershell
   (Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion   # e.g. 15.4.8636.0 -> Sitefinity 15.4
   ```
3. **Confirm the .NET target** in the `.csproj` (`<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>` or `<TargetFramework>net48</TargetFramework>`).

**Gotcha: these skills assume net48 + MVC/Feather widgets (`MvcControllerProxy`), NOT the Sitefinity ASP.NET Core renderer / decoupled ("Sitefinity Core") frontend.** Sitefinity has two live rendering stacks (WebForms is deprecated and only appears as legacy rows): the classic in-process MVC/Feather stack this skill set covers, and the ASP.NET Core Renderer, a separate .NET (Core) web app (`Progress.Sitefinity.Renderer` NuGet) that reads pages over the REST/OData layout API and renders its own widget model. Tell them apart before you touch anything: a Core page or template has a non-NULL `renderer` column on `sf_page_data` / `sf_page_templates` (the backend's own `AdminApp` value also appears there; classic pages are NULL), Core widget rows use different `object_type` values than `MvcControllerProxy`, and the solution contains a second web project referencing `Progress.Sitefinity.Renderer`. **There is no verified Core-renderer skill in this set yet** - do not apply the MVC widget, designer-attribute, page-controls, or page-surgery recipes to a Core page; say so and work from the official Core renderer docs instead. The two stacks are not interchangeable: if the project targets the Core renderer, the guidance here and in the companion skills does NOT transfer - call that out explicitly rather than applying MVC recipes to a Core site.

**Version-specific behavior is real.** Designer attribute availability grows release to release, SDK package versions differ, and lifecycle internals get reworked. When you ask a docs question or reason about an API, **state the version you are on** so the answer is scoped correctly. When in doubt about a specific attribute or overload, verify it exists in that project's assemblies before relying on it.

## Docs-first, verified-behavior-wins

Prefer authoritative sources over blog posts:

- **Official docs**: https://www.progress.com/documentation/sitefinity-cms
- **Official samples**: https://github.com/Sitefinity/feather-samples (classic MVC/Feather widgets - this stack). **Gotcha:** https://github.com/Sitefinity/sitefinity-aspnetcore-mvc-samples, despite the "mvc" in its name, targets the ASP.NET Core renderer - do not copy its patterns into a net48 MVC site.

The platform has many versions and the wider web is littered with stale, version-mismatched blog posts - treat random search results with suspicion. **Gotcha:** when the docs and what you actually observe on the running site disagree, trust the verified behavior, and say so plainly (note the doc, note the observation, proceed on the observation). The companion skills follow this rule - their claims were verified against real 15.4 assemblies and databases, not copied from docs.

## Data access decision guide

Choosing the wrong data layer is the most damaging early mistake. The rule:

- **Sitefinity-owned CMS data** - pages, content items (News, Events, Blogs), Module Builder dynamic types, taxonomies, users, media - goes through the **manager / OpenAccess-backed APIs**: `PageManager`, `DynamicModuleManager`, `ContentManager`, `TaxonomyManager`, `UserManager`, `LibrariesManager`, etc.
- **NEVER issue raw SQL writes against `sf_*` tables.** The schema does have foreign keys, but **only on OpenAccess structural tables** (subclass tables, taxonomy junctions, permission joins) - every relationship that matters (page -> page data -> drafts -> controls -> properties, master -> live copies, URLs, related data, permissions targets, Module Builder per-type -> `sf_dynamic_content`) is a bare column the OpenAccess ORM enforces at runtime, together with the content lifecycle (an item is a separate row per Master/Temp/Live state), version snapshots, and the L2 cache. A direct write bypasses all of it and silently corrupts caches, lifecycle state, and versioning. Reads against `sf_*` for diagnostics and inspection are fine (several companion skills give you verified SQL); writes are not. The verified column-by-column map is in `sitefinity-database-structure` (baseline: Sitefinity 15.4).
- **Your own custom databases** (application data that is not Sitefinity content) can use a **separate data layer side by side** - EF, Dapper, ADO.NET - pointed at your own tables. Sitefinity does not object; just keep it out of the `sf_*` schema.
- **Reading content over HTTP:** to expose or query existing CMS content as REST, decide read-vs-custom first - just **reading/querying** (list/filter/sort/page/expand) is zero-code through the built-in default OData service at `/api/default` (`sitefinity-odata-services`); **custom logic, writes, or bespoke shapes** call for a ServiceStack service (`sitefinity-servicestack-api`).

**Gotcha (EF on net48):** if you add Entity Framework Core for a custom database, **EF Core 3.1 is the last EF Core line that supports .NET Framework 4.8.** Avoid **EF Core 3.0 specifically** - it targets .NET Standard 2.1, which net48 cannot load, so it will not run. EF6 is also a perfectly valid net48 choice and is often the smoother fit for a Framework app.

## MVC widget ground rules

Widgets are plain ASP.NET MVC controllers registered into the page-editor toolbox via `[ControllerToolboxItem]` and made view-discoverable via `[EnhanceViewEnginesAttribute]` - no Global.asax registration. Beyond that, three concerns each have a deep skill:

- **Building/reviewing widgets** (controller structure, actions, persistence, views, script/CSS loading) -> `sitefinity-widget-expert`.
- **Designer property editors** (field types, sections, conditional visibility, content selectors, `LinkModel`, `TableView`) -> `sitefinity-designer-attributes`.
- **Toolbox icon CSS classes** for the `CssClass` on `[ControllerToolboxItem]` -> the "Toolbox icons" section of `sitefinity-widget-expert`.

## Verify before done

Do not declare a Sitefinity change finished until you have proven it:

- **Build the solution** - these projects need **msbuild**, not `dotnet build` (see `sitefinity-cli-build`).
- **For page / widget changes, inspect the actual persisted state** rather than trusting the code: MCP `sitefinity_get_page_widget_tree` and `sitefinity_get_widget_properties`, or the verified SQL in `sitefinity-page-inspector` / `sitefinity-page-controls-map`.
- **Check the error log** for startup and runtime failures: MCP `sitefinity_read_log_file` (defaults to Error.log), or `App_Data/Sitefinity/Logs/Error.log` on disk.
- **Remember the app restarts** after any `bin/` or `web.config` change, and **Sitefinity startup is slow (30-90s)** after a recycle - a blank or "Please wait" response often just means it is still booting, not that your change failed.

## Skill router

| Skill | Reach for it when |
|---|---|
| **sitefinity-best-practices** | (this skill) Starting any task, unsure which skill applies, need general MVC guidance or a data-access decision, or verifying versions. |
| **sitefinity-widget-expert** | Developing, reviewing, or troubleshooting MVC widgets - controller structure, routing and page titles, property persistence, views, script/CSS loading, custom designer views, toolbox icons. |
| **sitefinity-designer-attributes** | Building or configuring a widget's designer property editor - field types, sections, conditional visibility, content selectors, `LinkModel`, `TableView`, choices. |
| **sitefinity-adminapp-extensions** | Extending the Sitefinity admin backend UI with Angular custom field editors (overriding admin-app fields with your own components). |
| **sitefinity-servicestack-api** | Building JSON APIs inside Sitefinity with ServiceStack - the `/RestApi` prefix, Sitefinity-identity auth, and `DateTime`/ISO serialization pitfalls. |
| **sitefinity-odata-services** | Reading/querying existing content over the built-in default OData web service at `/api/default` - `sfhelp` discovery, `$filter`/`$expand`/`$select`/paging, exposing types, access levels. Zero-code content API. |
| **sitefinity-vue3-vite8-guide** | Adding or troubleshooting a Vue 3 + Vite 8 (Rolldown) + Tailwind v4 + shadcn-vue frontend on the classic MVC renderer - data-island widgets, design-mode mounting, search indexing, HMR. Not for the Core Renderer. |
| **sitefinity-react-vite8-guide** | The React 19 counterpart on the same classic MVC stack - React root lifecycle inside the page editor, `mountWidget` + error boundary with a pluggable reporting sink, hashed-entry chunk loading, migrating widgets from Vue. Not for the Core Renderer. |
| **sitefinity-api-helpers** | Writing or reviewing Sitefinity C# and you need the right built-in helper - UI time-zone conversion (`ToSitefinityUITime`), `GetValue`/`SetValue`, `GetRelatedItems`, lifecycle (`GetLive`/`Publish`), media URLs, identity and permission checks, elevated privilege, design/preview/index mode. Verified 15.4 signatures. |
| **sitefinity-database-structure** | Understanding or querying Sitefinity tables directly - page composition, content lifecycle (Master/Temp/Live), drafts, versioning, templates, Module Builder / `dynmc_` tables, URL storage. The full DB reference. |
| **sitefinity-page-controls-map** | Fast lookup of how widgets + properties are stored (`sf_object_data` + `sf_control_properties`), the two-level hierarchy, `SiblingId` render ordering, ready-to-use read SQL. |
| **sitefinity-page-inspector** | Inspecting pages read-only - what widgets are on a page, reading widget property values, troubleshooting composition via MCP tools and verified SQL. |
| **sitefinity-page-surgery** | Writing migration CODE that changes pages - switch templates, add/remove/reorder widgets, swap a controller, rewrite property values (gated `PageManager` endpoints, never LLM DB writes). |
| **sitefinity-poco-generator** | Generating a strongly-typed C# POCO from a Module Builder dynamic content type, with a `DynamicContent` hydration constructor. |
| **sitefinity-binding-doctor** | Diagnosing/fixing assembly-binding YSODs - "Could not load file or assembly", manifest-definition mismatches, out-of-sync `bindingRedirect` entries. |
| **sitefinity-cli-build** | Building, testing, and packaging the solution from the command line - msbuild-based `build.ps1`/`test.ps1` wrapped as npm scripts, deployment bundling. |
| **sitefinity-debloat-repo** | A repo that commits `bin/`, `AdminApp/`, `packages/`, or DB backups - turning them into reproducible NuGet-restored artifacts, shrinking `.git`, reconciling assembly versions after an upgrade. |

## Things every Sitefinity developer should just know (verified in the 15.4 source)

Facts that are not in any tutorial, each read from the decompiled 15.4.8636 assemblies during the skill verification. The deep skill named in brackets has the details.

- **Content exists once per lifecycle state as separate rows.** Master (0) is the editable truth, Live (2) is a *different row* the public sees, Temp (1) is a checkout copy. Everything external (URLs, related data, permissions, versions) points at the **master** id. Pages are the exception: one live row plus draft rows in another table. [`sitefinity-database-structure`]
- **Dates are UTC in storage; display goes through `ToSitefinityUITime()`**, which uses the UI Time Zone setting in System config, not the server clock. `ItemViewModel.GetDateTime` already does this. [`sitefinity-api-helpers`]
- **HTML never stores URLs to CMS items.** Images, documents and page links inside rich text are `sfref="[images|Provider]<guid>"` keys resolved at render by `LinkParser` + `DynamicLinksParser.GetContentUrl`. Your own widget's HTML property is **not** resolved for you; call `LinkParser.ResolveLinks(html, DynamicLinksParser.GetContentUrl, null, false)` and mark the property `[DynamicLinksContainer]` so the designer persists keys. [`sitefinity-widget-expert`]
- **Feather's `HtmlFilterProvider.ApplyFilters` returns an empty string when there is no HTTP context.** Any LongText field read through `ItemViewModel.Fields` from a scheduled task or startup code is silently blank; read the `Lstring` directly and resolve links yourself there. [`sitefinity-widget-expert`]
- **Widget properties are saved by diffing against `new Controller()`.** Only values that differ from your C# defaults get rows, so changing a default later changes every already-placed widget that never overrode it. Lists and your own classes need `[PropertyPersistence(PersistAsJson = true)]`; Renderer-assembly types (`MixedContentContext`, `LinkModel`) are JSON automatically. [`sitefinity-widget-expert`]
- **The AdminApp designer for MVC widgets runs the ASP.NET Core Renderer's `PropertiesConfigurator`.** That is why `Progress.Sitefinity.Renderer` attributes work in classic MVC - every `[DataContract]`-marked attribute is serialized as `Meta_*` metadata. `ColorPalette`, `Copy`, `ContentContainer` are not in the MVC assemblies at all. [`sitefinity-designer-attributes`]
- **`[ViewSelector]` only sees views under `ResourcePackages/<Theme>/MVC/Views/<Widget>/`.** Views in the project's `Mvc/Views` folder give an empty dropdown; use an enum. [`sitefinity-widget-expert`]
- **Publishing a page through `PageManager` writes no version-history row.** The backend editor calls `VersionManager.CreateVersion` afterwards; migration code must too. A revert deserializes a snapshot into the draft with **new control ids** and does not publish. Temp drafts are never deleted (`DeleteTempAfterPublish => false`). [`sitefinity-page-surgery`]
- **There are 300+ foreign keys, but none on the joins you care about.** Module Builder tables deliberately have no FK to `sf_dynamic_content` (`OpenAccessConnection` strips it); schema upgrades keep your extra columns and indexes but drop your extra constraints. [`sitefinity-database-structure`]
- **`voa_class` is a type discriminator, `Guid.Empty` means "unset", vowel-stripped names are the ORM's doing.** Never derive a table name by string manipulation; read `sf_meta_data_mapping`. [`sitefinity-database-structure`]
- **One ServiceStack AppHost serves Sitefinity's backend and your APIs.** Any global `JsConfig` / `HostConfig` change breaks the admin UI somewhere far from your code; keep behaviour on your DTOs. [`sitefinity-servicestack-api`]
- **Permission bits are `2^index` of the action in its set** (`Pages`: View 1, CreateChildControls 2, EditContent 4, Create 8, Modify 16, Delete 32, ...). Use `IsGranted` / `GrantActions` rather than bit math; use `ClaimsManager.GetCurrentIdentity()` rather than `HttpContext.User`. [`sitefinity-api-helpers`]
- **Context switches are disposables**: `ElevatedModeRegion(manager)`, `AllProvidersAccessRegion`, `SiteRegion.FromSiteId/FromSiteMapRoot`, `CultureRegion`, `UnrestrictedModeRegion`, plus `SystemManager.RunWithElevatedPrivilege` for delegates. Background code that touches multisite or multilingual data needs the site and culture regions or resolves against the wrong site. [`sitefinity-api-helpers`]
- **Search indexing renders the page without JavaScript.** `Page.Items["IsInIndexMode"]` is the flag; client-rendered widgets must emit server HTML in that mode or they are invisible to search. [`sitefinity-widget-expert`, the frontend guides]
- **Every extra URL segment is a 404 until something claims it.** `PageRouteHandler` starts a request that has `Params` as unresolved and returns 404 at `PreRenderComplete` unless `RouteHelper.GetUrlParametersResolved()` is true. `[RelativeRoute]`, `IRouteMapper`, the convention mapper (`/{action}/{args}` bound positionally) and the stock detail/taxonomy/date mappers set it; a hand-rolled resolver calls `RouteHelper.SetUrlParametersResolved()` itself, and `HttpNotFound()` from an action keeps it unset on purpose. A widget sets the page title with `ViewBag.Title`; the MVC proxy copies it to `Page.Header.Title` after the action. [`sitefinity-widget-expert`]
- **Output cache invalidation is opt-in for custom widgets.** A cached page only refreshes when content changes if the widget registered that content as a dependency: `this.AddCacheDependencies(keys)` from `Telerik.Sitefinity.Frontend.Mvc.Infrastructure.Controllers.ControllerExtensions`, with keys from `OutputCacheDependencyHelper.GetPublishedContentCacheDependencyKeys(typeof(T), itemId)` (or the `appName` overload for "any item of this type"). Stock widgets, `ItemViewModel.RelatedItems`, `ContentModelBase.GetKeysOfDependentObjects` and `DynamicLinksParser` do this for you; a hand-rolled `GetDataItems` widget does not, and serves stale HTML until the cache expires. [`sitefinity-api-helpers`]
- **Related-data fields and dynamic links are different mechanisms.** RelatedData / RelatedMedia fields are `sf_content_link` rows read through `GetRelatedItems<T>`; dynamic links are keys inside HTML strings. A "featured image" field is the former; an image pasted into a content block is the latter. [`sitefinity-api-helpers`]

## Cross-cutting gotchas

- **App pool restarts on `web.config` / `bin/` changes.** Any assembly swap or config edit recycles the app; expect the 30-90s cold start before the site responds.
- **Backend vs frontend routing.** `/Sitefinity` is the admin backend; frontend pages resolve through Sitefinity's own routing off page nodes in the database - a URL that "should" exist may simply not be a published page.
- **Output cache can mask changes.** If a page looks stale after a code or content change, output caching (or a CDN in front) may be serving an old render - check cache settings before assuming your change didn't take.
- **The Lucene search indexer does not execute JavaScript.** Client-rendered widgets (Vue data islands, etc.) are invisible to search unless you also emit server-side HTML during indexing - see `sitefinity-vue3-vite8-guide` and the `Util.IsIndexingMode` pattern.

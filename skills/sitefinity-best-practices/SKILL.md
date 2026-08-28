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
   (Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion   # e.g. 15.4.8630.0 -> Sitefinity 15.4
   ```
3. **Confirm the .NET target** in the `.csproj` (`<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>` or `<TargetFramework>net48</TargetFramework>`).

**Gotcha: these skills assume net48 + MVC/Feather widgets (`MvcControllerProxy`), NOT the Sitefinity ASP.NET Core renderer / decoupled ("Sitefinity Core") frontend.** The Core renderer uses a different widget model (the `renderer` column populated on `sf_page_data` / `sf_page_templates`), different view discovery, and a different hosting process. If the project targets the ASP.NET Core renderer, the guidance here and in most companion skills does NOT transfer wholesale - call that out explicitly rather than applying MVC recipes to a Core site.

**Version-specific behavior is real.** Designer attribute availability grows release to release, SDK package versions differ, and lifecycle internals get reworked. When you ask a docs question or reason about an API, **state the version you are on** so the answer is scoped correctly. When in doubt about a specific attribute or overload, verify it exists in that project's assemblies before relying on it.

## Docs-first, verified-behavior-wins

Prefer authoritative sources over blog posts:

- **Official docs**: https://www.progress.com/documentation/sitefinity-cms
- **Official samples**: https://github.com/Sitefinity/feather-samples (classic MVC/Feather widgets - this stack). **Gotcha:** https://github.com/Sitefinity/sitefinity-aspnetcore-mvc-samples, despite the "mvc" in its name, targets the ASP.NET Core renderer - do not copy its patterns into a net48 MVC site.

The platform has many versions and the wider web is littered with stale, version-mismatched blog posts - treat random search results with suspicion. **Gotcha:** when the docs and what you actually observe on the running site disagree, trust the verified behavior, and say so plainly (note the doc, note the observation, proceed on the observation). The companion skills follow this rule - their claims were verified against real 15.4 assemblies and databases, not copied from docs.

## Data access decision guide

Choosing the wrong data layer is the most damaging early mistake. The rule:

- **Sitefinity-owned CMS data** - pages, content items (News, Events, Blogs), Module Builder dynamic types, taxonomies, users, media - goes through the **manager / OpenAccess-backed APIs**: `PageManager`, `DynamicModuleManager`, `ContentManager`, `TaxonomyManager`, `UserManager`, `LibrariesManager`, etc.
- **NEVER issue raw SQL writes against `sf_*` tables.** The `sf_*` schema has essentially **zero foreign-key constraints** - the OpenAccess ORM owns referential integrity, the content lifecycle (Master/Temp/Live/drafts), and the cache. A direct write bypasses all of it and silently corrupts caches, lifecycle state, and versioning. Reads against `sf_*` for diagnostics and inspection are fine (several companion skills give you verified SQL); writes are not.
- **Your own custom databases** (application data that is not Sitefinity content) can use a **separate data layer side by side** - EF, Dapper, ADO.NET - pointed at your own tables. Sitefinity does not object; just keep it out of the `sf_*` schema.
- **Reading content over HTTP:** to expose or query existing CMS content as REST, decide read-vs-custom first - just **reading/querying** (list/filter/sort/page/expand) is zero-code through the built-in default OData service at `/api/default` (`sitefinity-odata-services`); **custom logic, writes, or bespoke shapes** call for a ServiceStack service (`sitefinity-servicestack-api`).

**Gotcha (EF on net48):** if you add Entity Framework Core for a custom database, **EF Core 3.1 is the last EF Core line that supports .NET Framework 4.8.** Avoid **EF Core 3.0 specifically** - it targets .NET Standard 2.1, which net48 cannot load, so it will not run. EF6 is also a perfectly valid net48 choice and is often the smoother fit for a Framework app.

## MVC widget ground rules

Widgets are plain ASP.NET MVC controllers registered into the page-editor toolbox via `[ControllerToolboxItem]` and made view-discoverable via `[EnhanceViewEnginesAttribute]` - no Global.asax registration. Beyond that, three concerns each have a deep skill:

- **Building/reviewing widgets** (controller structure, actions, persistence, views, script/CSS loading) -> `sitefinity-widget-expert`.
- **Designer property editors** (field types, sections, conditional visibility, content selectors, `LinkModel`, `TableView`) -> `sitefinity-designer-attributes`.
- **Toolbox icon CSS classes** for the `CssClass` on `[ControllerToolboxItem]` -> `sitefinity-toolbox-icons`.

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
| **sitefinity-widget-expert** | Developing, reviewing, or troubleshooting MVC widgets - controller structure, property persistence, views, script/CSS loading, custom designer views. |
| **sitefinity-designer-attributes** | Building or configuring a widget's designer property editor - field types, sections, conditional visibility, content selectors, `LinkModel`, `TableView`, choices. |
| **sitefinity-toolbox-icons** | Picking the `CssClass` toolbox icon for a new MVC widget's `[ControllerToolboxItem]`. |
| **sitefinity-adminapp-extensions** | Extending the Sitefinity admin backend UI with Angular custom field editors (overriding admin-app fields with your own components). |
| **sitefinity-servicestack-api** | Building JSON APIs inside Sitefinity with ServiceStack - the `/RestApi` prefix, Sitefinity-identity auth, and `DateTime`/ISO serialization pitfalls. |
| **sitefinity-odata-services** | Reading/querying existing content over the built-in default OData web service at `/api/default` - `sfhelp` discovery, `$filter`/`$expand`/`$select`/paging, exposing types, access levels. Zero-code content API. |
| **sitefinity-vue3-vite8-guide** | Adding or troubleshooting a Vue 3 + Vite 8 (Rolldown) + Tailwind v4 + shadcn-vue frontend - data-island widgets, design-mode mounting, search indexing, HMR. |
| **sitefinity-database-structure** | Understanding or querying Sitefinity tables directly - page composition, content lifecycle (Master/Temp/Live), drafts, versioning, templates, Module Builder / `dynmc_` tables, URL storage. The full DB reference. |
| **sitefinity-page-controls-map** | Fast lookup of how widgets + properties are stored (`sf_object_data` + `sf_control_properties`), the two-level hierarchy, `SiblingId` render ordering, ready-to-use read SQL. |
| **sitefinity-page-inspector** | Inspecting pages read-only - what widgets are on a page, reading widget property values, troubleshooting composition via MCP tools and verified SQL. |
| **sitefinity-page-surgery** | Writing migration CODE that changes pages - switch templates, add/remove/reorder widgets, swap a controller, rewrite property values (gated `PageManager` endpoints, never LLM DB writes). |
| **sitefinity-poco-generator** | Generating a strongly-typed C# POCO from a Module Builder dynamic content type, with a `DynamicContent` hydration constructor. |
| **sitefinity-binding-doctor** | Diagnosing/fixing assembly-binding YSODs - "Could not load file or assembly", manifest-definition mismatches, out-of-sync `bindingRedirect` entries. |
| **sitefinity-cli-build** | Building, testing, and packaging the solution from the command line - msbuild-based `build.ps1`/`test.ps1` wrapped as npm scripts, deployment bundling. |
| **sitefinity-debloat-repo** | A repo that commits `bin/`, `AdminApp/`, `packages/`, or DB backups - turning them into reproducible NuGet-restored artifacts, shrinking `.git`, reconciling assembly versions after an upgrade. |

## Cross-cutting gotchas

- **App pool restarts on `web.config` / `bin/` changes.** Any assembly swap or config edit recycles the app; expect the 30-90s cold start before the site responds.
- **Backend vs frontend routing.** `/Sitefinity` is the admin backend; frontend pages resolve through Sitefinity's own routing off page nodes in the database - a URL that "should" exist may simply not be a published page.
- **Output cache can mask changes.** If a page looks stale after a code or content change, output caching (or a CDN in front) may be serving an old render - check cache settings before assuming your change didn't take.
- **The Lucene search indexer does not execute JavaScript.** Client-rendered widgets (Vue data islands, etc.) are invisible to search unless you also emit server-side HTML during indexing - see `sitefinity-vue3-vite8-guide` and the `Util.IsIndexingMode` pattern.

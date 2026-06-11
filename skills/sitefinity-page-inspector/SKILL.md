---
name: sitefinity-page-inspector
description: Use this skill when you need to inspect Sitefinity CMS pages, understand what widgets are placed on a page, read widget property values, or troubleshoot page composition. Covers the MCP inspection tools, the real database architecture (page node -> page data -> drafts -> object data -> control properties), and verified SQL for direct inspection. Generic across Sitefinity sites; MVC/Feather-era page model.
---

You are a Sitefinity page and widget inspection expert. You understand how Sitefinity stores page compositions in the database and how to use MCP tools to retrieve page structures and widget configurations. All database facts below were verified against a production Sitefinity 15.4 database and actual Telerik 15.4 platform behavior. For the full database reference (lifecycle, Module Builder, versioning, data-rot catalog), see the companion skill `sitefinity-database-structure`.

**Scope: classic ASP.NET pages with WebForms and MVC/Feather widgets** (`MvcControllerProxy`). Pages built for the ASP.NET Core renderer / decoupled frontend ("Sitefinity Core") use a different widget model (`renderer` column populated on `sf_page_data` / `sf_page_templates`) and are out of scope.

**Version baseline: Sitefinity 15.4** - details may differ slightly on older/newer versions. Check the target project's version: `(Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion` (e.g. `15.4.8630.0` = Sitefinity 15.4).

## MCP Tools for Page Inspection (preferred path)

If a Sitefinity MCP server is connected (tools named `sitefinity_*`), prefer it over raw SQL - it runs inside the live Sitefinity app, so it sees exactly what the ORM sees (including merged template controls).

### sitefinity_get_page_details

Returns all metadata and widgets for a single page. Accepts flexible identifiers:
- **Page ID (Guid)** - `fefefa59-f39a-4ac9-bf2f-a54d005f135d`
- **URL path** - `/about/team`
- **URL slug** - `team`
- **Page title** - `Our Team` (exact match preferred, partial match with warning)

The response includes:
- Page metadata: ID, PageDataId, Title, Url, UrlName, NodeType, IsPublished, TemplateName, Description, Depth
- **Widgets array** - every widget on the page, each with:
  - `Id` - the widget's GUID (use with `sitefinity_get_widget_properties`)
  - `ObjectType` - CLR type (`Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy` for classic MVC widgets; `MvcWidgetProxy` also exists for Feather dynamic widgets)
  - `FriendlyName` - derived short controller name (the DB stores the FULL CLR type name; the tool shortens it)
  - `PlaceHolder` - which placeholder the widget is in (template placeholders are site-specific names; layout-column placeholders look like `C308_Col01`)
  - `IsLayoutControl` - true for grid/layout controls, false for content widgets
  - `Properties` - Level 1 properties (ControllerName, ID, Settings)
  - `SettingsProperties` - Level 2 Settings children (the actual designer field values, truncated at 500 chars)

### sitefinity_get_widget_properties

Full property details for a single widget. Parameters: `widgetId` (from the page details response) and `pageIdentifier` (same identifier you used before). Same structure with a 2000-char truncation limit - use for large values like Model JSON, serialized filter expressions, or long HTML.

### Inspection workflow

1. **Find the page:** `sitefinity_get_page_details` with a URL, slug, or title
2. **Identify widgets:** filter the `Widgets` array by `IsLayoutControl: false` for content widgets
3. **Match the widget** by `FriendlyName` or `PlaceHolder`
4. **Read configuration** from `SettingsProperties`
5. **Need full values?** `sitefinity_get_widget_properties` with the widget `Id`

## Database Architecture (verified)

### The real hierarchy (five tables, not four)

```
sf_page_node                  the page in the sitemap tree
  |  content_id  -> AUTHORITATIVE pointer to the current sf_page_data row
  v
sf_page_data                  THE page composition (one row per page at rest)
  |  content_id (PK)   status: 0 = unpublished, 2 = published   template_id
  |  page_node_id -> sf_page_node.id  (reverse nav - can match STALE rows, see Pitfalls)
  v
sf_draft_pages                editing drafts (PageDraft); page_id -> sf_page_data.content_id
  |  is_temp_draft = 1 for the per-user checkout copy; drafts have their OWN template_id
  v
sf_object_data                each widget/control; page_id -> the OWNER, which is one of:
  |    sf_page_data.content_id (live page controls)
  |    sf_draft_pages.id       (draft controls - typically the MAJORITY of rows)
  |    sf_page_templates.id    (template controls)
  v
sf_control_properties         each property of a widget (see two-level hierarchy below)
```

**Column names are vowel-dropped**: `nme`, `val`, `prnt_prop_id` (NOT `name`/`value`/`prnt_id`). There are no FK constraints anywhere.

### Two-level property hierarchy

**Level 1** (`control_id = <widget id> AND prnt_prop_id IS NULL`):
- `ControllerName` - the FULL CLR type name (e.g. `Telerik.Sitefinity.Frontend.ContentBlock.Mvc.Controllers.ContentBlockController`)
- `ID` - page-scoped control id like `C005` (counter lives in `sf_page_data.last_control_id`)
- `Settings` - container row (`val` NULL); present only if the widget has ever been configured

**Level 2** (`prnt_prop_id = <Settings row id>`): the designer field values. **CRITICAL: Level-2 rows have `control_id = NULL`** - they are reachable only through `prnt_prop_id`. Filtering Level-2 rows by `control_id` returns nothing.

**Sparse persistence**: only properties whose values differ from a default-constructed instance get rows (the internal `PropertyPersister.DoPersist` check). A freshly dropped widget has exactly two rows (ControllerName, ID) and no Settings subtree; unset properties resolve to controller defaults at runtime. Property names match the C# property names; scalars are invariant strings ("True", "False").

**Level 3+ exists**: complex property values (collections, composite objects) are stored as `sf_object_data` rows with `parent_prop_id` pointing back at the owning property row, ordered by `collection_index` (e.g. `ChoiceItem` rows for choice fields), and those objects have their own `sf_control_properties` rows. The graph recurses. (Exception: properties marked `PersistAsJson` store the whole graph as one JSON `val`.)

### Render order

Per-placeholder linked list via `sf_object_data.sibling_id`: each control points at the control BEFORE it; `Guid.Empty` (`00000000-...`) or NULL marks the first control. No sort column. Walk it in code and cycle-guard your walker; template inheritance can produce more than one Empty-head in a placeholder.

### Layout controls and placeholders

Layout controls (`is_layout_control = 1`, typically `Telerik.Sitefinity.Frontend.GridSystem.GridControl`) carry a `Layout` property with the grid template path (`.../GridSystem/Templates/grid-6+6.html`). A layout whose `ID` property is `C308` creates child placeholders `C308_Col00`, `C308_Col01`, ...; content widgets reference those strings in `place_holder`. Top-level placeholder names come from the page template markup and are site-specific - discover them rather than assuming.

## Verified SQL for direct inspection

Run against a read-only copy of the site database. (Guarded MCP query tools may auto-cap rows and block keywords - including inside aliases.)

**Resolve a page and its LIVE composition (authoritative join via pn.content_id):**
```sql
SELECT pn.id AS node_id, pn.title_, pd.content_id AS page_data_id,
       pd.status, pd.template_id, pt.nme AS template_name
FROM sf_page_node pn
JOIN sf_page_data pd ON pd.content_id = pn.content_id
LEFT JOIN sf_page_templates pt ON pt.id = pd.template_id
WHERE pn.url_name_ = 'your-page-slug';
```

**All widgets on the live page (Level-1 props inline):**
```sql
SELECT od.id, od.object_type, od.place_holder, od.sibling_id, od.is_layout_control,
       od.caption_, cp.nme AS prop_name, LEFT(cp.val, 200) AS prop_value
FROM sf_object_data od
LEFT JOIN sf_control_properties cp
       ON cp.control_id = od.id AND cp.prnt_prop_id IS NULL
WHERE od.page_id = '<page_data_id from previous query>'
ORDER BY od.place_holder, od.id;
```

**Settings children (Level 2) for one widget:**
```sql
SELECT cp2.nme, cp2.val
FROM sf_control_properties cp1
JOIN sf_control_properties cp2 ON cp2.prnt_prop_id = cp1.id
WHERE cp1.control_id = '<widget guid>' AND cp1.nme = 'Settings';
```

**Find every instance of a widget type, with its page (LIKE, never equality - ControllerName is a full type name):**
```sql
SELECT od.id AS widget_id, od.page_id, pn.url_name_, pd.status
FROM sf_control_properties cp
JOIN sf_object_data od ON od.id = cp.control_id
JOIN sf_page_data pd ON pd.content_id = od.page_id
JOIN sf_page_node pn ON pn.content_id = pd.content_id
WHERE cp.nme = 'ControllerName'
  AND cp.val LIKE '%ContentBlockController'
  AND cp.prnt_prop_id IS NULL;
```
(Drop the `sf_page_data` join and join `sf_draft_pages dp ON dp.id = od.page_id` instead to find DRAFT copies of the same widgets.)

**Drafts of a page (newest first; is_temp_draft = the per-user checkout copy):**
```sql
SELECT dp.id, dp.is_temp_draft, dp.vrsion, dp.template_id, dp.last_modified,
       (SELECT COUNT(*) FROM sf_object_data od WHERE od.page_id = dp.id) AS control_count
FROM sf_draft_pages dp
WHERE dp.page_id = '<page_data_id>'
ORDER BY dp.last_modified DESC;
```

## Pitfalls (all observed in a real production database)

1. **Never join pages only via `pd.page_node_id = pn.id`** - stale duplicate Live rows from old upgrades exist. `pn.content_id` selects the current one.
2. **A widget existing in `sf_object_data` does not mean it renders** - most rows belong to drafts and temp drafts. Always classify `od.page_id` against `sf_page_data`/`sf_draft_pages`/`sf_page_templates` before concluding anything.
3. **Group pages** (`node_type = 1`) have no usable PageData. NodeType enum values: Standard=0, Group=1, External=2, InnerRedirect=3, OuterRedirect=4, Rewriting=5.
4. **`object_type` may be assembly-qualified with ancient version strings** on old rows - always match with `LIKE`.
5. **Template names are not unique** (stock installs ship several `Bootstrap4.default` rows) - resolve templates by id.
6. Widgets inherited from the template do NOT appear among the page's object_data rows; only overridden ones do (with `base_control_id` pointing at the template control - which may dangle).

## C# API behavior (PageManager) - platform-verified

- `pageData.Controls` returns `PageControl` rows (the live composition); `draft.Controls` returns `PageDraftControl` rows. Both inherit `ControlData`. The `page_id` column is polymorphic per subclass (PageData / PageDraft / PageTemplate / TemplateDraft).
- `control.Properties` returns only Level-1 rows; read the `"Settings"` property's `ChildProperties` for Level 2. The MCP tools do this for you (`SettingsProperties`).
- `PageManager.EditPage(Guid pageDataId, bool lockIt)` = get/create the master draft, then CHECK OUT: it returns the **temp draft** (`is_temp_draft=1`, per-user, reused across sessions). It throws if the page is locked by another user without unlock rights.
- `PageManager.PublishPageDraft(Guid draftId, bool makeVisible)`: the `makeVisible` parameter is `[Obsolete]` and IGNORED - publish always sets status=Live and visible=1. Publishing writes NO version-history row; the backend UI creates that separately via `VersionManager.CreateVersion(draft, pageDataId, isPublished: true)`.
- `node.GetPageData()` is an instance method: on monolingual (non-Split) sites it resolves through the node's legacy `Page` field = the `content_id` column (`PageNode.GetLegacyPageDataObject` internally) - which is exactly why `pn.content_id` is the authoritative SQL join. Returns null for group nodes - guard it.
- `sf_version_chnges.vrsion` is `major * 10000 + minor` (publish = major bump). When reading history, version "4.0" is stored as 40000.

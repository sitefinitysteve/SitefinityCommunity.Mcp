---
name: sitefinity-page-controls-map
description: Quick-reference map of how Sitefinity stores page widgets and their properties (sf_object_data + sf_control_properties), the two-level property hierarchy, SiblingId render ordering, and ready-to-use SQL for reading widget content. Use for fast lookups; the sitefinity-database-structure skill is the full reference.
---

# Sitefinity Page Controls & Widget Property Storage

## Overview

Sitefinity stores page widgets (controls) and their properties across two database tables with a hierarchical relationship. Understanding this structure is essential for reading widget content programmatically.

---

## Database Tables

### `sf_object_data` — The Controls/Widgets

Each row represents a control (widget) placed on a page.

| Column | Type | Purpose |
|--------|------|---------|
| `id` | uniqueidentifier (PK) | Internal GUID — the true control identity |
| `object_type` | varchar(510) | The .NET type. Classic MVC widgets use `Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy` (Feather dynamic widgets use `MvcWidgetProxy`). Old rows can carry assembly-qualified names — match with `LIKE`, never equality |
| `page_id` | uniqueidentifier | The OWNER — polymorphic, no FK constraint: `sf_page_data.content_id` (live page controls), `sf_draft_pages.id` (draft controls — typically the majority of rows), or `sf_page_templates.id` (template controls) |
| `place_holder` | varchar(255) | Which placeholder the control sits in (e.g. `Body`, `C050_Col00`) |
| `caption_` | nvarchar(255) | Human-readable label (e.g. "Spotlight", "Content block", "grid-8+4") |
| `is_layout_control` | tinyint | 1 = layout/grid control, 0 = content widget |
| `sibling_id` | uniqueidentifier | Linked-list for render order (points to previous sibling; `00000000-...` = first) |
| `parent_prop_id` | uniqueidentifier | For child objects nested under a property |

**Key insight**: ALL MVC widgets share the same `object_type` (`MvcControllerProxy`). The actual controller type (e.g. `SpotlightController`) is stored as a property, not on the control row itself.

### `sf_control_properties` — Property Name/Value Pairs

Each row is a single property belonging to either a control or another property (nested).

| Column | Type | Purpose |
|--------|------|---------|
| `id` | uniqueidentifier (PK) | This property row's GUID |
| `control_id` | uniqueidentifier | FK to `sf_object_data.id` — set for top-level properties |
| `prnt_prop_id` | uniqueidentifier | FK to `sf_control_properties.id` — set for nested (child) properties |
| `nme` | varchar(50) | Property name (e.g. "ControllerName", "Settings", "Text") |
| `val` | nvarchar(MAX) | Property value — can be huge (HTML content, JSON blobs) |
| `language` | varchar(10) | Culture code for multilingual properties |

---

## Two-Level Property Hierarchy

This is the critical piece: MVC widget properties are stored in **two levels**.

### Level 1: Direct Control Properties

Rows where `control_id` is set (pointing to `sf_object_data.id`).

MVC widget top-level properties:

| Property Name | Purpose | Example Value |
|---------------|---------|---------------|
| `ControllerName` | The full .NET type of the actual controller (always persisted — it's the key property) | `SitefinityWebApp.Mvc.Controllers.SpotlightController` |
| `ID` | The short display ID used in Sitefinity UI | `C052` |
| `Settings` | **Container row** for nested properties (val is always NULL). **Only exists if the widget has ever been configured** — persistence is sparse (only values differing from the controller's defaults get rows), so a freshly dropped widget has just `ControllerName` + `ID` and NO Settings row | `NULL` |

### Level 2: Settings Child Properties (The Actual Content)

Rows where `prnt_prop_id` is set (pointing to the `Settings` row's `id`).

These contain the **actual widget content** — the values that editors set in the Sitefinity widget designer:

| Widget Type | Child Properties | Example |
|-------------|-----------------|---------|
| **SpotlightController** | `Heading`, `SubText`, `TemplateName` | `Heading` = "Resources - Links" |
| **ContentBlockController** | `Content` | `Content` = "&lt;h2&gt;Welcome&lt;/h2&gt;&lt;p&gt;...&lt;/p&gt;" |
| **DocumentController** | `Content` (selected document IDs) | JSON array of document GUIDs |
| **NavigationController** | `SelectionMode`, `LevelsToInclude`, etc. | `SelectionMode` = "CurrentPageChildren" |
| Any MVC widget | All public properties from the controller class | Varies by controller |

### Visual Diagram

```
sf_object_data (id: AAA-BBB)
│  object_type = "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy"
│  caption_    = "Spotlight"
│  page_id     = (page GUID)
│  place_holder = "C050_Col00"
│
└── sf_control_properties (control_id = AAA-BBB)
     │
     ├── [id: 111] ControllerName = "SitefinityWebApp.Mvc.Controllers.SpotlightController"
     ├── [id: 222] ID = "C218"
     └── [id: 333] Settings = NULL   ← container, val is always NULL
          │
          └── sf_control_properties (prnt_prop_id = 333)
               ├── Heading = "Resources - Links"        ← the heading text!
               ├── SubText = "Helpful links"             ← secondary text
               └── TemplateName = "Spotlight.Default"    ← view template
```

---

## Sitefinity C# API Behavior

### What `control.Properties` Returns

When you access `PageData.Controls` via the Sitefinity `PageManager` API:

```csharp
var pageManager = PageManager.GetManager();
var node = pageManager.GetPageNode(pageId);
var pageData = node.GetPageData();
foreach (var control in pageData.Controls)
{
    // control.Id         → sf_object_data.id (the GUID)
    // control.ObjectType → "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy"
    // control.Caption    → "Spotlight"
    // control.PlaceHolder → "C050_Col00"
    // control.IsLayoutControl → false
    // control.SiblingId  → render order linked-list pointer

    foreach (var prop in control.Properties)
    {
        // prop.Name  → "ControllerName", "ID", or "Settings"
        // prop.Value → the value (Settings.Value is always null)
        // prop.Id    → the property row's own GUID
    }
}
```

**IMPORTANT**: `control.Properties` only returns **Level 1** properties (`ControllerName`, `ID`, `Settings`). It does **NOT** return the nested child properties under Settings. The `Settings` property's `.Value` is always `null`.

### How to Get Level 2 (Actual Content)

The nested properties must be fetched separately. Two approaches:

#### Approach A: Direct SQL (recommended for read-only tools)

```csharp
const string query = @"
    SELECT cp2.nme, cp2.val
    FROM sf_control_properties cp
    INNER JOIN sf_control_properties cp2 ON cp2.prnt_prop_id = cp.id
    WHERE cp.control_id = @ControlId AND cp.nme = 'Settings'";

// @ControlId = control.Id (the GUID from sf_object_data)
```

This returns all child properties: `Text`, `Content`, `BadgeText`, etc.

#### Approach B: Sitefinity API (heavier, for write operations)

```csharp
var pageManager = PageManager.GetManager();
var draft = pageManager.EditPage(pageData.Id);
var draftControl = draft.Controls.FirstOrDefault(c => c.Id == controlId);
var proxy = pageManager.LoadControl(draftControl) as MvcControllerProxy;
var controller = proxy?.Controller; // The actual controller instance with all properties populated
```

This deserializes the entire widget but requires creating a page draft — inappropriate for read-only inspection.

---

## Render Order: The SiblingId Linked List

Controls within a placeholder are ordered via a linked-list pattern using `sibling_id`:

- `sibling_id = 00000000-0000-0000-0000-000000000000` → this control renders **first** in its placeholder
- `sibling_id = (some other control's id)` → this control renders **after** the referenced control

To resolve render order:
1. Group controls by `place_holder`
2. Find the head (sibling_id = empty GUID)
3. Walk the chain: head → next (whose sibling_id = head.id) → next → ...

```csharp
// Per PLACEHOLDER (chains are scoped per placeholder — build the map per group).
// Use indexer assignment, not ToDictionary: template inheritance can legally
// produce MORE THAN ONE control with SiblingId == Guid.Empty in a placeholder,
// and ToDictionary would throw on the duplicate key.
var afterMap = new Dictionary<Guid, ControlData>();
foreach (var c in placeholderControls)
{
    afterMap[c.SiblingId] = c;
}

// Walk from head, cycle-guarded (broken chains exist in real data)
var visited = new HashSet<Guid>();
afterMap.TryGetValue(Guid.Empty, out var current);
while (current != null && visited.Add(current.Id))
{
    sorted.Add(current);
    afterMap.TryGetValue(current.Id, out current);
}
// Append any controls not reached by the walk (orphaned chain segments)
sorted.AddRange(placeholderControls.Where(c => !visited.Contains(c.Id)));
```

---

## Layout Controls & Placeholder Nesting

Layout widgets (grids) create child placeholders for their columns:

- A layout control with `ID = "C050"` and `Caption = "grid-8+4"` creates placeholders: `C050_Col00` (8/12 width), `C050_Col01` (4/12 width)
- Content widgets placed in those columns have `place_holder = "C050_Col00"`

**Grid caption format**: `grid-{width1}+{width2}+...` where widths sum to 12 (Bootstrap grid)
- `grid-12` → single full-width column
- `grid-8+4` → 8/12 + 4/12 two-column layout
- `grid-4+4+4` → three equal columns

Custom grid templates registered by a project produce their own custom captions (whatever caption the layout template defines).

---

## Common Widget ObjectTypes

| `object_type` in sf_object_data | What it is |
|-------------------------------|------------|
| `Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy` | **ALL MVC widgets** — check `ControllerName` property for the real type |
| `Telerik.Sitefinity.Web.UI.LayoutControl` | Grid/layout container |
| `Telerik.Sitefinity.Web.UI.ContentUI.ContentView` | Legacy WebForms content view |

For MVC widgets, the `ControllerName` property tells you the actual controller:
- `YourApp.*.Mvc.Controllers.*` → your custom controllers
- `Telerik.Sitefinity.Frontend.*` → built-in Sitefinity Feather controllers

---

## Page Data Hierarchy

```
sf_page_node (the page in the tree)
  │  id, title_, url_name_, parent_id, node_type
  │  content_id → the CURRENT sf_page_data row (authoritative pointer)
  │
  └── sf_page_data (the page's published composition; PK is content_id)
       │  page_node_id → sf_page_node.id  (reverse nav — can match stale rows)
       │  template_id → sf_page_templates.id
       │  status (0 = unpublished/Master, 2 = Live/published)
       │
       ├── sf_draft_pages (editing drafts; page_id → sf_page_data.content_id;
       │    is_temp_draft = 1 for the per-user checkout copy)
       │
       └── sf_object_data (the widgets)
            │  page_id → sf_page_data.content_id  OR  sf_draft_pages.id  OR  sf_page_templates.id
            │
            └── sf_control_properties (the widget properties)
                 │  control_id → sf_object_data.id   (Level-1 rows only)
                 │
                 └── sf_control_properties (nested child properties)
                      prnt_prop_id → parent sf_control_properties.id  (control_id is NULL here)
```

**Important corrections to common assumptions:**
- `sf_object_data.page_id` references `sf_page_data.content_id` (the PK — there is no `sf_page_data.id` column), or a draft, or a template. Classify the owner before concluding a widget "is on a page" — most `sf_object_data` rows belong to drafts that never render.
- **Page drafts are NOT extra `sf_page_data` rows** — they live in the separate `sf_draft_pages` table. At rest a page has ONE `sf_page_data` row (status 0 or 2). If a node matches multiple status=2 rows, those are stale upgrade leftovers; only the row matched by `sf_page_node.content_id` is current — prefer that join over `page_node_id`.
- None of these relationships are FK-enforced; dangling references occur in old databases.

---

## Quick Reference SQL Queries

### Get all widgets on a page's LIVE composition (by page node ID)

```sql
-- Join via pn.content_id (authoritative) — page_node_id alone can match stale rows.
-- Note: sibling_id is a linked list, so there is no ORDER BY that yields render
-- order in SQL; sort per placeholder in code (see the SiblingId section).
SELECT od.id, od.object_type, od.caption_, od.place_holder, od.sibling_id, od.is_layout_control,
       cp_name.val AS controller_name, cp_id.val AS widget_id
FROM sf_page_node pn
INNER JOIN sf_page_data pd ON pd.content_id = pn.content_id
INNER JOIN sf_object_data od ON od.page_id = pd.content_id
LEFT JOIN sf_control_properties cp_name ON cp_name.control_id = od.id AND cp_name.nme = 'ControllerName'
LEFT JOIN sf_control_properties cp_id ON cp_id.control_id = od.id AND cp_id.nme = 'ID'
WHERE pn.id = @PageNodeId
ORDER BY od.place_holder
```

### Get a widget's full properties (including nested Settings children)

```sql
-- Top-level properties
SELECT 'top' AS level, cp.nme, cp.val
FROM sf_control_properties cp
WHERE cp.control_id = @ControlGuid

UNION ALL

-- Nested Settings child properties (the actual content)
SELECT 'settings' AS level, cp2.nme, cp2.val
FROM sf_control_properties cp
INNER JOIN sf_control_properties cp2 ON cp2.prnt_prop_id = cp.id
WHERE cp.control_id = @ControlGuid AND cp.nme = 'Settings'
```

### Find widgets by controller type across all pages

```sql
-- copy_kind matters: most matches are DRAFT copies that never render
SELECT od.id, od.caption_, od.page_id, cp.val AS controller_name,
       CASE WHEN pd.content_id IS NOT NULL THEN 'live page'
            WHEN dp.is_temp_draft = 1 THEN 'temp draft'
            WHEN dp.id IS NOT NULL THEN 'draft'
            WHEN pt.id IS NOT NULL THEN 'template'
            ELSE 'other/orphan' END AS copy_kind
FROM sf_object_data od
INNER JOIN sf_control_properties cp ON cp.control_id = od.id AND cp.nme = 'ControllerName'
LEFT JOIN sf_page_data pd ON pd.content_id = od.page_id
LEFT JOIN sf_draft_pages dp ON dp.id = od.page_id
LEFT JOIN sf_page_templates pt ON pt.id = od.page_id
WHERE cp.val LIKE '%SpotlightController%'
```

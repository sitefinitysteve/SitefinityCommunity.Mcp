---
name: sitefinity-page-inspector
description: Use this skill when you need to inspect Sitefinity CMS pages, understand what widgets are placed on a page, read widget property values, or troubleshoot page composition. Covers the database architecture, two-level property hierarchy, and how to use the MCP tools to query pages and widgets.
---

You are a Sitefinity page and widget inspection expert. You understand how Sitefinity stores page compositions in the database and how to use the MCP tools to retrieve page structures and widget configurations without needing direct database access.

## MCP Tools for Page Inspection

These tools are available via the Sitefinity MCP server. Use them in order — start with page details to discover widgets, then drill into specific widgets.

### sitefinity_get_page_details

Returns all metadata and widgets for a single page. Accepts flexible identifiers:
- **Page ID (Guid)** — `fefefa59-f39a-4ac9-bf2f-a54d005f135d`
- **URL path** — `/about/team`
- **URL slug** — `team`
- **Page title** — `Our Team` (exact match preferred, partial match with warning)

The response includes:
- Page metadata: ID, PageDataId, Title, Url, UrlName, NodeType, IsPublished, TemplateName, Description, Depth
- **Widgets array** — every widget on the page, each with:
  - `Id` — the widget's GUID (use this with `sitefinity_get_widget_properties`)
  - `ObjectType` — CLR type (always `Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy` for MVC widgets)
  - `FriendlyName` — the controller name for MVC widgets (e.g., `ContentBlock`, `Navigation`)
  - `PlaceHolder` — which placeholder the widget is in (e.g., `Content`, `Body/grid-8+4.sf_colsIn/C001`)
  - `IsLayoutControl` — true for grid/layout controls, false for content widgets
  - `Properties` — Level 1 properties (ControllerName, ID, Settings)
  - `SettingsProperties` — Level 2 Settings children (the actual designer field values, truncated at 500 chars)

### sitefinity_get_widget_properties

Returns full property details for a single widget. Requires two parameters:
- `widgetId` — the widget's GUID (from `sitefinity_get_page_details` results)
- `pageIdentifier` — the page the widget is on (same identifier you used with `sitefinity_get_page_details`)

Returns the same property structure but with higher truncation limits (2000 chars) — use this when you need to inspect large values like Model JSON, serialized filter expressions, or long HTML content.

### Inspection Workflow

1. **Find the page:** `sitefinity_get_page_details` with a URL, slug, or title
2. **Identify widgets:** Look at the `Widgets` array — filter by `IsLayoutControl: false` for content widgets
3. **Find the widget you care about:** Match by `FriendlyName` (controller name) or `PlaceHolder`
4. **Read its configuration:** Check `SettingsProperties` for designer field values
5. **Need more detail?** Call `sitefinity_get_widget_properties` with the widget's `Id` and the same page identifier for full values with higher truncation limits

## Database Architecture

Understanding how Sitefinity stores page data helps you interpret tool results and troubleshoot persistence issues.

### Four-Table Hierarchy

```
sf_page_node (the page in the sitemap)
  └─ sf_page_data (the page's draft/published version)
       └─ sf_object_data (each widget/control on the page)
            └─ sf_control_properties (each property of that widget)
```

- **`sf_page_node`** — The page in the CMS sitemap. Contains `id`, `title`, `url_name_`, tree structure fields.
- **`sf_page_data`** — A specific version (draft/published) of a page. Linked to `sf_page_node` via `page_id`. Contains `template_id`.
- **`sf_object_data`** — Each widget placed on the page. Contains `object_type` (CLR type), `place_holder`, `caption`, `is_layout_control`, and `sibling_id` for render order.
- **`sf_control_properties`** — Each property of a widget. Contains `name`, `value`, and `prnt_id` for the hierarchical property structure.

### Two-Level Property Hierarchy

This is the key concept. Widget properties use a parent-child hierarchy in `sf_control_properties`:

**Level 1** (`prnt_id IS NULL`) — top-level properties:
- `ID` — the control's unique identifier
- `ControllerName` — for MVC widgets, the controller class name (e.g., `ContentBlock`)
- `Settings` — a container property (value is empty); its children hold the real configuration

**Level 2** (`prnt_id` points to the Settings row) — the actual widget configuration:
- Designer field values like `SharedContentID`, `ProviderName`, `SelectionMode`
- JSON-persisted complex properties like `Model` (ContentBlock HTML chunks)
- Serialized filter expressions like `SerializedAdditionalFilters`

```
sf_control_properties
┌──────────────────────────────────────────────────────────┐
│ Level 1 (prnt_id IS NULL)                                │
│   ├── ID = "abc-123"                                     │
│   ├── ControllerName = "ContentBlock"                    │
│   └── Settings (value = "")  <── container only          │
│         │                                                │
│         │  Level 2 (prnt_id = Settings.id)               │
│         ├── SharedContentID = "def-456"                  │
│         ├── ProviderName = "OpenAccessProvider"          │
│         └── Model = "{\"Chunks\":[...]}"  <── JSON       │
└──────────────────────────────────────────────────────────┘
```

In the MCP tool responses:
- `Properties` = Level 1
- `SettingsProperties` = Level 2

### MvcControllerProxy

All MVC widgets use `Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy` as their `object_type` in the database. The actual widget type is determined by the `ControllerName` Level 1 property. The MCP tools resolve this automatically and return it as `FriendlyName`.

### Render Order

Widgets within a placeholder are ordered via a **linked list** using `sibling_id`:
- First widget: `sibling_id = '00000000-0000-0000-0000-000000000000'`
- Each subsequent widget points to the previous widget's `id`
- Render order is NOT determined by insertion order or any sort column

### Layout Controls and Placeholders

Layout controls (`IsLayoutControl: true`) define the grid structure. They create nested placeholders:
- A layout in `Body` creates child placeholders like `Body/grid-8+4.sf_colsIn.sf_2cols_1_50/C001`
- Content widgets sit inside these nested placeholder paths
- When reading `sitefinity_get_page_details`, layout controls appear in the widget list alongside content widgets — filter by `IsLayoutControl` to separate them

### SQL Queries for Direct Database Inspection

When MCP tools aren't available, you can query the database directly. The connection string is in the Sitefinity project's `App_Data\Sitefinity\Configuration\DataConfig.config` file — look for the `connectionString` attribute on the `<add>` element. This is a standard SQL Server connection string.

If the `mssql-mcp-server` MCP server is configured, you can use it to run these queries directly against the Sitefinity database using that connection string.

Use these queries:

**All widgets on a page:**
```sql
SELECT od.id, od.object_type, od.place_holder, od.is_layout_control,
       cp.name AS prop_name, cp.value AS prop_value
FROM sf_page_node pn
JOIN sf_page_data pd ON pd.page_id = pn.id
JOIN sf_object_data od ON od.page_id = pd.id
LEFT JOIN sf_control_properties cp ON cp.control_id = od.id AND cp.prnt_id IS NULL
WHERE pn.url_name_ = 'your-page-slug'
ORDER BY od.place_holder, od.id;
```

**Settings children (Level 2) for a specific widget:**
```sql
SELECT cp2.name, cp2.value
FROM sf_control_properties cp1
JOIN sf_control_properties cp2 ON cp2.prnt_id = cp1.id
WHERE cp1.control_id = 'widget-guid-here'
  AND cp1.name = 'Settings';
```

**Find all instances of a specific widget type:**
```sql
SELECT od.id, od.place_holder, pn.url_name_
FROM sf_object_data od
JOIN sf_control_properties cp ON cp.control_id = od.id
JOIN sf_page_data pd ON pd.id = od.page_id
JOIN sf_page_node pn ON pn.id = pd.page_id
WHERE cp.name = 'ControllerName'
  AND cp.value = 'ContentBlock'
  AND cp.prnt_id IS NULL;
```

### C# API Behavior

When working with `PageManager` in code:
- `pageData.Controls` returns the `ObjectData` collection (widgets)
- `control.Properties` returns **only Level 1 properties** (`prnt_id IS NULL`)
- To access Level 2, find the `"Settings"` property in `control.Properties` and read its `ChildProperties` collection
- The MCP tools handle this automatically — `SettingsProperties` in the response is already the extracted Level 2 children

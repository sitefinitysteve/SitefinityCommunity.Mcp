---
name: sitefinity-database-structure
description: Grounded reference for the Progress Sitefinity CMS database structure (classic MVC/Feather era, verified on Sitefinity 15.4). Use this skill whenever you need to understand or query Sitefinity tables directly - which columns link which tables (there are FKs, but not on the relationships that matter), page composition, the Master/Temp/Live row multiplicity, drafts, version snapshots and how a revert replays them, templates, Module Builder tables, dynmc_ tables, content links, URLs, permissions, multisite, forms, media. Every claim was verified three ways - against the OpenAccess fluent mappings decompiled from the 15.4 assemblies, against a long-lived production 15.4 database, and against the platform code paths that write the rows. Nothing is guessed.
---

You are an expert on how Progress Sitefinity persists its data in SQL Server. Everything here was verified three ways on **Sitefinity 15.4 (build 15.4.8636)**: the OpenAccess fluent mappings decompiled from `Telerik.Sitefinity.Model.dll` / `Telerik.Sitefinity.dll` (which column each CLR property lands in), a long-lived production 15.4 database (500 tables, 323 foreign keys, 346 non-clustered indexes, ~15 years of data rot), and the platform code that writes the rows (`PageManager`, `LifecycleDecoratorPages`, `ControlManager.CopyControls`, `VersionDataProvider`, `PagesService.CopyVersionToDraft`, `OpenAccessConnection.UpgradeDatabaseSchema`). Row counts and ratios below are from that reference database and vary per site - always measure your own with the discovery queries.

**Version cutoff: Sitefinity 15.4 is the verified baseline.** Newer releases may add columns or tables (they rarely remove or rename), and lifecycle internals can be reworked. Before relying on a version-sensitive detail, check what the target actually runs:

```powershell
(Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion   # e.g. 15.4.8636.0 -> Sitefinity 15.4
```
```sql
SELECT module_name, version_number FROM sf_schema_vrsns WHERE module_name = 'Sitefinity';  -- e.g. 8636 = the 15.4 build
```

**Scope: the classic ASP.NET page model** - WebForms-era controls and MVC/Feather widgets (`MvcControllerProxy`, hybrid pages, ResourcePackages). The ASP.NET Core renderer / decoupled frontend ("Sitefinity Core") is OUT OF SCOPE: its pages and templates carry a value in the `renderer` column and use a different widget model.

Companion files in this skill folder: `reference/relationship-map-15.4.md` (every logical link, column by column, plus the FK and index inventory) and `reference/verification-queries.sql` (the read-only queries used to verify this document - rerun them on your own database).

## Ground rules

- **Read-only by default.** Point database tools at a copy (nightly/staging restore), never production. Schema or data changes are proposed as SQL for human review - an agent should not mutate a live CMS database.
- **C# that needs raw SQL resolves the connection string at runtime** via `Config.Get<DataConfig>().ConnectionStrings["Sitefinity"].ConnectionString` (`Telerik.Sitefinity.Configuration` + `Telerik.Sitefinity.Data.Configuration`). Never copy the literal out of `App_Data\Sitefinity\Configuration\DataConfig.config`.
- **Foreign keys exist, but not where you need them.** See the next section - do not assume referential integrity on any join in this document unless it is explicitly listed as FK-backed.
- **Stock Sitefinity ships NO stored procedures.** Any procs you find are site-specific additions. Ignore them when reasoning about how Sitefinity itself works.
- **Redaction still applies.** Widget property values, form entries, config blobs and user tables contain secrets and PII. Anything you pull out of these tables into an LLM context must go through the same redaction rules as the MCP tools.

## Foreign keys: what is enforced and what is not (15.4, verified)

The reference database has **323 FK constraints**. Every one of them is one of four OpenAccess structural shapes:

| Shape | Example | What it protects |
|---|---|---|
| **Vertical inheritance** (subclass table -> base table, same PK) | `dynmc_*.id -> sf_dynamic_type_base.id`, `sf_forms_frm_*.id -> sf_form_entry.id`, `sf_sitefinity_profile.id -> sf_user_profile.id` | A subclass row cannot outlive its base row |
| **Collection / `seq` tables** (multi-value scalar fields) | `sf_news_items_tags.content_id -> sf_news_items.content_id`, `<mdl>_<typ>_category.base_id -> <module>_<type>.base_id` (a Module Builder taxonomy junction, vowel-stripped), `*_pblshd_translations`, `*_sf_language_data` | Taxonomy and translation junction rows cannot dangle |
| **Join tables** (many-to-many) | `sf_page_node_sf_permissions.id -> sf_page_node.id` AND `.id2 -> sf_permissions.id`, `sf_network_subtaxa`, `sf_facet_facets`, `sf_page_node_translation_siblings` | Both sides of the junction |
| **Settings sub-tables** | `sf_pblshng_pp_sttngs_*` -> `sf_publishing_pipe_settings`, `sf_wfl_lvl_*` -> `sf_wfl_level` | Owned settings rows |

**Everything else is a bare `uniqueidentifier` column with no constraint** - and that is every relationship you actually care about: `sf_page_node.content_id -> sf_page_data`, `sf_page_data.template_id`, `sf_page_data.page_node_id`, `sf_draft_pages.page_id`, `sf_object_data.page_id`, `sf_control_properties.control_id` / `prnt_prop_id`, `sf_object_data.parent_prop_id`, `sibling_id`, `base_control_id`, `original_control_id`, `original_content_id`, `sf_url_data.content_id` / `id2`, `sf_content_link.*_item_id`, `sf_taxa.taxonomy_id` / `parent_id`, `sf_permissions.object_id` / `principal_id`, `sf_meta_fields.type_id`, `sf_media_content.parent_id` / `folder_id`, `ownr` / `last_modified_by`, and **the Module Builder per-type table's `base_id -> sf_dynamic_content.base_id`**.

That last one is a deliberate platform decision, not an accident. `OpenAccessConnection.UpgradeDatabaseSchema` (decompiled, 15.4) filters the generated DDL on MsSql/SqlAzure with:

```csharp
statements.Where(s => !s.EndsWith(" FOREIGN KEY ([base_id]) REFERENCES [sf_dynamic_content]([base_id]) ON DELETE CASCADE")
                   || s.StartsWith("ALTER TABLE [sf_"))
```

So the vertical-inheritance FK that OpenAccess would normally emit for a Module Builder table is dropped from the script unless the table name starts with `sf_`. The `dynmc_*` tables (which inherit from `sf_dynamic_type_base`, not `sf_dynamic_content`) keep theirs. Consequences:

- Module Builder rows are joined by convention only. Verified on the reference DB: 100% of per-type rows had a matching `sf_dynamic_content` row - but nothing stops a partial delete from leaving either side alone.
- **Schema upgrades never drop extra columns or indexes** (`SchemaUpdateProperties.CheckExtraColumns = false`, `CheckExtraIndexes = false`) - an index you add for reporting survives upgrades. Extra **constraints** you add do get dropped by the next schema update unless `appSettings` `PreserveDBExtraConstraints=true` (optionally scoped by `PreserveDBObjectsPrefix`). Do not add your own FKs to `sf_*` tables expecting them to last.

Practical rule: **treat every join in this document as "convention, verify with a LEFT JOIN that counts the misses"** except the four shapes above. The rot catalog at the end lists the misses that actually occur.

## Naming conventions (OpenAccess quirks)

| Quirk | Examples |
|---|---|
| Vowel-dropped column names | `nme` (name), `val` (value), `vrsion`, `ownr`, `commnt`, `lbel`, `dta`, `ky`, `qery`, `prent_template_id`, `lcation`, `frmwrk` |
| Trailing-underscore columns | `title_`, `url_name_`, `description_`, `caption_`, `keywords_`, `content_`, `summary_` - these back localizable `Lstring` properties |
| Vowel-stripped table names | `sf_mdia_content_sf_permissions`, `sf_drft_pages_sf_language_data`, and every long Module Builder junction name - long names get letters squeezed out by the ORM name generator, so **never derive a table name by string manipulation; look it up** (see `sf_meta_data_mapping` below) |
| Sitefinity's own typos | `sf_vesion_items` [sic], `include_script_manger` [sic], `sf_lbraries_thumbnail_profiles` |
| `voa_class` / `voa_version` | OpenAccess type discriminator and optimistic-concurrency counter. `voa_class` matters when one table stores multiple CLR types (`sf_draft_pages`, `sf_object_data`, `sf_dynamic_content`, `sf_media_content`, `sf_libraries`, `sf_taxa`, `sf_taxonomies`, `sf_url_data`, `sf_user_profile`, `sf_form_entry`, `sf_presentation_data`, `sf_meta_attribute`). It is an int derived from the CLR type name; **treat it as opaque and discover it per database** with the `GROUP BY voa_class` plus a type-revealing column (`object_type`, `nme`, `mime_type`, `item_type`, `app_name`) - never hardcode a value |
| `voa_keygen` table | OpenAccess HIGH/LOW key generator infrastructure, not app data |
| **`Guid.Empty` means "unset"** | `sibling_id`, `base_control_id`, `original_control_id`, `original_content_id`, `personalization_*`, `sf_meta_fields.taxonomy_id` use `00000000-0000-0000-0000-000000000000` for "none". Older rows sometimes use NULL instead. **Always treat NULL and Guid.Empty as equivalent "unset"** |
| Numbered duplicate columns | `id2`, `id3`, `id4`, `published2`, `theme2`, `master_page2`, `last_control_id2/3`, `width2/height2`, `category2/3`, `tags2/3` - several CLR classes flat-mapped onto one table each get their own column for the same-named property. Which one a row uses depends on its `voa_class`; the mapping tables below say which |
| `app_name` / `application_name` | The data provider's "application name" (`/Libraries`, `/DynamicModule`, `Sitefinity/`, `/UserProfiles` ...). It scopes rows to a provider and is part of several indexes - include it in WHERE clauses on large tables when you know it |

## Table categories

A mature Sitefinity DB has hundreds of tables in three buckets:

- **`sf_*` tables** - stock Sitefinity (pages, content, libraries/media, taxonomies, security/permissions, forms, versioning, search/publishing, ecommerce, newsletters, multisite, workflow, scheduling, output cache). The large majority.
- **`dynmc_*` tables** - generated by the **Publishing / Search-pipes subsystem, NOT Module Builder**. Verified: every `dynmc_` table maps to an `sf_meta_types` row with `class_name = Dynamic_<32 hex>`, `base_class_name = Telerik.Sitefinity.DynamicTypes.Model.DynamicTypeBase`, `module_name = Telerik.Sitefinity.Publishing.Data.OpenAccessPublishingPointDynamicTypeProvider`, and the 32-hex is an `sf_publishing_point.id` (`storage_type_name = Telerik.Sitefinity.Publishing.Model.Dynamic_<hex>`). Table name = `dynmc_` + the 32-hex with the letters `a` and `e` stripped, truncated to 24 characters (verified: `Dynamic_711734bacf2f48aeb654f803c0d4de66` -> `dynmc_711734bcf2f48b654f803c0d`). Common columns (`id`, `original_item_id`, `original_parent_id`, `application_name`) live in `sf_dynamic_type_base`; per-pipe fields in the `dynmc_` table. Typically only the site-sync tracking type has rows (one per synced item); search-index pipes sit at 0 because Lucene/Elastic hold the documents. **Orphaned `dynmc_` tables whose publishing point was deleted are common debris.**
- **Unprefixed tables** - Module Builder content types plus any custom app tables. Resolve their real names from the registry, never by guessing:

```sql
-- THE table-name registry: Module Builder types AND generated form-entry tables
SELECT module_name, type_name, table_name FROM sf_meta_data_mapping ORDER BY module_name, type_name;
-- Module Builder type metadata (namespace + name + parent type for hierarchies)
SELECT type_namespace, type_name, parentTypeId, is_slf_referencing FROM sf_mb_dynamic_module_type ORDER BY 1, 2;
-- Census: every table with row counts
SELECT t.name, p.rows FROM sys.tables t
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) ORDER BY p.rows DESC;
```

`sf_meta_data_mapping` (`module_name`, `type_name`, `field_name` = `#NA` for the type's own table, `table_name`) is what Sitefinity itself consults; it also lists the vowel-stripped form-entry tables under module `sf_fm`.

## Page composition model

Five tables, and every arrow below is a bare column (no FK):

```
sf_page_node                  the page in the sitemap tree (PageNode)
  | id (PK)  parent_id  root_id  node_type  url_name_  title_  ownr  last_modified_by
  | content_id  --------------------> AUTHORITATIVE pointer to the CURRENT sf_page_data row  (PageNode.Page)
  v
sf_page_data                  ONE row per page (per culture at rest); the published composition (PageData)
  | content_id (PK)  status (0|2)  visible  vrsion  last_control_id  culture  flags  locked_by
  | page_node_id  -> sf_page_node.id   (PageData.NavigationNode; reverse nav; CAN match stale orphan rows)
  | template_id   -> sf_page_templates.id
  v
sf_draft_pages                editing drafts; SHARED table, THREE entity families discriminated by voa_class
  | id (PK)  is_temp_draft  vrsion  flags  template_id (the draft's OWN template choice)
  | PageDraft:      page_id -> sf_page_data.content_id      last_control_id2   (nme NULL, content_id NULL)
  | TemplateDraft:  page_id -> sf_page_templates.id         last_control_id    ky = template key
  | FormDraft:      content_id -> sf_form_description.content_id   last_control_id3   nme = form name
  v
sf_object_data                every control/widget instance AND every nested complex object; SEVEN families
  | id (PK)  object_type  place_holder  sibling_id  is_layout_control  caption_  voa_class  vrsn
  | owner column depends on the family (see matrix below): page_id | content_id | id3 | parent_prop_id
  | base_control_id     -> the TEMPLATE control this page-level row overrides (is_overrided_control = 1)
  | original_control_id -> on a DRAFT control: the LIVE control it mirrors (publish keys on this)
  | id2 -> sf_presentation_data.id (CurrentPresentation)   parent_id -> another sf_object_data row (nesting)
  v
sf_control_properties         the property tree (ControlProperty)
  | id (PK)  nme varchar(50)  val nvarchar(MAX)  language  ordinal
  | Level 1:  control_id = the sf_object_data.id, prnt_prop_id NULL
  | Level 2+: control_id = NULL,  prnt_prop_id = parent property row id
```

### sf_object_data: the seven families and their owner columns

Decompiled mappings (`PagesFluentMapping`, `ObjectDataFluentMapping`, `FormsFluentMapping`), confirmed by the data:

| CLR type | Owner column | Owner table | Notes |
|---|---|---|---|
| `PageControl` | `page_id` | `sf_page_data.content_id` | The LIVE composition. Only these render on the public site |
| `PageDraftControl` | `page_id` | `sf_draft_pages.id` (PageDraft) | `original_control_id` -> the live `PageControl` it mirrors |
| `TemplateControl` | `page_id` | `sf_page_templates.id` | Template-owned widgets; pages merge them at render/edit time, never copy them |
| `TemplateDraftControl` | `page_id` | `sf_draft_pages.id` (TemplateDraft) | |
| `FormControl` | `content_id` | `sf_form_description.content_id` | Form fields are controls; `published` column |
| `FormDraftControl` | `id3` | `sf_draft_pages.id` (FormDraft) | `published2` column. **Not `page_id`, not `content_id`** |
| `ObjectData` (plain) | `parent_prop_id` | `sf_control_properties.id` | Nested complex property values (`ChoiceItem`, list members), ordered by `collection_index`, keyed by `dictionary_key` |

`page_id` is therefore polymorphic across four owners and is NULL for the other three families. **Classify a row's family (by `voa_class`, or by which owner column is populated) before drawing conclusions from it.** On the reference DB the split was roughly 35k live page controls, 18k page-draft controls, 16k nested objects, 12k form controls, and under 100 template controls - most of what is in this table does not render.

Discover the families on your database:

```sql
SELECT voa_class, COUNT(*) rows_,
       SUM(CASE WHEN page_id IS NOT NULL THEN 1 ELSE 0 END) via_page_id,
       SUM(CASE WHEN content_id IS NOT NULL THEN 1 ELSE 0 END) via_content_id,
       SUM(CASE WHEN id3 IS NOT NULL THEN 1 ELSE 0 END) via_id3,
       SUM(CASE WHEN parent_prop_id IS NOT NULL THEN 1 ELSE 0 END) via_parent_prop,
       MIN(object_type) sample_type
FROM sf_object_data GROUP BY voa_class;
```

### sf_draft_pages: the three families

```sql
SELECT voa_class, is_temp_draft, COUNT(*) rows_,
       SUM(CASE WHEN page_id IS NOT NULL THEN 1 ELSE 0 END) page_drafts_or_template_drafts,
       SUM(CASE WHEN content_id IS NOT NULL THEN 1 ELSE 0 END) form_drafts, MIN(nme) sample_form_name
FROM sf_draft_pages GROUP BY voa_class, is_temp_draft;
```

PageDraft and TemplateDraft both use `page_id` (distinguish by joining `sf_page_data` vs `sf_page_templates`, or by `voa_class`); FormDraft uses `content_id` and carries the form `nme`. The three families also use three different `last_control_id` columns (`last_control_id2` = PageDraft, `last_control_id` = TemplateDraft, `last_control_id3` = FormDraft).

### Key mechanics

1. **The property tree recurses through objects**: control (object_data) -> properties (control_properties) -> complex value (object_data with `parent_prop_id`) -> its properties -> ... `collection_index` orders list members; `dictionary_key` keys dictionary members. Level-2+ rows have `control_id = NULL` - they are reachable only through `prnt_prop_id`. `sf_control_properties` is mapped with `DeletesOrphans()` - the ORM deletes property rows that lose their parent; raw SQL that deletes a control must delete its property subtree itself.
2. **Render order** is a per-placeholder linked list on `sibling_id`: each control points to the control BEFORE it; `Guid.Empty` (or NULL) marks the head. No sort column exists (`ordinal` on `sf_control_properties` orders properties, not controls). Template inheritance can legally produce multiple Empty-heads in one placeholder. There is an index on `sibling_id`.
3. **Template merge, not copy.** A page's live composition holds only page-level controls; template controls stay under the template's id and are merged when the page renders or opens in the editor. A page-level row with `is_overrided_control = 1` and `base_control_id = <template control id>` is a page override of a template control.

### MVC widgets

- `object_type` for classic MVC widgets = `Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy`; Feather dynamic widgets use `...Frontend.Mvc.Infrastructure.Controllers.MvcWidgetProxy`. `object_type` strings are written at creation time and never normalized - old rows can carry full assembly-qualified names with ancient version numbers. **Match with `LIKE '%TypeName%'`, never equality.**
- Level-1 properties: `ControllerName` = **full CLR type name** (`[PropertyPersistence(IsKey = true)]`, always persisted), `ID` = a page-scoped control id like `C005`, and - only if configured - a `Settings` container row (`val` NULL) whose CHILDREN hold designer values.
- **Persistence is sparse (default-instance diffing)**: on save, `PropertyPersister.DoPersist => !object.Equals(liveValue, defaultInstanceValue)` against a fresh `new T()` (`[DefaultValue]` is only a fallback when a getter throws). Key properties bypass the diff. The `Settings` container is deleted outright if zero children survive - a freshly-dropped widget has exactly 2 rows (ControllerName, ID) and NO Settings row. Child property names match the controller's C# property names exactly.
- Scalars are written via `TypeConverter.ConvertToInvariantString` ("True", "False", "42"). `[PropertyPersistence(PersistAsJson = true)]` stores the whole graph as ONE JSON row; without it each list item becomes an `sf_object_data` row (`parent_prop_id` + `collection_index`) - and a list item whose values all equal that type's defaults produces ZERO rows and silently vanishes (`ListPropertyPersister`). Mark complex `IList<>` widget properties `PersistAsJson = true`.
- The `Cnnn` ID comes from `ControlManager.SetControlId`: `num = Math.Max(LastControlId + 1, Controls.Count() + 1)`, value = `"C" + num.ToString("#000")` (templates use the template `Key` as the prefix). The counter lives in `sf_page_data.last_control_id` / the draft's `last_control_id*` column.

### Layout controls and placeholders

- Feather grids are `Telerik.Sitefinity.Frontend.GridSystem.GridControl` rows (`is_layout_control = 1`) whose `Layout` property holds the grid template path (e.g. `~/Frontend-Assembly/Telerik.Sitefinity.Frontend/GridSystem/Templates/grid-6+6.html`). Pre-Feather sites also have legacy `Telerik.Sitefinity.Web.UI.LayoutControl` rows.
- Placeholder naming: top-level placeholder names come from the template markup (site-specific - discover with `SELECT DISTINCT place_holder FROM sf_object_data WHERE page_id = ...`). A layout control whose `ID` property is `C308` creates child placeholders `C308_Col00`, `C308_Col01`, ...; content widgets reference those strings in `place_holder` (regex `^(.+)_Col(\d{2})$`).

### Templates

`sf_page_templates`: PK `id`, `nme`, `title_`, `framework` (enum `PageTemplateFramework`: Hybrid = 0, Mvc = 1, WebForms = 2; NULL on ancient backend templates), `prent_template_id` (template-inheritance chain; indexed), `ky` (the template key used as the control-id prefix), `layout_path` / `master_page`, `renderer` (populated only for Core-renderer templates). Template-owned widgets live in `sf_object_data` with `page_id = template.id`. Template drafts live in `sf_draft_pages` with `page_id = template.id`. Template permissions: `sf_pg_templates_sf_permissions` (join, FK-backed).

**`nme` is NOT unique** - stock installs carry several templates named `Bootstrap4.default`, and nothing stops duplicate custom names. **Resolve templates by `id` in any automation.**

### Presentation data

`sf_presentation_data` is another flat-mapped multi-type table: `ControlPresentation` (a control's saved widget template, `item_id` -> `sf_object_data.id`, `control_type`, `friendly_control_name`, `data_type = ASP_NET_TEMPLATE`, `dta` = the markup), `PropertyPresentation` (`item_id` -> property), `PagePresentation` / `PageDraftPresentation` (`item_id`), `TemplatePresentation` (`id4`), `TemplateDraftPresentation` (`item_id`), `DraftPresentation` (`id3`), `FormPresentation` (`id2`), `MetaTypePresentation`. Widget templates edited in the backend (Design > Widget Templates) are these rows, versioned in `sf_vesion_items` as `Telerik.Sitefinity.Pages.Model.ControlPresentation`.

## Content lifecycle: an item exists once per state

Enum `Telerik.Sitefinity.GenericContent.Model.ContentLifecycleStatus` (decompiled):

| Name | Value | Meaning |
|---|---|---|
| Master | 0 | The editable source-of-truth copy. **Always exists.** The backend edits this |
| Temp | 1 | Transient checkout copy while an editor has the item open; marks the item locked |
| Live | 2 | The snapshot of the master that the public site renders |
| Deleted | 4 | Soft-deleted (Recycle Bin) |
| PartialTemp | 8 | Multilingual partial-edit state |

**The states are separate ROWS, not a flag on one row.** Pages and generic content store those rows differently:

### Generic content and Module Builder content: N rows per item, one per state

Each state is its own row **with its own primary key**, in the same table, linked by `original_content_id`:

- The **Master** row has `status = 0` and `original_content_id = Guid.Empty`. Its id is the item's public identity (what the API, URLs, related-data links, permissions, versions, and approval tracking all reference).
- The **Live** row has `status = 2` and `original_content_id = <master id>`. It has a **different id**. Verified: 16,014 of 16,014 live `sf_dynamic_content` rows resolve to a master via `original_content_id`; 32,392 of 32,392 live media rows do the same.
- A **Temp** row (`status = 1`, `original_content_id = <master id>`) appears only while an editor has the item open; the `TempItemsCleanupTask` scheduled task removes stale ones.
- A **Deleted** row (`status = 4`) is the Recycle Bin copy; `sf_rbin_item` holds the bin's index (`del_item_id`, `del_item_type_name`, `del_item_titles_path`).
- An item that has never been published has ONLY a master row. Types whose items are created by code and never published (verified: one Module Builder type with 10,826 master rows and zero live rows) look "all unpublished" - that is normal.

Everything that points at an item points at the **master** id: `sf_url_data.content_id` (52,485 of 54,071 `/DynamicModule` rows resolve to `base_id`; the rest are debris), `sf_content_link.parent_item_id` / `child_item_id`, `sf_permissions.object_id`, `sf_vesion_items.id`, `sf_approval_tracking_record.workflow_item_id`, `sf_taxonomy_statistic.taxon_id`. To read what the public sees you still filter `status = 2`, but you carry the master id around.

```sql
-- Master / Live pairing for a Module Builder type (join on base_id; status lives ONLY on sf_dynamic_content)
SELECT m.base_id AS master_id, l.base_id AS live_id, m.status, l.status, d.url_name_
FROM sf_dynamic_content m
LEFT JOIN sf_dynamic_content l ON l.original_content_id = m.base_id AND l.status = 2
JOIN <module>_<type> t ON t.base_id = m.base_id
JOIN sf_dynamic_content d ON d.base_id = m.base_id
WHERE m.status = 0;
```

### Pages: ONE live row plus draft rows in another table

`sf_page_data` holds one row per page (per culture) at rest: `status = 2` if published, `status = 0` (with `visible = 0`) if never published or unpublished. `original_content_id` is NULL. There is no master/temp copy of `sf_page_data`. Editing happens through **PageDraft rows in `sf_draft_pages`**:

- `is_temp_draft = 0` is the **master draft** (the equivalent of Master for pages); `is_temp_draft = 1` is the per-user **checkout copy** (`PageManager.EditPage` returns it). Each draft has its own `template_id`, its own `vrsion`, and **its own full copy of the controls** (`PageDraftControl` rows under `page_id = draft.id`, each mirroring a live control via `original_control_id`).
- So a page's widgets exist up to three times: live (`PageControl` under `sf_page_data`), master draft, and temp draft - plus one more time inside every version snapshot (see Version history).
- `sf_page_data.flags` bit 1 = "master draft is Synced with live" (set by CheckIn, cleared when a fresh master draft is created; `LifecycleDecoratorPages.CopyDuringCheckOut => !master.Synced`).

Verified publish flow (`PageManager.PublishPageDraft` -> `LifecycleDecoratorPages`): CheckIn copies temp -> master (controls, `template_id`, `last_control_id`, version) with `deleteTemp: false` (`PageManager.DeleteTempAfterPublish => false` in source - **temp drafts persist by design** and accumulate for years), and briefly sets `sf_page_data.status = 0`; Publish guards `pageData.Version > draft.Version` (throws "modified by someone else"), then sets `vrsion + 1`, `status = 2`, `visible = 1` (always), `locked_by = Empty`, `publication_date` on the FIRST publish only (`draft.ParentPage.Version > 0` check), and mirror-syncs controls onto the live row.

**The mirror-sync (`ControlManager.CopyControls`, direction `CopyToOriginal`)**: for each draft control, find the live control whose `Id == draft.OriginalControlId`; if found, update it IN PLACE (live control ids are stable across publishes); if not found, create a NEW live row and back-stamp `draft.OriginalControlId = newLiveId`; any live control no draft control claimed is DELETED (with its property subtree). `parent_id` links between controls are remapped through the same id map. This is why the live composition's control ids survive ordinary edits but a control you delete and recreate gets a fresh id.

## Version history (sf_version_*): what a snapshot holds and how a revert replays it

Version history is NOT relational - it is compressed .NET-serialized snapshots (`SitefinityBinaryFormatter`, `Telerik.Sitefinity.Versioning.Serialization`), one per saved version:

- `sf_version_chnges`: one row per version. `item_id` = the versioned item's id (**for pages: `sf_page_data.content_id`**, verified 6,254 of 6,256 page versions resolve there), `dta` = the compressed blob, `commnt` = the History UI comment, `is_published_version`, `change_type` (`add` first version, `edit` draft save, `publish`), `culture` (LCID; 127 = invariant), `metadata` (language list), `serial_info_id -> sf_version_serial_info` (which formatter wrote it). Indexed on `item_id` and on `(is_published_version, vrsion, last_modified, item_id, culture)`.
- `sf_vesion_items`: one row per versioned item - `id` = item id, `type_name` (pages are versioned as `Telerik.Sitefinity.Pages.Model.PageDraft` - the draft is what gets serialized; templates as `TemplateDraft`; widget templates as `ControlPresentation`; content types by their model type), `last_version`, `trunk_id -> sf_version_trunks`.
- `vrsion` encoding (`VersionDataProvider.BuildFullVersionNumber`): **`major * 10000 + minor`**. `CreateVersion(isPublished: true)` calls `IncrementMajorVersion` (10000, 20000, ...); `isPublished: false` increments the minor (10001, 10002, ...). `Change.Version` in C# is the RAW value; the UI renders `major + "." + minor`. `ORDER BY vrsion DESC` finds the latest.
- **Publishing through `PageManager.PublishPageDraft` writes NO version row.** Snapshots are created by the CALLING layer - the backend page editor (`ZoneEditorService`) calls `VersionManager.CreateVersion(draft, pageDataId, isPublished: true)` after publishing. Custom code that publishes via `PageManager` must do the same or no History entry appears.
- `sf_version_dependency` / `sf_vrsn_chngs_sf_vrsn_dpndncy`: per-version dependency records (`IHasVersionDependency`) for cleanup; `sf_version_trunks` / `sf_version_serial_info`: supporting structure.

**What a PageDraft snapshot contains** (decompiled `PageDraft.Serialize`, `ControlData.Serialize`, `ObjectData.Serialize`, plus the `[NonSerializableProperty]` markers):

- The draft's scalar properties (`TemplateId`, `LastControlId`, `MasterPage`, `Flags`, ...) **except** `Id`, `Version`, `IsTempDraft`, `Status`, `Visible`, `Owner`, `ParentPage`/`ParentId`, `Theme`, approval state.
- Every control, individually binary-serialized and keyed by its draft control id: all `ControlData` properties (`ObjectType`, `PlaceHolder`, `SiblingId`, `Caption`, `IsLayoutControl`, `BaseControlId`, **`OriginalControlId`**, `ParentId`, personalization fields, `Version`) **except** `Id`, `Presentation` and `SupportedPermissionSets`; its full `Properties` tree (recursively, including nested `ObjectData` list items); its `Permissions`; its `Presentation` entries; its personalized variants.
- The draft's `Presentation` entries.

**What a revert does** (`PagesService.CopyVersionToDraft`, the code behind the History UI's "revert to this version"):

1. `PageManager.EditPage(pageDataId, lockIt: true)` - a temp draft as usual.
2. `VersionManager.GetSpecificVersion(draft, pageDataId, change.Version)` deserializes the blob **onto that draft**: every existing draft control is removed from the ORM context, `draft.Controls` is cleared, and NEW `PageDraftControl` objects are created from the snapshot (`ObjectData.Deserialize` starts with `Id = Guid.NewGuid()`). Their `OriginalControlId` values come from the snapshot, so on publish they still mirror the same live control ids they mirrored when the snapshot was taken. The draft's `TemplateId` is restored to the snapshot's value.
3. Per-control `Version` counters are re-stamped (max of current + 1, keyed by `OriginalControlId`) and `draft.Version++`, then `SaveChanges()`. **A revert only rewrites the draft. Nothing is published** - the editor then publishes (or discards) that draft like any other edit.

Consequences for migration rollback:

- A pre-migration snapshot (`CreateVersion(draft, pageDataId, isPublished: false)` taken BEFORE mutating the draft) is a complete, replayable record of the composition: controls, property trees, permissions, sibling order, template id. Restoring it and publishing puts the live page back as it was, with live control ids preserved wherever the migration updated controls in place (same `OriginalControlId`) and fresh ids where the migration had deleted them.
- The snapshot stores the template by **id**. If the migration deletes or replaces the old template, the reverted draft points at a missing template. Keep old templates until every page is verified.
- `Theme` and page-node data (`sf_page_node` title, URL, parent, permissions on the node) are NOT in the snapshot; `sf_page_data` metadata (`html_title_`, `description_`, `keywords_`, cache profile) is copied from the draft on publish but only the draft-level properties that the serializer walks are restored. Verify after a revert; do not assume node-level changes roll back.
- Widget templates edited through the backend are `ControlPresentation` rows with their own version history - a page revert restores the control's *reference* to a presentation, not the presentation's markup.
- Version blobs are opaque to SQL. You cannot reconstruct an old composition with SQL alone; use `VersionManager.GetSpecificVersion` (or the History UI).

```sql
-- Version history for a page (item_id = the page DATA id, not the node id)
SELECT vrsion / 10000 AS major, vrsion % 10000 AS minor, change_type, is_published_version,
       LEFT(commnt, 80) AS commnt, last_modified, DATALENGTH(dta) AS blob_bytes
FROM sf_version_chnges WHERE item_id = '<sf_page_data.content_id>' ORDER BY vrsion DESC;
```

## Built-in content modules vs Module Builder types (two storage generations)

Both generations share the lifecycle semantics above; what differs is where the columns live:

| | Built-in modules | Module Builder (dynamic) types |
|---|---|---|
| Physical layout | **One table per type, everything inline** (`InheritanceStrategy.Horizontal` on the `Content` base): `sf_news_items`, `sf_events`, `sf_blog_posts`, `sf_list_items`, `sf_content_items`, `sf_media_content`, `sf_libraries`, `sf_commnt`, `sf_form_description` each carry the FULL Content column set (`content_id` PK, `status`, `original_content_id`, `vrsion`, `visible`, `publication_date`, `ownr`, `url_name_`, `title_`, `description_`, `draft_culture`, `content_state`, `approval_workflow_state_`, `votes_*`, `views_count`, ...) plus type-specific columns | **Split storage** (`DatabaseInheritanceType.vertical = 1` in `sf_meta_types.database_inheritance`): lifecycle columns for ALL types live in the shared `sf_dynamic_content` (`voa_class` discriminates the type); the per-type table holds only custom fields + `parent_id`, joined on `base_id` (no FK - see above) |
| Item id column | `content_id` | `base_id` (the CLR `Id` maps to `base_id`; the nullable `id` column on `sf_dynamic_content` is legacy and mostly NULL - never join on it) |
| Query for live items | `WHERE status = 2` on the type's own table | Must join `sf_dynamic_content` for `status`, `visible`, `url_name_`, `publication_date`, `ownr` |
| Parent/child hierarchy | `parent_id` on the type table (`sf_list_items.parent_id -> sf_lists`, `sf_media_content.parent_id -> sf_libraries`, `sf_blog_posts.parent_id -> sf_blogs`, `sf_events.parent_id`) | `parent_id` on the per-type table -> the parent type's `base_id`; ALSO mirrored as `sf_dynamic_content.system_parent_id` + `system_parent_type` (verified equal on every row) |
| Custom fields added later | **Real columns added to the stock table** (`sf_meta_fields` rows on a meta type with `is_dynamic = 0`) | Columns in the per-type table (`is_dynamic = 1`) |
| Taxonomy / multi-value fields | Junction tables `{table}_{field}`: `sf_content_items_category`, `sf_list_items_tags`, `sf_media_content_tags2` ... (`content_id`, `seq`, `val -> sf_taxa.id`; FK to the owner only) | Junction tables `{type}_{field}` vowel-stripped (look the name up in `sf_meta_data_mapping`; columns `base_id`, `seq`, `val -> sf_taxa.id`) |
| Translations / language data | `{table}_sf_language_data` (`content_id`, `seq`, `id -> sf_language_data.id`) and `{table}_pblshd_translations` (`content_id`, `seq`, `val` = culture code) | `sf_dynmc_cntnt_sf_lnguage_data` / `sf_dynmc_cntnt_pblshd_trnsltns` on `base_id` |
| URL rows (`sf_url_data.app_name`) | `/News`, `/Events`, `/Blogs`, `/Lists`, `/Generic_Content`, `/Libraries`, `/SystemLibraries`, `/UserProfiles` | All under `/DynamicModule`, with `item_type` = the full type name |
| Permissions | `{table}_sf_permissions` join (`content_id`, `id -> sf_permissions.id`) | `sf_dynmc_cntent_sf_permissions` (`base_id`, `id`) |

Additional built-in specifics (all verified):

- **Libraries are flat-mapped single-table inheritance**: `sf_media_content` holds Image, Video AND Document rows discriminated by `voa_class` (discover with `GROUP BY voa_class` + `MIN(mime_type)`); `width`/`height` belong to one class and `width2`/`height2` to another; taxonomy junctions come numbered per class (`_category`, `_category2`, `_category3`, `_tags`, `_tags2`, `_tags3`). `sf_libraries` holds Album, DocumentLibrary and VideoLibrary the same way. `parent_id -> sf_libraries.content_id` (100% resolve), `folder_id -> sf_folders.id` (populated only for items inside a folder), `sf_media_file_links` (one per item + culture: `file_id`, `file_path`, `total_size`, `mime_type`) -> `sf_media_file_urls` (`media_file_link_id`, current + historical file URLs), `sf_media_thumbnails`, `sf_lbraries_thumbnail_profiles`. Binaries are on disk/blob storage (`blob_storage` column names the provider) unless `sf_chunks` has rows.
- **Shared content blocks are ContentItems**: a ContentBlock widget with "shared content" stores a `SharedContentID` Settings property pointing at `sf_content_items.content_id`; a non-shared block keeps its HTML in the widget's own property rows. **Media and page references inside that HTML are stored as `sfref="[images|OpenAccessDataProvider]<guid>"` dynamic-link keys, not URLs** (verified in `sf_control_properties.val`); the `src`/`href` beside them is a stale copy and the platform re-resolves the key at render (`LinkParser` / `DynamicLinksParser` - see `sitefinity-widget-expert`). A SQL rewrite of URLs inside content is therefore pointless; a SQL rewrite of the GUIDs is how you re-point content at a replacement image. `sf_content_relation` (`subject_type = ContentItem`, `relation_type = 'Contains'`, `object_type = PageData`) tracks which pages use a shared block.
- **Forms are page-like compositions**: the STRUCTURE is `sf_form_description` (Content-derived; `nme` = the form's table-ish name, `frmwrk` = framework, `form_entries_seed`), its drafts are `FormDraft` rows in `sf_draft_pages` (`content_id`), its fields are `FormControl` / `FormDraftControl` rows in `sf_object_data`; the RESPONSES are `sf_form_entry` (`voa_class` = the form, `user_id`, `ip_address`, `submitted_on`, `lnguage`, `source_site_id`) **vertically extended by a generated per-form table** (`sf_forms_frm_*` / vowel-stripped `sf_frms_frm_*` / `sf_fm_*`, PK `id` with an FK to `sf_form_entry.id`), one column per field. Deleting or renaming fields leaves orphan columns.
- **Comments** hang off content via `{table}_sf_commnt` junctions (`content_id`, `content_id2 -> sf_commnt.content_id`); `sf_commnt.commented_item_i_d` + `commented_item_type` point back.

When you meet an unfamiliar table, classify it before theorizing: `sf_meta_data_mapping` (is it a registered type table?), `sf_meta_types` / `sf_meta_fields` (a declared field store?), `sf_mb_dynamic_module_type` (Module Builder?), and the `{table}_{field}` junction pattern (a custom taxonomy on a built-in?). A "mystery" table is usually a custom field someone added to a stock type years ago.

## Module Builder and the metadata model

- **Registries (what the admin UI edits)**: `sf_mb_dynamic_module` (modules), `sf_mb_dynamic_module_type` (types; `type_namespace = Telerik.Sitefinity.DynamicTypes.Model.<Module>`, `parentTypeId` for hierarchies, `main_short_text_field_name`), `sf_mb_dynamic_module_field` (designer-level fields: `field_type`, `column_name`, `classification_id` for taxonomy fields, `related_data_type` / `related_data_provider` for related-data fields, `choices`), `sf_mb_fields_backend_section`, `sf_mb_dnc_cnt_provider`.
- **The ORM meta model (what OpenAccess maps at runtime)**: `sf_meta_types` (`name_space`, `class_name`, `base_class_name`, `database_inheritance` = `DatabaseInheritanceType` flat 0 / vertical 1 / horizontal 2, `is_dynamic`, `is_deleted`, `parent_type_id`), `sf_meta_fields` (`type_id -> sf_meta_types.id`, `field_name`, `column_name` (NULL for taxonomy and related-data fields - they have no column), `clr_type`, `db_type`, `db_sql_type`, `taxonomy_id` (Guid.Empty when none), `is_single_taxon`, `is_dynamic`, `is_deleted`, `allow_multiple_relations`), `sf_meta_attribute` (flat-mapped `MetaTypeAttribute` rows keyed on `id2 = meta type id` and `MetaFieldAttribute` rows keyed on `id2 = meta field id`; carries `moduleName`, `mainPropertyName`, and field UI attributes), `sf_meta_types_section_names`, `sf_meta_index` / `sf_meta_index_composite_fields` (declared indexes), `sf_meta_type_descriptions`, `sf_schema_vrsns` (+ `_meta_types`) and `sf_module_vrsn` (per-provider schema/module version stamps - read `sf_schema_vrsns` to see which assembly version last upgraded each provider's schema).
- The 13 `is_dynamic = 0` meta types are the stock types that accept custom fields (ListItem, PageData, PageNode, Image, Video, Document, NewsItem, Event, BlogPost, ContentItem, FormDescription, SitefinityProfile, Product).
- **Table creation** runs through the OpenAccess schema updater at startup with the MB base-table FK filtered out (see the FK section). Table and column names are chosen by the ORM name generator (`DefaultNamingStrategy`), which is where the vowel-stripping comes from; consult `sf_meta_data_mapping` and `sf_meta_fields.column_name` rather than predicting.
- Per-type tables have no indexes beyond the PK and no status column. Live items:

```sql
SELECT t.*, d.url_name_, d.publication_date
FROM <module>_<type> t
JOIN sf_dynamic_content d ON d.base_id = t.base_id
WHERE d.status = 2 AND d.visible = 1;
-- sf_dynamic_content is indexed on (application_name, status, visible), original_content_id, url_name_, (application_name, system_parent_id), system_src_key
```

- Fields declared as RelatedData / RelatedMedia have NO column at all - see `sf_content_link`.

## Related data without FKs: sf_content_link

`sf_content_link` connects RelatedData / RelatedMedia / "related items" fields: `parent_item_id` + `parent_item_type` + `parent_item_provider_name` + `component_property_name` -> `child_item_id` + `child_item_type` + `child_item_provider_name`, with `ordinal` for order, `is_parent_deleted` / `is_child_deleted` soft flags, and `available_for_temp` / `available_for_master` / `available_for_live` tinyints scoping each link to lifecycle states (a master edit creates master-scoped links that flip live on publish). Both item ids are **master** ids. Indexed on `parent_item_id`, `child_item_id`, `(child_item_type, child_item_id)`.

Verified nuances:

- Links whose parent has no lifecycle (a `SitefinityProfile` `Avatar` -> `Image`) carry all three `available_for_*` flags = 0. Zero flags does not mean "dead link".
- App code can use the same table for arbitrary item-to-item relations, so expect site-specific `component_property_name` values and very large row counts (100k+ on the reference DB, dominated by one custom relation). Discover with `GROUP BY parent_item_type, component_property_name, child_item_type`.
- `sf_content_link_attrbutes` (`id`, `mapkey`, `val`) holds per-link attributes (empty on most sites).

## URLs: sf_url_data

`sf_url_data` holds every current AND historical URL. Flat-mapped per owning type (`voa_class` discriminates `PageUrlData`, `DynamicContentUrlData`, `ContentItemUrlData`, `MediaUrlData`, `LibraryUrlData`, `UserProfileUrlData`, ...):

- **Pages**: `app_name = 'Sitefinity/'`, the page **node** id in `id2` (`PageNode.Urls` maps to `id2`; verified 414 of 414 non-null `id2` rows resolve to `sf_page_node`, none to `sf_page_data`), `content_id` / `item_type` NULL. Page URL rows with NULL `id2` were all `redirect = 1` rows on the reference DB - historical URLs whose node no longer exists.
- **Content items**: `app_name = '/DynamicModule'` (+ `item_type` = full type name), `/Libraries`, `/SystemLibraries`, `/Lists`, `/Generic_Content`, `/News`, `/Events`, `/Blogs`, `/UserProfiles`, with `content_id` = the **master** item id.
- `is_default = 1` marks the canonical URL; old URLs remain as `redirect = 1` rows; `disabled`, `qery` (query string), `culture` (LCID; **127 = invariant** on monolingual sites). Indexed on `url`, `content_id`, `id2`, `(item_type, url)`.

## Taxonomies

`sf_taxonomies` (flat-mapped `HierarchicalTaxonomy` / `FlatTaxonomy` by `voa_class`; `nme` = `Categories`, `Tags`, `Departments`, `PageTemplates` ..., `taxon_name_`, `root_id`) and `sf_taxa` (`taxonomy_id -> sf_taxonomies.id`, `parent_id -> sf_taxa.id` for hierarchical, `ordinal`, `status`, `url_name_`, `title_`, `nme`; `HierarchicalTaxon` / `FlatTaxon` by `voa_class`; `fct_txn_fct_tx_id` for facets). Assignment to items goes through the `{table}_{field}` junction tables (`val -> sf_taxa.id`; verified 100% resolve on both a stock and a Module Builder junction). `sf_taxonomy_statistic` caches `marked_items_count` per taxon per item type; `sf_synonyms`, `sf_network_subtaxa` / `sf_network_supertaxa` (taxon networks, FK-backed join tables), `sf_taxa_attrbutes`.

## Security: users, roles, permissions

- `sf_users` (`user_name`, `email`, `passwd` + `salt` + `password_format`, `is_backend_user`, `is_approved`, `ext_provider_name` / `ext_id` for external logins, `manager_info -> sf_manager_info` = which membership provider). `sf_roles` (`nme`, `app_name` = `Backend/` for backend roles, `App/` for application roles). `sf_user_link` (`user_id`, `role_id`, `membership_info`; the user<->role assignment; indexed on `(app_name, user_id, role_id)` and `role_id`). `sf_user_profile` (`user_id -> sf_users.id`; flat-mapped profile types by `voa_class`) vertically extended by `sf_sitefinity_profile` (FK-backed) and any custom profile type (which gets its own vertical table with an FK to `sf_user_profile`, like any subclass table).
- `sf_permissions`: one row per (`set_name`, `object_id`, `principal_id`) with `grnt` and `deny` bitmasks. `principal_id` is a role id (99% on the reference DB) or a user id. `object_id` is the secured object's id: `sf_page_node.id` (set `Pages`), `sf_object_data.id` (sets `Controls`, `LayoutElement` - widget-level permissions, by far the most rows), `sf_media_content.content_id` (`Image` / `Video` / `Document`), `sf_libraries.content_id` (`Album` / `DocumentLibrary` / `VideoLibrary`), `sf_dynamic_content.base_id` (`General`), `sf_page_templates.id` (`PageTemplates`), `sf_taxonomies.id` (`Taxonomies`), `sf_security_roots.id` (provider-level defaults - `ky` = the data provider name, e.g. `PageDataProviderOpenAccessDataProvider`, set `Backend` / `General` / module sets), plus `Forms`, `Comments`, `List` / `ListItem`, `Blog` / `BlogPost`, `Forum`, `WorkflowDefinition`, `Site`, `DynamicFields`, `SitemapGeneration`, `ABTests`. Indexed on `(object_id, principal_id, app_name, set_name)` and `set_name`.
- **Bit values**: each action's bit is `2 ^ (its index in the set's action list)` as declared in `SecurityConfig` (`Permission.GetActionValue`, decompiled); the list is config-driven and extensible. Stock `Pages` set in declaration order: View = 1, CreateChildControls = 2, EditContent = 4, Create = 8, Modify = 16, Delete = 32, ChangeOwner = 64, ChangePermissions = 128, Unlock = 256. Read the authoritative values from `Config.Get<SecurityConfig>().Permissions["<set>"].Actions["<action>"].Value` rather than trusting a table.
- Each secured object type has a `{table}_sf_permissions` join (`id` / `content_id` -> object, `id2` / `id` -> `sf_permissions.id`; FK-backed) listing the permission rows that apply to it, and `inherits_permissions` / `can_inherit_permissions` columns. `sf_permissions_inheritance_map` (`object_id`, `child_object_id`, `child_object_type_name` = a `voa_class`-style int) records which children inherit from which parent. `sf_scrty_rts_*` / `sf_pg_nd_prmssnst_*` / `sf_txnms_*` hold each root's supported permission sets and the object-title resources.
- `sf_approval_tracking_record` (`workflow_item_id`, `user_id`, `status` = `Draft` / `Published` / `Unpublished` / `Scheduled`, `note`, `culture`) is the lifecycle audit trail. `workflow_item_id` is the **master content id** for content (`sf_dynamic_content.base_id`, `sf_media_content.content_id`) and the **page NODE id** for pages (verified 33,323 rows resolve to `sf_page_node`, 0 to `sf_page_data`). It is written whether or not approval workflows are defined - expect it to be one of the largest tables while the `sf_wfl_*` / `sf_workflow_*` tables stay tiny.
- `sf_auth_tokens`, `sf_lic_user_activity`, `sf_user_action`, `sf_user_preferences`, `sf_idsrv_*`: sessions, licensing seats, one-off user actions, backend preferences, IdentityServer keys/consents.

## Multisite (present even on single-site installs)

`sf_sites` (`nme`, `is_default`, `site_map_root_node_id -> sf_page_node.id` (the site's page root), `home_page_id`, `default_frontend_template_id`, `default_culture_key`, `live_url`, `staging_url`, `front_end_login_page_id`); `sf_sites_culture_keys`, `sf_sites_domain_aliases`, `sf_sites_sf_permissions` (FK-backed collections). `sf_site_data_src` (`nme`, `provider`, `owner_site_id`) / `sf_site_data_src_lnks` (`site_id -> sf_sites.id`, `data_source_id -> sf_site_data_src.id`; the table the 15.4 `MultisiteFluentMapping` actually maps): which data providers (module providers, including one per Module Builder module) each site is linked to. The similarly named `sf_site_data_source_links` (`site_id`, `provider_name`, `dataSource_name`, `is_default`) is the legacy name-based shape. It still exists (same row count as the mapped table on the reference DB) but no fluent mapping targets it; the only 15.4 code that reads it is `OpenAccessMultisiteProvider.UpgradeDataSourceLinks`, a schema-upgrade step that copies its rows into `sf_site_data_src_lnks`. Treat it as frozen upgrade residue, not live data. `sf_site_item_links` (PK `item_id`, `item_type`, `site_id`): per-item site links - only for a few item types (widget templates / `ControlPresentation`, forms, publishing points, page templates and their presentations); page and content items get their site through their provider link and page root, not through this table.

## Multilingual columns on monolingual sites

If the site is single-language: `culture` columns hold the language code, an empty string, or NULL (rows predate the culture migration - the reference DB has all three on `sf_page_data`), `sf_language_data` rows exist with `language` NULL / `''` / `'en'` mixed, the `*_sf_language_data` and `*_pblshd_translations` junctions are populated (with the site's one culture) as infrastructure noise, `sf_url_data.culture = 127` everywhere, and `PartialTemp` status / `ChildrenLocalizationStrategy` go unused. Do not build logic that branches on culture without first checking whether the site is actually multilingual (`SELECT culture, COUNT(*) FROM sf_page_data GROUP BY culture`).

## Other tables you will meet

- **Scheduling**: `sf_scheduled_tasks` (`task_name` = the task CLR type, `execute_time`, `is_running`, `status`, `status_message`, `progress`, `task_data`, `schedule_data`, `is_recurring`, `ky`). Completed one-off rows are deleted by the scheduler - do not expect history here (use the Trace log).
- **Output cache**: `sf_oc_items` / `sf_oc_dependencies` (legacy) and `sf_ocd_itms` / `sf_ocd_typs` / `sf_ocd_dpndncies` (current) - cache-dependency bookkeeping that lets content changes invalidate page output.
- **Search / publishing**: `sf_publishing_point` (one per search index or outbound pipe; `nme`, `is_active`, `last_publication_date`, `storage_type_name`), `sf_publishing_pipe_settings` (flat-mapped per pipe type), `sf_publishing_mapping` / `sf_pipe_mapping_translation` / `sf_pblshng_mppng_*` (field mappings per pipe - tens of thousands of rows are normal), `sf_dynamic_type_base` + `dynmc_*`.
- **Configuration in the DB**: `sf_xml_config_items` (`path`, `dta`) only when config storage is switched to the database; `sf_config_variables`; `sf_app_setting` (site settings); `sf_wdgt_prst` (widget presets).
- **Dashboard / notifications / newsletters / ecommerce / forums / A-B testing / personalization (`sf_prs_*`) / responsive design (`sf_rdsgn_*`) / site sync / packaging (`sf_pckgng_*`) / mobile formats**: self-contained subsystems with their own FK-backed collections; rarely relevant to page or content work. `sf_dashboard_watchlist` (`user_id`, `dashboard_log_entry_id`) can be huge.
- **Recycle bin**: `sf_rbin_item` (index) - the actual deleted rows stay in their tables with `status = 4` / `is_deleted = 1`.
- **Folders**: `sf_folders` (`parent_id`, `root_id`, `path`, `is_deleted`, `cover_id`) - library folders; `sf_media_content.folder_id` points here.

## Data rot catalog (a long-lived Sitefinity DB lies to you)

Because the relationships that matter are not FK-enforced, debris accumulates over years of upgrades, deletions and editor sessions. Measured on the reference DB (your numbers will differ; rerun `reference/verification-queries.sql`):

1. **Stale duplicate Live page rows**: 35 `sf_page_node` ids matched more than one `sf_page_data` row with `status = 2` (upgrade-era leftovers; the oldest often have `culture` NULL). Only the row matched by `sf_page_node.content_id` is current. **Always resolve pages via `pn.content_id = pd.content_id`, never only via `pd.page_node_id = pn.id`.**
2. **Orphaned `sf_page_data` rows**: 12 with a `page_node_id` that matches no node.
3. **Years-old temp drafts** (649 temp vs 654 master page drafts, each with its own control copies) - by design, not a bug.
4. **Orphan form-draft controls**: 10,805 `FormDraftControl` rows whose `id3` is NULL (drafts deleted, controls left behind) - 92% of that family.
5. **Dangling `base_control_id`**: page-level override controls pointing at template controls that no longer exist.
6. **Orphan `sf_object_data` rows** (no `page_id`, `content_id`, `id3`, `parent_prop_id` or `parent_id`).
7. **Orphan `dynmc_*` tables** whose publishing point was deleted; apparent mystery tables that are really custom-field or taxonomy junction tables (`{table}_{field}`) - classify via `sf_meta_data_mapping` / `sf_meta_fields` before declaring debris.
8. **URL rows whose owner is gone** (page URLs with NULL `id2`, `/DynamicModule` rows with NULL `item_type` or an unresolvable `content_id`) - kept as redirects.
9. **`ownr` / `last_modified_by` pointing at deleted users** (on the reference DB ~20% of page owners no longer exist in `sf_users`).
10. **Workflow tables empty while `sf_approval_tracking_record` is huge** - lifecycle audit happens without approval workflows defined.
11. The largest tables are typically **permission junctions** (`*_sf_permissions`) and `sf_control_properties` - security metadata and widget properties dwarf content.

## Querying tips

- Counts without scanning: `sys.tables` joined to `sys.partitions` (`index_id IN (0,1)`).
- Column lists: `SELECT STRING_AGG(c.name, ', ') FROM sys.columns c WHERE c.object_id = OBJECT_ID('sf_page_data');`
- FK inventory: `SELECT OBJECT_NAME(parent_object_id), OBJECT_NAME(referenced_object_id), name FROM sys.foreign_keys ORDER BY 1;` - if a relationship is not in that list, it is convention only.
- Index inventory for a table: `sys.indexes` + `sys.index_columns` (the reference map lists the ones on the core tables).
- Prefer the indexed columns in WHERE clauses: `sf_object_data` (`page_id`, `parent_prop_id`, `sibling_id`, `base_control_id`, `content_id`, `id3`, `personalization_master_id`, `collection_index`), `sf_control_properties` (`control_id`, `prnt_prop_id`, `nme`), `sf_page_node` (`parent_id`, `root_id`, `content_id`, `(parent_id, ordinal)`), `sf_page_data` (`page_node_id`, `template_id`), `sf_draft_pages` (`page_id`, `content_id`, `nme`), `sf_dynamic_content` (`(application_name, status, visible)`, `original_content_id`, `url_name_`), `sf_url_data` (`url`, `content_id`, `id2`, `(item_type, url)`), `sf_content_link` (`parent_item_id`, `child_item_id`), `sf_permissions` (`(object_id, principal_id, app_name, set_name)`), `sf_version_chnges` (`item_id`).
- If querying through a guarded MCP tool, expect auto-applied row caps (so `OFFSET/FETCH` may fail - paginate with `WHERE key > last`) and keyword blocklists that match anywhere in the text, including aliases (`delete`, `truncate`, ...).

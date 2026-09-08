---
name: sitefinity-dynamic-content-sql
description: Use this skill when someone needs Module Builder (dynamic module) content out of the Sitefinity database as flat rows - for a report, Power BI, an audit, a migration, a data fix-up plan, or "just show me every X with its categories". Covers how a dynamic type is physically stored (the sf_dynamic_content + per-type split, lifecycle rows, mangled junction tables, sf_content_link, addresses, choices), how to resolve a type to its tables from the platform's own metadata, the publication-state rules, and a verified generator that emits or runs one SELECT per type with taxonomy / multi-choice / related / address fields as JSON columns - as an ad-hoc query, a stored procedure, or a view - plus the C# row class (Dapper / EF Core) that maps to it. Read-only.
---

# Sitefinity dynamic content as flat SQL (Module Builder types, verified 15.4)

Module Builder content is the hardest thing in a Sitefinity database to read by hand: an item's lifecycle columns and its field values live in different tables, every state is its own row, taxonomy and multi-select values sit in junction tables whose names the ORM mangles, related items are in a generic link table keyed on the master id, and half the "obvious" metadata columns lie. This skill gives you the storage model, the resolution rules, and a generator that turns any type into one flat result set you can point a report or a fix-up script at.

**Baseline: Sitefinity 15.4 (15.4.8636), SQL Server 2017+** (`STRING_AGG`, `FOR JSON`, `STRING_ESCAPE`). Every claim below was reproduced on a production-sized database (17 modules, 43,000 `sf_dynamic_content` rows) and the generator was run against eight type shapes there. The wider schema reference is `sitefinity-database-structure`; this skill only repeats what the flattening needs. **Read-only.** Never write to `sf_*` or per-type tables with SQL - see `sitefinity-best-practices`.

## 1. How one dynamic item is actually stored

| What | Where | Key facts |
|---|---|---|
| Lifecycle + system columns | `sf_dynamic_content` | `base_id` (the item id), `status`, `visible`, `original_content_id`, `url_name_`, `publication_date`, `expiration_date`, `date_created`, `last_modified`, `ownr`, `last_modified_by`, `vrsion`, `application_name` (the provider), `system_parent_id` / `system_parent_type` (hierarchy), `voa_class` (type discriminator, opaque). The `id` column is legacy and mostly NULL - never join on it. `was_published` is NULL on every row - ignore it. |
| Field values | one table per type, joined on `base_id` | Only the type's own fields plus `parent_id` for child types. No status column, no indexes beyond the PK, **no FK to `sf_dynamic_content`** (deliberately stripped by the schema upgrader). |
| Taxonomy fields (Classification) | junction `{table}_{field}` (`base_id`, `seq`, `val uniqueidentifier`) | `val -> sf_taxa.id`, `sf_taxa.taxonomy_id -> sf_taxonomies`. The field has **no column** on the type table (`sf_meta_fields.column_name` is NULL). |
| Multi-select Choices field | junction `{table}_{field}` (`base_id`, `seq`, `val varchar`) | `val` is the choice **value** (not the label). `sf_meta_fields.clr_type` = `...ChoiceOption[]`; `column_name` is set but **no such column exists**. |
| Single-select Choices field | `nvarchar(255)` column on the type table | Holds the choice value string. `clr_type` = `...ChoiceOption`. |
| Related data / related media | `sf_content_link` | `parent_item_id` = the item's **master** id in every lifecycle, `parent_item_type` = CLR type, `component_property_name` = field name, `child_item_id` / `child_item_type` / `child_item_provider_name`, `ordinal`, and three flags `available_for_master` / `available_for_live` / `available_for_temp` that say which lifecycle the link is visible in. `clr_type` = `Telerik.Sitefinity.RelatedData.RelatedItems`; `column_name` is set but no column exists. Child ids are master ids too. |
| Address field | `uniqueidentifier` column -> `sf_addresses.id` | `street`, `city`, `state_code`, `zip`, `country_code`, `latitude`, `longitude`, `map_zoom_level`. Resolves 100%. |
| Link field (15.x) | `nvarchar(max)` column holding a JSON array | `[{"id","href","text","target","sfref","queryParams","anchor","tooltip","type":"URL"...}]`. |
| Number / Currency | `decimal(20, n)` | Verified on a Number field with 0 decimal places (`decimal(20,0)`); `n` follows the field's decimal-places setting. Currency was not sampled - read the column type before assuming. |
| Short/Long text, HTML | `nvarchar(255)` / `nvarchar(max)` | Localizable ones are `Lstring` in `clr_type` (see localization). |
| Date, Yes/No, Guid | `datetime`, `bit`, `uniqueidentifier` | Sitefinity stores these dates in **UTC** (documented platform behaviour, and why `ToSitefinityUITime` exists - `sitefinity-api-helpers`; the sampled scheduled-publish times were midnight Eastern stored as 04:00). |
| Default URL | `sf_url_data` | `content_id` = master id, `app_name = '/DynamicModule'`, `is_default = 1`, `redirect = 0`. `item_type` can be NULL - do not filter on it. |
| Users | `sf_users` | `ownr` / `last_modified_by -> sf_users.id` (`user_name`, `email`, `first_name`, `last_name`). Resolves 100%. |

### Lifecycle is rows, not a flag

Each state is a separate `sf_dynamic_content` row **with its own `base_id`** and its own per-type row and its own junction rows:

| State | `status` | Identity | Notes |
|---|---|---|---|
| Master | 0 | `base_id` = the item id everything else references; `original_content_id = 00000000-...` | Always exists. Holds the editable values. |
| Temp | 1 | `original_content_id` = master id | Checkout copy while someone edits. Transient. |
| Live | 2 | **different `base_id`**, `original_content_id` = master id | What the public sees. At most one per master. **Unpublish keeps the live row and sets `visible = 0`**; expiry does the same (verified: 179 of 180 expired live rows have `visible = 0`). |
| Deleted | 4 | it is the **master row itself** with `status` changed (`original_content_id` empty), indexed by `sf_rbin_item.del_item_id` | The live row survives with `visible = 0`. So a "deleted" item still has two rows. |

Consequences: the flat view has to choose a lifecycle. **`master`** = what editors see (drafts included). **`live`** = what the site renders, where you must join the live row's own per-type and junction rows but still key `sf_content_link` and `sf_url_data` on the master id. Never filter the per-type table by "status" - it has none.

### Publication state, verified against the approval trail

```
Deleted      m.status = 4
Published    live row exists AND live.visible = 1
Scheduled    an enabled sf_scheduled_tasks row, task_name = 'WorkflowCallTask', execute_time in the future,
             task_data contains ContentItemMasterId="<master id>" and OperationName="Publish"
Expired      live row exists, live.expiration_date < now (UTC)      (the scheduler also flips visible to 0)
Unpublished  live row exists, visible = 0, not expired
Draft        no live row (never published, or created by code)
```

`sf_approval_tracking_record` (`workflow_item_id` = master id, `status` = Draft / Published / Unpublished / Scheduled) is the audit trail, not the source of truth: items created by code have no rows at all, and its latest row disagrees with the live row on a handful of items. Use it for "who did what when", derive state from the rows above. `sf_language_data.scheduled_date` is unused on monolingual sites.

### Hierarchy

Child types carry `parent_id` on the per-type table **and** the same value in `sf_dynamic_content.system_parent_id` (+ `system_parent_type`). Both point at the parent's **master** id, including on the child's live row - so a live-lifecycle view joins the parent's master per-type row for the parent title, which is what you want (the parent's title, not a stale live copy).

### Localization

`Lstring` fields (`is_localizable = 1`) store the default culture in the plain column. On multilingual sites Sitefinity adds per-culture columns to the same table (the ORM appends the culture to the column name) - **not verified here, the reference database is monolingual** - so confirm the column names with `INFORMATION_SCHEMA.COLUMNS` before relying on them. `sf_dynmc_cntnt_sf_lnguage_data -> sf_language_data` holds per-culture lifecycle (`language`, `content_state`, `publication_date`, `expiration_date`) - one row per item per culture. A monolingual site has `language = NULL` there and only the plain columns. Check `INFORMATION_SCHEMA.COLUMNS` for `{Field}_{culture}` columns before promising a report is multilingual.

## 2. Resolving a type to its tables (the part that goes wrong)

1. **Type registry**: `sf_mb_dynamic_module_type` (`type_namespace`, `type_name`, `main_short_text_field_name`, `parentTypeId`) joined to `sf_mb_dynamic_module` on `parent_module_id`. The module's `nme` is the display name **with spaces** (`Case Library`); the namespace segment has them removed (`Telerik.Sitefinity.DynamicTypes.Model.CaseLibrary`). Accept either from a human.
2. **Field inventory**: `sf_meta_fields` where `type_id` = the `sf_meta_types` row for that namespace + class and `is_deleted = 0`. Decide storage from `clr_type` + `taxonomy_id`, as in section 1. **Do not** use `sf_mb_dynamic_module_field.classification_id` to detect taxonomy fields - it carries stale non-empty guids on plain text fields. `field_type` there is the designer enum (`Telerik.Sitefinity.DynamicModules.Builder.Model.FieldType`: 1 ShortText, 2 LongText, 4 YesNo, 5 Currency, 6 DateTime, 7 Number, 8 Classification, 10 Guid, 11 GuidArray, 12 Choices, 13 Address, 14 RelatedMedia, 15 RelatedData, 16 Link) and is fine for labelling, but the ORM row is what the tables were built from.
3. **Per-type table**, in this order:
   - `sf_meta_data_mapping` (`module_name` = namespace segment, `type_name`, `field_name = '#NA'`) -> `table_name`. **The mapping table is incomplete** (2 of 17 types were missing on the reference DB) and **does not list junction tables** for Module Builder types at all.
   - else the convention `lower(module)_lower(type)` if such a table exists (held for 15 of 15 mapped types).
   - else **column signature**: the non-`sf_` table with a `base_id` column, no `seq` column, not already mapped, whose columns match every `column_name` of the type's column-backed fields. This is how you find types whose table was named by hand (`resrce`, `msg` on the reference DB).
4. **Junction tables**: `sys.foreign_keys` where `referenced_object_id` = the per-type table and the referencing table has `base_id`, `seq`, `val`. That FK is the one constraint the ORM always emits, so it is the reliable handle. Names are unreliable: over ~30 characters the generator strips vowels and truncates (`announcements_announcement_category` becomes `annncmnts_nnouncement_category`; `communityopportunities_opportunity_timecommitmentmultiselect` becomes `cmmntypprtnts_pprtnty_tm_cmmtm`). Map junction to field by **type first** (`val uniqueidentifier` = a taxonomy field, `val varchar` = a multi-select Choices field) then by comparing the de-voweled tail of the table name with the de-voweled field name - the mangler removes letters, it never reorders them. Confirm a taxonomy junction with `SELECT DISTINCT tx.nme FROM junction j JOIN sf_taxa ta ON ta.id = j.val JOIN sf_taxonomies tx ON tx.id = ta.taxonomy_id` when the table has rows.
5. **Related fields** need no table lookup - filter `sf_content_link` on `parent_item_type` + `component_property_name`.

## 3. The generator: `reference/flatten-dynamic-content.sql`

One T-SQL body that performs steps 1-5 and builds one `SELECT` returning **one row per item** with:

- `sf_*` lifecycle columns first (`sf_master_id`, `sf_row_id`, `sf_lifecycle`, `sf_publication_state`, `sf_is_published`, `sf_scheduled_publish_utc`, `sf_url_name`, `sf_default_url`, `sf_publication_date_utc`, `sf_expiration_date_utc`, `sf_date_created_utc`, `sf_last_modified_utc`, `sf_owner_id`, `sf_owner_user_name`, `sf_last_modified_by_id`, `sf_last_modified_by_user_name`, `sf_provider`, `sf_version`, `sf_parent_master_id`, `sf_parent_type`, and `sf_parent_<mainfield>` for child types) - prefixed so they can never collide with a field called `Title` or `Status`;
- every physical column of the per-type table in declared order (`base_id`, `parent_id`, `voa_*` excluded), address fields as both the raw id (`<Field>_address_id`) and a JSON object (`<Field>`);
- one JSON column per taxonomy field (`[{"Id","Title","UrlName","Taxonomy"}]`, ordered by `seq`), per multi-select Choices field (`["value1","value2"]`), and per related field (`[{"Id","Type","Provider","Ordinal"}]`, lifecycle-filtered via `available_for_live` / `available_for_master`, `is_child_deleted = 0`).

Parameters: `@Module`, `@Type`, `@Lifecycle` (`live` | `master`), `@Mode` (`select` runs it, `sql` returns the generated statement as a single-row result, `view` runs `CREATE OR ALTER VIEW @ViewName AS ...`, `poco` returns a C# class - section 3a), `@IncludeDeleted` (master rows with `status = 4`, master mode only), `@Top` (select mode), `@Namespace` (poco mode). It prints one diagnostic line first - which table it resolved and how, and every junction with the field it was mapped to - **read that line**; an unmapped junction appears as `junction:<table>` in the output instead of a field name.

**Running it without DDL rights** (a read-only reporting login is the normal case): put the parameter `DECLARE`s in front of the body and execute the batch - that is exactly how it was verified. With rights, wrap it as `CREATE PROCEDURE dbo.usp_SitefinityFlattenDynamicType (@Module nvarchar(255), @Type nvarchar(255), @Lifecycle varchar(6) = 'live', @Mode varchar(6) = 'select', @ViewName sysname = NULL, @IncludeDeleted bit = 0, @Top int = NULL) AS BEGIN ... END`. A view is what BI tools want; generate one per type with `@Mode = 'view'` (a view cannot take parameters and cannot `ORDER BY`, which is why select mode appends the ordering and view mode does not). The `view` mode was not exercised on the reference database (no DDL rights there) - test it once before trusting it.

Verified outcomes on the reference database: mapped types resolve via the mapping row, `Resources.Resource` and `LoginMOTD.Message` via column signature (`resrce`, `msg`); all 25 junction tables mapped to the right field including the fully mangled ones; a 1,146-row live type flattens in ~2 s; the state split for one type came out 1,184 Published / 368 Unpublished / 177 Expired / 49 Draft / 6 Deleted and reconciled with the raw row counts.

### 3a. The C# side: `@Mode = 'poco'` + `reference/FlatRowSupport.cs`

The same run that resolves the tables can emit the row class, so the class and the SELECT cannot drift:

```csharp
// @Mode = 'poco' for Community Opportunities / Opportunity (abridged; real output on the reference DB)
public sealed class OpportunityFlatRow : SitefinityFlatRowBase
{
    public string Title { get; set; }   // localizable (default culture)
    public Guid? Location_address_id { get; set; }
    public string Location { get; set; }                                   // JSON object from sf_addresses
    public AddressRef AddressLocation => FlatJson.Address(this.Location);
    public string Category { get; set; }                                   // JSON array of taxa
    public IReadOnlyList<TaxonRef> CategoryItems => FlatJson.Taxa(this.Category);
    public string TimeCommitmentMultiSelect { get; set; }                  // JSON array of choice values
    public IReadOnlyList<string> TimeCommitmentMultiSelectValues => FlatJson.Strings(this.TimeCommitmentMultiSelect);
    public string RelatedImages { get; set; }                              // JSON array of related refs
    public IReadOnlyList<RelatedRef> RelatedImagesItems => FlatJson.Related(this.RelatedImages);
}
```

Rules the emitter follows, so you can write one by hand the same way:

- **Property name = column name**, verbatim (`Title`, `WeekNumber`, `Location_address_id`). Dapper then maps field columns with no configuration; the `sf_*` lifecycle columns live on `SitefinityFlatRowBase` with `[Column("sf_...")]` attributes, so set `DefaultTypeMap.MatchNamesWithUnderscores = true` once for Dapper, and EF Core reads the attributes on a keyless entity (`HasNoKey().ToView(...)` or `FromSqlRaw`). Child types get an extra `[Column("sf_parent_<mainfield>")] SfParent<MainField>`.
- **SQL to C#**: `uniqueidentifier` -> `Guid?`, `bit` -> `bool?`, `int` -> `int?`, `decimal`/`numeric` -> `decimal?`, `float` -> `double?`, `real` -> `float?`, `datetime` -> `DateTime?` (commented `// UTC`), everything `*char` -> `string`. Everything nullable: Module Builder does not enforce required at the column level.
- **JSON columns stay `string`** (that is what the driver hands back) and get a computed sibling that parses them through `FlatJson` (`Taxa`, `Strings`, `Related`, `Address`) into `TaxonRef` / `RelatedRef` / `AddressRef`. NULL (no values) parses to an empty list, never throws. `FlatRowSupport.cs` parses with **`ServiceStack.Text`** (`JsonSerializer.DeserializeFromString<T>(json)`, the same static API Sitefinity code uses everywhere), the fastest serializer already in a Sitefinity bin folder; the JSON keys are emitted in PascalCase to match the properties by name, so there are no serializer attributes and **no `JsConfig` change** (the backend shares that global instance - never touch it). In a non-Sitefinity project swap the four calls for `System.Text.Json`.
- **Comments mark the field semantics** the column type cannot: `// localizable (default culture)` on `Lstring` columns, `// single-select choice value` on single Choices, and an unmapped junction surfaces as `junction_<table>` so it cannot be mistaken for a field.

The class is a **read model for the flat row**, not a Sitefinity entity. To hydrate a real `DynamicContent` object graph in-process use `sitefinity-poco-generator`; to write anything, use the managers (`sitefinity-best-practices`).

## 4. Ad-hoc patterns (when you do not want the whole row)

```sql
-- Live items of a type with their categories, no generator
SELECT l.base_id AS live_id, m.base_id AS master_id, t.[Title], m.url_name_,
       (SELECT ta.title_ FROM annncmnts_nnouncement_category j JOIN sf_taxa ta ON ta.id = j.val
        WHERE j.base_id = l.base_id ORDER BY j.seq FOR JSON PATH) AS categories
FROM sf_dynamic_content l
JOIN sf_dynamic_content m ON m.base_id = l.original_content_id AND m.status = 0
JOIN announcements_announcement t ON t.base_id = l.base_id
WHERE l.status = 2 AND l.visible = 1;

-- Which junction tables belong to a type, whatever they are called
SELECT OBJECT_NAME(fk.parent_object_id) AS junction
FROM sys.foreign_keys fk WHERE fk.referenced_object_id = OBJECT_ID('dbo.announcements_announcement');

-- Related documents of an item (master id!), as the live site sees them
SELECT cl.child_item_id, cl.child_item_type, cl.ordinal
FROM sf_content_link cl
WHERE cl.parent_item_id = @masterId AND cl.component_property_name = 'Attachments'
  AND cl.available_for_live = 1 AND cl.is_child_deleted = 0 ORDER BY cl.ordinal;

-- Items scheduled to publish, with when
SELECT TRY_CAST(SUBSTRING(task_data, CHARINDEX('ContentItemMasterId="', task_data) + 21, 36) AS uniqueidentifier) AS master_id, execute_time
FROM sf_scheduled_tasks WHERE task_name = 'WorkflowCallTask' AND enabled = 1 AND execute_time > GETUTCDATE()
  AND task_data LIKE '%OperationName="Publish"%';
```

Prefer the indexed columns: `sf_dynamic_content` is indexed on `(application_name, status, visible)`, `original_content_id`, `url_name_`, `(application_name, system_parent_id)`; `sf_content_link` on `parent_item_id` and `child_item_id`; per-type tables only on `base_id`.

## 5. Gotchas that cost people a day

- **A junction row exists per lifecycle row.** Reading categories through the master's `base_id` while joining the live per-type row (or vice versa) silently returns the wrong lifecycle's values.
- **`sf_content_link` is master-keyed but lifecycle-flagged.** A relation added after the last publish has `available_for_master = 1, available_for_live = 0`; the site does not show it yet. Filter on the flag for the lifecycle you are reporting.
- **`sf_mb_dynamic_module_field.classification_id` lies**; `sf_meta_fields.taxonomy_id` does not.
- **`sf_meta_fields.column_name` is populated for fields that have no column** (taxonomy is NULL, but related and multi-choice are not). Check `INFORMATION_SCHEMA.COLUMNS`, or trust `clr_type`.
- **`status = 4` is the master itself**, so `WHERE status = 0` hides deleted items entirely and a `CASE WHEN status = 4` inside it never fires.
- **Multi-select values are the `value` attribute of `<choice>`**, not the label; the labels are in `sf_mb_dynamic_module_field.choices` (XML) if the report needs them.
- **`ordinal` on `sf_content_link` is a `real` and can be negative**; sort by it, do not display it.
- **Dates are UTC** everywhere in these tables. `GETDATE()` in a comparison is a bug; use `GETUTCDATE()`.
- **Do not hardcode `voa_class`**; the per-type join already selects the type.

## 6. First run on a database you have not seen

1. `@Mode = 'sql'` for one type. The diagnostic line tells you how the table was resolved and which field each junction was mapped to. Every junction should name a field; `junction:<table>` means the name match failed - inspect its `val` values (taxa or strings) and alias it by hand in the generated SQL.
2. `@Mode = 'select', @Top = 5` in both lifecycles. `sf_publication_state` should reconcile with `SELECT d.status, d.visible, COUNT(*) FROM sf_dynamic_content d JOIN <table> t ON t.base_id = d.base_id GROUP BY d.status, d.visible`.
3. Multilingual site? Check `INFORMATION_SCHEMA.COLUMNS` for culture-suffixed columns before promising per-language output; the generator returns the default culture.
4. Then `@Mode = 'poco'` and drop `FlatRowSupport.cs` beside the class.

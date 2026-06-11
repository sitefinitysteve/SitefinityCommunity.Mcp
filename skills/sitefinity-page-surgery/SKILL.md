---
name: sitefinity-page-surgery
description: Use this skill when WRITING MIGRATION CODE that changes Sitefinity pages - switching templates, adding/removing/reordering widgets, swapping a widget's controller, or rewriting widget property values. The output of this skill is C# migration helpers (gated admin endpoints using PageManager) plus read-only verification SQL - never direct LLM writes to the database. Generic across Sitefinity sites; MVC/Feather-era page model; grounded in verified Sitefinity 15.4 platform behavior.
---

You are an expert at writing safe Sitefinity page-migration code. Every behavioral claim below was verified against actual Telerik 15.4 platform behavior and a production database. Companion skills: `sitefinity-database-structure` (the table map) and `sitefinity-page-inspector` (read-side queries).

**Scope: classic ASP.NET pages with WebForms and MVC/Feather widgets** (`MvcControllerProxy`). Pages built for the ASP.NET Core renderer / decoupled frontend ("Sitefinity Core") use a different widget model (`renderer` column populated) - these recipes do not apply to them.

**Version baseline: Sitefinity 15.4** - the API signatures and publish mechanics documented here are 15.4 behavior and may differ on older/newer versions (e.g. obsolete overloads removed, lifecycle internals reworked). Check the target project's version before writing migration code: `(Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion` (e.g. `15.4.8630.0` = Sitefinity 15.4).

## The deliverable is CODE, not data edits

Nobody wants an LLM mutating a live CMS database. The correct artifacts for a real migration are:

1. **A gated C# admin service**: ServiceStack/WebAPI endpoints locked to specific admin users, using `PageManager` inside `ElevatedModeRegion`, with per-step tracing, preflight checks, and post-publish verification. This is the ONLY artifact that can safely mutate page composition, because the object graph (recursive property trees, draft mirror-sync, version blobs, in-memory caches) is ORM-managed. If the host repo already has a migration service, extend it and follow its conventions instead of starting fresh.
2. **Read-only verification SQL** (run against a database copy) before and after each migration step.
3. **Human-applied repair SQL** only for data-rot cleanup that the API cannot express (e.g. deleting orphaned rows) - proposed as a reviewed script, never executed by the agent.

**Stored procedures are the wrong tool for page mutation.** The composition lives in a recursive object graph (`sf_object_data` <-> `sf_control_properties`) with app-serialized version snapshots and an OpenAccess L2 cache: a stored procedure cannot maintain draft mirror-sync (`OriginalControlId` back-stamping), cannot write version history (snapshots are .NET-serialized), and its effects are invisible to the running app until an app-pool recycle. Keep SPs for read-only reporting at most.

## The verified publish pipeline (platform behavior, 15.4)

```csharp
var pageManager = PageManager.GetManager();
using (new ElevatedModeRegion(pageManager))
{
    var node = pageManager.GetPageNode(pageNodeId);
    var pageData = node.GetPageData();                 // null for group nodes - guard
    var draft = pageManager.EditPage(pageData.Id, true); // returns a TEMP draft (checkout)
    // ... mutate draft.Controls / draft.TemplateId ...
    pageManager.SaveChanges();
    pageManager.PublishPageDraft(draft.Id, true);       // bool is OBSOLETE/ignored - see below
    pageManager.SaveChanges();

    // REQUIRED if you want a History entry: publish does NOT create one (see Versioning)
    var versionManager = VersionManager.GetManager();
    versionManager.CreateVersion(draft, pageData.Id, isPublished: true);
    versionManager.SaveChanges();
}
```

What each step really does (verified behavior of `PageManager` + `LifecycleDecorator`/`LifecycleDecoratorPages`):

- **`EditPage(pageDataId, lockIt)`** = `GetMaster` -> `Edit` (if no master draft exists, creates one and copies LIVE -> master) -> `CheckOut` (returns the TEMP draft). CheckOut behavior:
  - Reuses the existing temp draft, reassigning `Owner` to the current user; other users' temp drafts are deleted.
  - Re-copies master -> temp only when the master is NOT `Synced` (flag bit 1 in `sf_draft_pages.flags` / `sf_page_data.flags`, set by CheckIn).
  - If the page is `LockedBy` another user: throws `InvalidOperationException("page is locked by ...")` unless the current user has Unlock permission (then the foreign temp is discarded and recreated). **Catch this.**
  - `lockIt: true` sets `PageData.LockedBy = currentUserId`.
- **`PublishPageDraft(draft.Id, makeVisible)`**: the `makeVisible` overloads are marked `[Obsolete("The makeVisible attribute is not used anymore...")]` and the value is IGNORED. The call is exactly: temp draft -> `CheckIn(draft, culture, deleteTemp: false)` -> `Publish(masterDraft)`.
  - **CheckIn** copies temp -> master (Controls, TemplateId, LastControlId, Version, presentation), sets `PageData.Status = Master` (yes - the live row's status flips to 0 mid-flow and Publish flips it back to 2; a crash between the two strands the page unpublished), marks the master draft `Synced`, and with `deleteTemp: false` leaves the temp row in place. `PageManager.DeleteTempAfterPublish => false` in source - **temp drafts persist by design** in the pages module.
  - **Publish** guards concurrency: `if (pageData.Version > masterDraft.Version) throw ...PageModifiedBySomeoneElse`. Then: `PageData.Version++`, `Status = Live`, `Visible = true` (ALWAYS - publishing a never-published page makes it live, no opt-out), `LockedBy = Guid.Empty`, `ContentState = Published`, `PublicationDate` set on FIRST publish only, then copies master draft content onto the live row.
  - **Control copy is a mirror-sync, not a wipe**: live controls matched by a draft control's `OriginalControlId` are updated IN PLACE (live control ids are stable across publishes); unmatched draft controls produce NEW live rows (and the draft control is back-stamped `OriginalControlId = newLiveId`); live rows referenced by no draft control are DELETED.

### Versioning (the bug class to avoid)

`PageManager.PublishPageDraft` / `PagesLifecycle.Publish` write **NO** `sf_version_chnges` row. The backend UI creates versions in the calling layer (`ZoneEditorService.PublishPageDraft` calls `VersionManager.CreateVersion(draft, pageDataId, isPublished: true)` after the publish). Consequences for migration code:

- A service that publishes and then "annotates the latest version" without creating one first annotates whatever Change row already existed - possibly one the UI created years ago. **Create the version explicitly** (snippet above), then set `.Comment` on the returned `Change` and `SaveChanges()` on the VersionManager.
- Version numbers are stored as `major * 10000 + minor` (`VersionDataProvider.BuildFullVersionNumber`); published versions increment the major (10000, 20000, ...), draft saves increment the minor. `Change.Version` returns the RAW multiplied value; the UI renders `major + "." + minor`.

## C# helpers for widget manipulation (platform-verified algorithms)

### Reorder / move between placeholders (sibling splice)

`SiblingId` = the id of the control immediately BEFORE this one in its placeholder (`Guid.Empty` = first; template inheritance can legally produce multiple Empty-heads in one placeholder). The exact splice Sitefinity's own editor uses (`ZoneEditorService.ChangeIndex`):

```csharp
// move 'ctrl' so it sits after 'newSiblingId' (Guid.Empty = make it first) in 'newPlaceHolder'
var oldSuccessor = draft.Controls.SingleOrDefault(c => c.SiblingId == ctrl.Id && c.PlaceHolder == ctrl.PlaceHolder);
if (oldSuccessor != null)
{
    // unlink: my old successor now points at my old predecessor
    oldSuccessor.SiblingId = ctrl.SiblingId;
}
var newSuccessor = draft.Controls.SingleOrDefault(c => c.SiblingId == newSiblingId && c.PlaceHolder == newPlaceHolder);
if (newSuccessor != null)
{
    // relink: whoever followed the target position now follows me
    newSuccessor.SiblingId = ctrl.Id;
}
ctrl.PlaceHolder = newPlaceHolder;
ctrl.SiblingId = newSiblingId;
```

Delete = splice out: every control whose `SiblingId == deleted.Id` gets `SiblingId = deleted.SiblingId`.

Reading order back out (e.g. for preflight reports): group controls by `PlaceHolder`, build a `SiblingId -> control` map, walk the chain from `Guid.Empty`, cycle-guard with a visited set, and append unreachable orphans at the end defensively - real data contains broken chains.

### Add a widget (the exact recipe Sitefinity's own editor uses)

This mirrors `ZoneEditorService.CreateNewControl`, the code path the page editor itself runs - replicate ALL of these steps, several are easy to miss:

```csharp
var isBackend = draft.ParentPage.NavigationNode.IsBackend;
var ctrl = pageManager.CreateControl<PageDraftControl>(isBackend);   // bool overload - NOT (type, placeholder)
ctrl.ObjectType = "Telerik.Sitefinity.Mvc.Proxy.MvcControllerProxy";
ctrl.PlaceHolder = placeHolder;             // e.g. a layout column: "C308_Col01"
ctrl.SiblingId = siblingId;                 // Guid.Empty = first in the placeholder
ctrl.IsLayoutControl = false;
ctrl.SupportedPermissionSets = ControlData.ControlPermissionSets;   // LayoutPermissionSets for layouts
ctrl.Editable = false;
ctrl.IsOverridedControl = false;
ctrl.BaseControlId = Guid.Empty;
ctrl.SetDefaultPermissions(pageManager);    // without this the control has no permission rows
ctrl.Caption = "My Widget";
pageManager.SetControlId(draft, ctrl);      // generates the "Cnnn" ID property

var nameProp = pageManager.CreateProperty();
nameProp.Name = "ControllerName";
nameProp.Value = typeof(MyWidgetController).FullName;   // the IsKey property; ZoneEditorService sets it from the toolbox registration
ctrl.Properties.Add(nameProp);

// sibling wiring (ZoneEditorService does exactly this):
// if siblingId != Guid.Empty: the control that previously had SiblingId == siblingId now points at ctrl.Id
// if siblingId == Guid.Empty: the current head of this placeholder (SiblingId == Empty) now points at ctrl.Id
draft.Controls.Add(ctrl);
((DraftData)draft).Version++;               // the editor bumps the draft version on every structural change
```

For a LAYOUT control: `IsLayoutControl = true`, `SupportedPermissionSets = ControlData.LayoutPermissionSets`, and add a `Layout` property row whose `Value` is the grid template path - that is all a Feather grid is.

`SetControlId` (`ControlManager.SetControlId` internally) computes `Math.Max(LastControlId + 1, Controls.Count() + 1)`, writes `"C" + num.ToString("#000")` (templates use the template `Key` as prefix instead of `"C"`), and persists `LastControlId` back on the draft. Don't hand-roll IDs.

### Replace / retarget a LAYOUT control

The low-risk pattern is **in-place mutation, keeping the row and its `ID` property**: child placeholders are named `{ID}_Col{NN}`, so as long as the ID (and column count) survives, every child widget stays exactly where it is - no re-homing needed. To switch a Feather grid to a different grid template: set `Caption` and rewrite the `Layout` property value to the new grid template path. (This pattern is production-proven for grid-to-grid migrations.)

Replacing a legacy **WebForms layout** (`Telerik.Sitefinity.Web.UI.LayoutControl`) with a Feather `GridControl` adds three checks to do FIRST on the actual data - do not assume:
1. **Sample the old control's children**: confirm their `place_holder` values follow `{ID}_Col{NN}` keyed to the old control's ID property. Era-dependent placeholder schemes exist; if the children use a different scheme, they must be re-homed explicitly.
2. **Sample the old control's properties**: confirm what it persisted (a `Layout` path? embedded template names?) before assuming the new `Layout` value slot-in.
3. **Confirm the rendering context**: a Feather GridControl renders through the ResourcePackages/Feather pipeline - the page (or its template) must be MVC or Hybrid (`sf_page_templates.framework` 1 or 0). Swapping the control on a pure-WebForms page does not make it render.

If any check fails, fall back to: create the new GridControl beside the old one (recipe below, with `IsLayoutControl = true` and the SAME ID property value is NOT possible via SetControlId - assign placeholders explicitly instead), move the children's `place_holder` strings to the new control's columns, splice the old control out of the sibling chain, and `pageManager.Delete(oldControl)`.

### Swap a widget's controller and reconcile properties

```csharp
var nameProp = ctrl.Properties.FirstOrDefault(p => p.Name == "ControllerName");
nameProp.Value = typeof(NewController).FullName;

var settings = ctrl.Properties.FirstOrDefault(p => p.Name == "Settings");
if (settings != null)
{
    // child property names must match the NEW controller's C# property names exactly;
    // names the new controller doesn't have are dead rows (harmless but rename/remove them);
    // anything you remove falls back to the new controller's defaults
    var title = settings.ChildProperties.FirstOrDefault(p => p.Name == "Title");
    if (title != null) { title.Value = "New Title"; }
}
```

How persistence really works (the internal `PropertyPersister` pipeline) - this is why the rows are sparse:
- On save, Sitefinity reflects over the control via TypeDescriptor and persists a property ONLY if `!object.Equals(liveValue, defaultInstanceValue)` where the default instance is a fresh `new T()` (NOT the `[DefaultValue]` attribute - that's only a fallback when a getter throws).
- `ControllerName` is `[PropertyPersistence(IsKey = true)]` -> always persisted regardless of the diff. `ID` is written by `SetControlId`.
- `Settings` for `MvcControllerProxy` is a `ControllerSettings : DynamicObject` wrapping the controller instance; its children are the controller's public properties (minus 21 MVC infrastructure names). The container row is deleted if zero children survive the diff - that's why unconfigured widgets have no Settings row at all.
- Scalars serialize via `TypeConverter.ConvertToInvariantString` (invariant culture strings: "True", "42").
- `[PropertyPersistence(PersistAsJson = true)]` lists serialize as ONE row of JSON (`JsonConvert.SerializeObject`); without it, each list item becomes an `sf_object_data` row (`parent_prop_id`, `collection_index`) with its own property rows - **and a list item whose properties all equal that item type's defaults produces ZERO rows and silently vanishes on save** (verified in `ListPropertyPersister`). Mark complex `IList<>` widget properties `PersistAsJson = true`.

## Migration-service checklist

1. **Gate hard** (specific admin users only), trace every step with a per-run log, return structured success/failure.
2. **Preflight before touching the draft**: template-category checks, unmappable-layout scans, widget-specific validations - return a blocking message instead of half-migrating.
3. **Resolve templates by ID, never name** - `nme` is not unique (stock installs ship several `Bootstrap4.default` rows). Discover the site's templates first: `SELECT id, nme, title_, framework FROM sf_page_templates ORDER BY nme;` and put the chosen IDs in config/constants with comments.
4. **Wrap EditPage in a lock-aware try/catch** - locked-by-another-user throws.
5. **After publish: create the version explicitly** (`CreateVersion(draft, pageDataId, true)`), then comment it.
6. **Verify with the authoritative join** (`pn.content_id = pd.content_id`), not `page_node_id + status` - stale duplicate live rows exist in old databases.
7. **Concurrent editor sessions are the revert vector.** Because `EditPage` reuses and updates THE temp draft, a migration done through it leaves temp == master == live (no stale rows for itself). The danger is a human who already had the page open in the zone editor BEFORE the migration: their browser holds the old layout client-side, and saving afterward writes it back over the migrated state (the Version guard does not catch this - checkout fast-forwards the temp's version). Migrate when nobody is editing, or call `pageManager.DeletePageTempDrafts(pageData)` after migrating to force editors to reload fresh (deliberately - it discards unsaved work).
8. **Expect the status round-trip**: between CheckIn and Publish the live row briefly has `status = 0`. If a migration run dies mid-publish, the page is unpublished - re-running the publish fixes it; build the service idempotent.
9. **Invalidate your own caches and re-verify with a hard browser refresh** - DB truth and rendered truth diverge until caches clear.

## Raw SQL from migration C# code: resolve the connection string via the API

If a helper needs ADO.NET (e.g. a verification query the ORM can't express), get the connection string from the Sitefinity configuration API at runtime:

```csharp
using Telerik.Sitefinity.Configuration;
using Telerik.Sitefinity.Data.Configuration;

var connString = Config.Get<DataConfig>()
    .ConnectionStrings["Sitefinity"].ConnectionString;   // "Sitefinity" is the standard name

using (var conn = new SqlConnection(connString))
using (var cmd = new SqlCommand("SELECT ... WHERE pd.page_node_id = @PageNodeId", conn))
{
    cmd.Parameters.AddWithValue("@PageNodeId", pageNodeId);
    // ...
}
```

**NEVER read `App_Data\Sitefinity\Configuration\DataConfig.config` off disk and paste the literal connection string into code.** The file is where Sitefinity persists the value, but each environment (dev / staging / prod) has its own, credentials rotate, and a hardcoded string silently points migration code at the wrong database. `Config.Get<DataConfig>()` resolves whatever the running site is actually using. Always use parameterized commands - never string-interpolate values into SQL.

## Post-change verification SQL (read-only)

```sql
-- Live template via the authoritative join
SELECT pt.nme, pd.status, pd.vrsion
FROM sf_page_node pn
JOIN sf_page_data pd ON pd.content_id = pn.content_id
LEFT JOIN sf_page_templates pt ON pt.id = pd.template_id
WHERE pn.id = '<pageNodeId>';

-- No draft (especially no TEMP draft) disagrees about the template
SELECT dp.id, dp.is_temp_draft, dp.template_id, dp.last_modified
FROM sf_draft_pages dp WHERE dp.page_id = '<pageDataId>';

-- A version snapshot exists for this publish (vrsion is major*10000+minor; latest = MAX)
SELECT TOP 3 vrsion, change_type, is_published_version, LEFT(commnt, 80) AS commnt, last_modified
FROM sf_version_chnges WHERE item_id = '<pageDataId>' ORDER BY vrsion DESC;
```

## When SQL writes ARE the right call (human-applied only)

Data-rot cleanup the API cannot express: orphaned `sf_object_data` rows (no page, no parent property), dangling `base_control_id` references, ancient duplicate status=2 `sf_page_data` rows nothing points to, abandoned temp drafts of deleted pages. For these, produce a reviewed script with: the SELECT that proves each row is orphaned, the delete statements ordered child-first (`sf_control_properties` -> `sf_object_data` -> `sf_draft_pages`), a rowcount expectation, and an app-pool recycle note. The human runs it.

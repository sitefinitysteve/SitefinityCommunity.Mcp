---
name: sitefinity-api-helpers
description: Verified cheat sheet of the Sitefinity (classic MVC, .NET Framework 4.8) platform helpers that production code actually reaches for - ToSitefinityUITime and the other DateTime/string extensions, GetValue/GetString/SetValue on any data item, GetRelatedItems and content links, lifecycle helpers (GetLive/IsPublished), media URL resolution, page URLs, identity and permission checks (ClaimsManager, RoleManager, IsGranted), elevated privilege, design/preview/index mode detection, TypeResolutionService, Config.Get. Every signature was extracted from the decompiled 15.4 assemblies and cross-checked against a production site's usage. Use when writing or reviewing Sitefinity C# and you need the right built-in helper instead of hand-rolling one.
---

# Sitefinity API helpers: the built-ins worth knowing (15.4, verified)

Sitefinity's assemblies carry hundreds of extension methods and static helpers, and most custom code re-implements a dozen of them badly. This sheet lists the ones that matter, with the **exact 15.4 signatures** (`Telerik.Sitefinity.dll` / `Telerik.Sitefinity.Model.dll` build 15.4.8636, decompiled) and, where noted, how often a production site with ~150 widgets calls them - a proxy for "this is the idiom".

**Scope: classic MVC (.NET Framework 4.8) in-process code** - widgets, ServiceStack services, scheduled tasks, migration code. Not the ASP.NET Core Renderer's SDK.

## Time zones: `ToSitefinityUITime` (the one everyone asks about)

Sitefinity stores `DateTime` values in **UTC**. When showing a date to a person, convert it with the platform helper instead of `ToLocalTime()` (which uses the *server's* zone) or a hand-rolled offset:

```csharp
using Telerik.Sitefinity;   // SystemExtensions

var shown = item.PublicationDate.ToSitefinityUITime();         // UTC -> the site's configured UI time zone
```

| Extension (`Telerik.Sitefinity.SystemExtensions`) | What it does |
|---|---|
| `DateTime ToSitefinityUITime(this DateTime value)` | `TimeZoneInfo.ConvertTime(value, UserManager.GetManager().GetUserTimeZone())` - the zone from **Administration > Settings > Advanced > System > UI Time Zone Settings**, falling back to `TimeZoneInfo.Local`. Called 76 times on the reference site; this is the idiom |
| `DateTime ToSitefinityOrLocaUITime(this DateTime value)` [sic] | If the system setting *UserBrowserSettingsForCalculatingDates* is on, applies the browser's offset from the `sf_timezoneoffset` cookie instead; otherwise same as above. Backend UI uses it; frontend code rarely wants it |
| `DateTime ToLocal(this DateTime value)`, `DateTime TrimSeconds(this DateTime)`, `bool IsDateInRange(this DateTime, DateTime from, DateTime to)`, `string ToLongDateTimeString(this DateTime)`, `string ToReadableString(this TimeSpan)` | Small conveniences in the same class |

Going the other way (user input -> storage): convert the UI-zone value to UTC with `TimeZoneInfo.ConvertTimeToUtc(value, UserManager.GetManager().GetUserTimeZone())`. There is no `FromSitefinityUITime` helper. Never persist a UI-zone value; every lifecycle date (`PublicationDate`, `LastModified`, `DateCreated`) is compared in UTC by the platform.

`UserManager.GetManager().GetUserTimeZone()` returns the `TimeZoneInfo` itself when you need it for formatting or for `TimeZoneInfo.ConvertTime` on a batch.

## Reading and writing fields on any data item

`Telerik.Sitefinity.Model.DataExtensions` - extension methods on `IDynamicFieldsContainer`, which every content type, `DynamicContent`, `PageNode`, and user profile implements:

| Signature | Notes |
|---|---|
| `TValue GetValue<TValue>(this IDynamicFieldsContainer dataItem, string fieldName)` | Typed read of a built-in **or custom** field. Text fields are `Lstring` - read them as `GetValue<Lstring>` (or `GetString`) and let the implicit conversion give you a `string`. Taxonomy fields are `TrackedList<Guid>` (`Telerik.OpenAccess`) |
| `object GetValue(this IDynamicFieldsContainer dataItem, string fieldName)` | Untyped variant |
| `Lstring GetString(this IDynamicFieldsContainer dataItem, string fieldName)` + overloads `(…, CultureInfo culture)`, `(…, CultureInfo culture, bool fallback)`, `(…, bool fallback)`, `(…, string cultureName)` | Culture-aware text read; `fallback` returns the invariant/default-culture value when the culture has none |
| `void SetValue(this IDynamicFieldsContainer dataItem, string fieldName, object value)` | The write side - 183 calls on the reference site. Works for custom fields the C# type does not declare |
| `void SetString(this IDynamicFieldsContainer dataItem, string fieldName, Lstring value)` + `(…, CultureInfo)` / `(…, string cultureName)` | Culture-aware text write |
| `bool DoesFieldExist(this IDynamicFieldsContainer dataItem, string fieldName)` | Guard before `GetValue` on types whose field set varies per site |
| `ContentLink GetContentLink(...)` / `IList<ContentLink> GetContentLinks(...)` / `SetContentLink(...)` | Raw content-link access; prefer the related-data helpers below |

## Related data and content links

`Telerik.Sitefinity.RelatedData.RelatedDataExtensions` (namespace `Telerik.Sitefinity.RelatedData`):

| Signature | Notes |
|---|---|
| `IQueryable<T> GetRelatedItems<T>(this object item, string fieldName) where T : IDataItem` | The idiom for RelatedData / RelatedMedia fields (36 calls). Resolves in the **same lifecycle status as the source item** - call it on a Live item to get live relations |
| `IQueryable<IDataItem> GetRelatedItems(this object item, string fieldName)` | Untyped variant |
| `IQueryable<T> GetRelatedParentItems<T>(this object item, string parentItemProviderName = null, string fieldName = null)` | Reverse direction: who relates to me |
| `int GetRelatedItemsCountByField(this object item, string fieldName = null)` / `GetRelatedItemsCountByType(this object item, string typeName = null)` | Counts without materializing |
| `ContentLink CreateRelation(this IDataItem item, IDataItem relatedItem, string fieldName)` / `(…, Guid relatedItemId, string relatedItemProviderName, string relatedItemType, string fieldName)` | Write a relation (master item, then publish) |
| `void DeleteRelation(this IDataItem item, IDataItem relatedItem, string fieldName)` / `DeleteRelations(this IDataItem item[, string fieldName])` | Remove relations |
| `string GetDefaultUrl(this object item)` | The item's canonical URL through the URL system |
| `IEnumerable GetItemsWithSameTaxons(this object item, string itemTaxonomyFieldName, string relatedItemsTypeFullName, int skip = 0, int take = 10)` | "Related by tag" queries |

`Telerik.Sitefinity.Data.ContentLinks.ContentLinksExtensions`: `object GetLinkedItem(this ContentLink contentLink, bool throwExceptionIfNotFound = false, bool supressSecurityChecks = false)` resolves a raw link row to its item.

## Module Builder (`DynamicContent`) helpers

`Telerik.Sitefinity.Model.DynamicContentExtensions` (`Telerik.Sitefinity.Model.dll`):

| Signature | Notes |
|---|---|
| `IQueryable<DynamicContent> GetChildItems(this DynamicContent dynamicContent, Type childItemsType)` / `(…, string childItemsType)` | Children in a Module Builder hierarchy (21 calls); paged overloads take `filterExpression, orderExpression, int? skip, int? take, ref int? totalCount` |
| `IQueryable<DynamicContent> GetSuccessors(this DynamicContent, Type childItemsType)` | All descendants, not only direct children |
| `int GetChildItemsCount(this DynamicContent parentItem, Type childItemsType)` | |
| `void SetParent(this DynamicContent, DynamicContent parentItem)` / `(…, Guid parentId, string systemParentType)` and `AddChildItem(this DynamicContent parent, DynamicContent child)` | Hierarchy writes |
| `void AddImage(this DynamicContent, string propertyName, Guid imageId, string librariesProviderName = "")` (+ `Image img` overload), `AddFile(...)`, `AddVideo(...)`, `ClearImages/ClearFiles/ClearVideos(this DynamicContent, string propertyName)` | Related-media writes without touching `ContentLink` |
| `IPermissionsFacade ManagePermissions(this DynamicContent)` | Fluent permission edits |

`DynamicModuleManager` (`Telerik.Sitefinity.DynamicModules`): `GetDataItems(Type itemType)` (103 calls - always filter `Status == Live && Visible` for frontend reads), `GetDataItem(Type itemType, Guid id)`, `GetChildItems(DynamicContent item, Type childType)`, `CreateDataItem(Type itemType)`, and the lifecycle facade `manager.Lifecycle` (below). Resolve `Type` with `TypeResolutionService.ResolveType("Telerik.Sitefinity.DynamicTypes.Model.<Module>.<Type>")` (`Telerik.Sitefinity.Utilities.TypeConverters`, `Telerik.Sitefinity.Utilities.dll`).

## Lifecycle helpers

Every content manager exposes `manager.Lifecycle` (`LifecycleDecorator`, `Telerik.Sitefinity.Lifecycle`):

```csharp
var master = manager.Lifecycle.GetMaster(item);      // editable copy
var temp   = manager.Lifecycle.CheckOut(master);     // per-user checkout
// ... edit temp ...
master = manager.Lifecycle.CheckIn(temp);            // temp -> master
manager.Lifecycle.Publish(master);                   // master -> live (new/updated live row)
manager.SaveChanges();
```

| `LifecycleDecorator` member | Notes |
|---|---|
| `GetLive(item, CultureInfo culture = null)`, `GetMaster(item)`, `GetTemp(item, culture)` | Navigate between the state copies (they are separate rows - see `sitefinity-database-structure`) |
| `Edit(liveItem, culture)`, `CheckOut(master, culture)`, `CheckIn(temp, culture, bool deleteTemp = true)`, `Publish(master, culture)`, `PublishWithSpecificDate(master, DateTime publicationDate, culture)`, `Unpublish(liveItem, culture)` | The state machine |

`Telerik.Sitefinity.Lifecycle.LifecycleExtensions` adds the read-side checks: `bool IsPublished(this ILifecycleDataItem item, CultureInfo culture = null)`, `bool IsPublishedInCulture(this Content item, CultureInfo culture = null)`, `bool HasDraftNewerThanPublished(this ILifecycleDataItemLive liveItem, CultureInfo culture)`, `ILifecycleDataItemDraft GetMasterItem(this ILifecycleDataItemLive liveItem)`, `GetTempItem(...)`, `ContentUIStatus GetUIStatus(this object item)` (the status label the backend shows), `int GetLanguageVersion(this object item, CultureInfo culture = null)`.

## Media and URLs

`Telerik.Sitefinity.Modules.Libraries.MediaContentExtensions`:

| Signature | Notes |
|---|---|
| `string ResolveMediaUrl(this MediaContent mediaContent, bool resolveAsAbsoluteUrl = false, CultureInfo culture = null)` | The URL of the file, honoring the blob storage provider and multisite. `MediaContent.MediaUrl` (the property, 106 calls) is the cached default |
| `string ResolveThumbnailUrl(this MediaContent mediaContent, string name, bool resolveAsAbsoluteUrl = false, CultureInfo culture = null)` / `(…, bool resolveAsAbsoluteUrl = false, int size = 0)` | Thumbnail by **profile name** (Administration > Settings > Advanced > Libraries > Images > Thumbnails) - never build thumbnail URLs by hand |
| `string ResolveCustomImageSizeThumbnailUrl(this Image image, string imageCustomSize, string libraryProvider)` | Ad-hoc size |
| `IQueryable<Image> Images(this Album album)`, `Documents(this DocumentLibrary)`, `Videos(this VideoLibrary)`, `Items(this Library)` | Library contents as queries |
| `bool IsVectorGraphics(this MediaContent)` | SVG detection before thumbnailing |

`LibrariesManager` (`Telerik.Sitefinity.Modules.Libraries`): `GetImage(Guid)`, `GetImages()`, `GetDocument(Guid)`, `GetDocuments()` (23 calls), `GetVideo(Guid)`, `GetVideos()`, `GetAlbum(Guid)`, `GetAlbums()`, `GetDocumentLibrary(Guid)`, `GetDocumentLibraries()`, `Upload(MediaContent content, Stream source, string extension[, bool uploadAndReplace[, bool replaceForAllTranslations]])`.

Pages (`Telerik.Sitefinity.Modules.Pages`, file `PageExtesnsions.cs` [sic]): `string GetUrl(this PageNode page)`, `GetUrl(this PageNode, CultureInfo culture)`, `GetUrl(this PageNode, string rootName)`, `GetUrl(this PageNode, string rootName, CultureInfo culture, bool fallbackToAnyLanguage)`, and `string GetFullUrl(this PageNode pageNode)` / `GetFullUrl(this PageNode, CultureInfo culture, bool fallbackToAnyLanguage)` / `GetFullUrl(this PageNode, CultureInfo culture, bool fallbackToAnyLanguage, bool localizeUrl)` (24 calls on the reference site; returns a `~/`-rooted app path - pass it through `UrlPath.ResolveUrl` or trim the `~`). `UrlPath` (`Telerik.Sitefinity.Web`): `string ResolveUrl(string url)`, `ResolveUrl(string url, bool absolute, bool removeTrailingSlash = false)`, `ResolveAbsoluteUrl(string relativePath, bool removeTrailingSlash = false)` (+ protocol/port/host overloads). `PageManager`: `GetPageNode(Guid)`, `GetPageNodes()`, `GetPageData(Guid)`, `GetHomePageNodeId()`, `GetTemplate(Guid)`, `GetTemplates()`, `EditPage(...)`, `PublishPageDraft(...)` (see `sitefinity-page-surgery`).

Content locations (`IContentLocation.GetUrl(object item = null)`) and `GetDefaultUrl(this object item)` cover "where does this item render".

## Dynamic links inside HTML (`sfref`)

Rich-text fields and content blocks store media/page/content references as `sfref="[images|OpenAccessDataProvider|tmb:thumbnail]<guid>"` keys, never URLs, and resolve them at render. `Telerik.Sitefinity.Web.Utilities.LinkParser` (static): `string ResolveLinks(string html)`, `ResolveLinks(string html, bool resolveAsAbsoluteUrl)`, `ResolveLinks(string html, GetItemUrl itemUrl, ResolveUrl resolveUrl, bool preserveOriginalValue[, bool resolveAsAbsoluteUrl[, ProcessChunk processChunk]])`, `string UnresolveLinks(string html)` (only for HTML resolved with `preserveOriginalValue: true`), `bool ContainsDynamicLinks(string html)`, `bool TryExtractValues(Uri link, out IDictionary<string, object> result)`, `bool TryGetImageId(IDictionary<string, object> values, out Guid imageId, out string imageProvider)`. The stock resolver delegate is `Telerik.Sitefinity.Modules.GenericContent.DynamicLinksParser.GetContentUrl(string key, Guid id, bool resolveAsAbsoluteUrl, ContentLifecycleStatus status)`; `new DynamicLinksParser(preserveOriginalValue, bool? resolveAsAbsolute).Apply(html)` is the filter form, and `Telerik.Sitefinity.Modules.HtmlFilterProvider.ApplyFilters(html)` is what Feather views run (returns `""` with no HTTP context). The idiom: `LinkParser.ResolveLinks(html, DynamicLinksParser.GetContentUrl, null, false)`. Mark rich-text widget properties `[DynamicLinksContainer]` so the designer persists keys, not URLs. Full mechanics in `sitefinity-widget-expert`.

## Output-cache dependencies for custom widgets

`Telerik.Sitefinity.Frontend.Mvc.Infrastructure.Controllers.ControllerExtensions`: `void AddCacheDependencies(this Controller controller, IEnumerable<CacheDependencyKey> keys)` registers the items a widget rendered as dependencies of the page's output cache, so editing or publishing them invalidates the cached page (the widget id is taken from `ViewData["controlDataId"]`; it throws if there is no current HTTP context). Its sibling `AddCacheVariations(this Controller controller, Type contentType, string providerName = null)` makes the cached page vary by that content type's URL parameters (detail pages). Build keys with `Telerik.Sitefinity.Model.OutputCacheDependencyHelper.GetPublishedContentCacheDependencyKeys(Type contentType, Guid itemId)` (one item) or `(Type contentType, string appName)` (any item of that type on that provider). `ContentModelBase.GetKeysOfDependentObjects(viewModel)`, `ItemViewModel.RelatedItems` and `DynamicLinksParser.GetContentUrl` register theirs automatically; a widget that queries `GetDataItems` by hand must call this itself or it serves stale HTML until the cache profile expires. ServiceStack endpoints get the equivalent from `[EnableCache]` when the request DTO implements `IHasCacheDependency.GetCacheDependencyObjects()` (`Telerik.Sitefinity.Web.UI.ContentUI.Contracts`).

## Region disposables (context switches)

`using (new ElevatedModeRegion(manager))` - suppress security checks on one manager; `using (new AllProvidersAccessRegion())` - let `ManagerBase.TryGetMappedManager` reach every provider; `using (SiteRegion.FromSiteId(Guid siteId[, SiteContextResolutionTypes]))` / `SiteRegion.FromSiteMapRoot(Guid rootId, ...)` - run as a specific site (multisite, background code); `using (new CultureRegion(CultureInfo))` - run in a culture (URLs, `Lstring` reads); `UnrestrictedModeRegion` - drop security restrictions entirely (use `RunWithElevatedPrivilege` for the delegate form). All in `Telerik.Sitefinity.Data` / `Telerik.Sitefinity.Multisite` / `Telerik.Sitefinity.Localization`; the platform's own resolver code stacks `AllProvidersAccessRegion` + `ElevatedModeRegion` + `SiteRegion` + `CultureRegion` exactly this way.

## Identity, users, roles, permissions

`ClaimsManager` (`Telerik.Sitefinity.Security.Claims`, all static):

| Signature | Notes |
|---|---|
| `SitefinityIdentity GetCurrentIdentity(string label = null)` | The idiom (21 calls): `.IsAuthenticated`, `.UserId`, `.Name`, `.IsBackendUser`, `.Roles` |
| `Guid GetCurrentUserId()` | `Guid.Empty` when anonymous |
| `SitefinityPrincipal GetCurrentPrincipal()`, `bool IsBackendUser()`, `bool IsUnrestricted()` | |
| `SitefinityIdentity GetIdentity(HttpContext / HttpContextBase / ClaimsPrincipal, string label = null)` | For code that has a context but no current thread identity (handlers, background) |
| `void Logout(HttpContext context = null, ClaimsPrincipal principal = null)`, `string GetLogoutUrl(string redirectUri = "")` | |

`UserManager` (`Telerik.Sitefinity.Security`): `User GetUser(Guid id)` (74 calls), `GetUser(string userName)`, `GetUserByEmail(string email)`, `FindUser(Guid)` / `FindUser(string username)` (null instead of throwing), `IQueryable<User> GetUsers()`, `TimeZoneInfo GetUserTimeZone()`. `RoleManager`: `Role GetRole(Guid id)` / `GetRole(string roleName)`, `IQueryable<Role> GetRoles()`, `GetRolesForUser(Guid userId)`, `bool IsUserInRole(Guid userId, Guid roleId)` / `(Guid userId, string roleName)`, `AddUserToRole(User, Role)`, `RemoveUserFromRole(User, Role)`. Backend roles live in the `Backend/` provider, application roles in `App/` - get the right manager (`RoleManager.GetManager("AppRoles")` vs `GetManager()`) or `GetRole` returns null.

Object permissions (`Telerik.Sitefinity.Security.SecurityExtensions` / `SecuredObjectExtensions`, on any `ISecuredObject` - pages, content, media, dynamic content):

| Signature | Notes |
|---|---|
| `bool IsGranted(this ISecuredObject item, string permissionSet, params string[] actions)` | e.g. `page.IsGranted("Pages", "View")`; sets and action names are the `SecurityConstants.Sets.*` constants |
| `bool IsGranted(this ISecuredObject securedObject, SecurityActionTypes actionType, string permissionsSetName = null)` | Enum form: `SecurityActionTypes.View / Create / Modify / Delete / ...` |
| `bool IsDenied(...)` (same overload family), `void Demand(...)` (throws `UnauthorizedAccessException`) | |
| `IEnumerable<Permission> GetOwnPermissions(this ISecuredObject)`, `GetInheritedPermissions(...)`, `IQueryable<Permission> GetActivePermissions(...)`, `string[] GetActivePermissionActionsForCurrentUser(this ISecuredObject)` | Inspection |
| `void GrantActions(this Permission permission, bool append, params string[] actions)`, `DenyActions(...)`, `UngrantActions(...)`, `UndenyActions(...)` | Writes on a `Permission` row (bit math handled for you - the bit is `2^index` of the action in the set) |

Elevation: `SystemManager.RunWithElevatedPrivilege(RunWithElevatedPrivilegeDelegate delegateToRun[, object[] parameters[, string urlRequest]])` runs the delegate as an unrestricted user (background tasks, workflow messaging - 4 calls). For manager-scoped elevation prefer `using (new ElevatedModeRegion(manager)) { ... }` (`Telerik.Sitefinity.Data`), which suppresses security checks on that manager's provider only.

## Request context: design, preview, index, backend

| Helper | Notes |
|---|---|
| `SystemManager.IsDesignMode` (91 calls), `SystemManager.IsPreviewMode` (29), `SystemManager.IsInlineEditingMode` | Static bools (`Telerik.Sitefinity.Services`). Preview runs with `IsDesignMode == true` as well, so one check covers the editor and its preview |
| `control.IsDesignMode()`, `IsPreviewMode()`, `IsIndexingMode()`, `IsInlineEditingMode()`, `IsBackend()`, `IsInBrowseAndEditMode()` (extensions on `Control`, namespace `System.Web.UI`, class `ControlExtensions`) | WebForms-era but they work on the current page's controls; `IsIndexingMode()` reads `Page.Items["IsInIndexMode"]` - the same flag the `Util.IsIndexingMode` helper in the frontend guides checks |
| `SystemManager.CurrentHttpContext` (`HttpContextBase`) | Null-safe replacement for `HttpContext.Current` in Sitefinity code paths (scheduled tasks, indexing) |
| `SystemManager.CurrentContext.Culture`, `.CurrentSite`, `.IsMultisiteMode` | Current culture and site |
| `Config.Get<TConfig>()` (`Telerik.Sitefinity.Configuration`) | Typed access to any config section - `Config.Get<DataConfig>().ConnectionStrings["Sitefinity"]`, `Config.Get<SystemConfig>()`, `Config.Get<SecurityConfig>().Permissions[...]` |
| `SystemManager.RegisterServiceStackPlugin(IPlugin plugin, bool isHighPriority = false)`, `SystemManager.GetModule(string name)`, `SystemManager.RestartApplication(...)` | Platform-level operations |

## String and misc extensions (`System.SitefinityExtensions`, `Telerik.Sitefinity.SystemExtensions`)

`string Arrange(this string value, params object[] arguments)` (Sitefinity's own `string.Format` - ubiquitous in platform code), `bool IsNullOrEmpty(this string)`, `IsNullOrWhitespace(this string)`, `bool IsGuid(this string)`, `string Left/Right(this string, int)`, `Sub(this string, int startIndex, int endIndex)`, `UpperFirstLetter/LowerFirstLetter`, `ToPascalCase/TocamelCase(this string, char[] separators = null)`, `TruncateString(this string, int maxLength, TruncateOptions)`, `Base64Encode/Base64Decode`, `UrlEncode/UrlDecode`, `UrlTokenEncode/UrlTokenDecode`, `string ToQueryString(this NameValueCollection collection, bool startWithQuestionMark = true)`, `TValue GetAndRemove<TKey, TValue>(this IDictionary<TKey, TValue>, TKey key)`, `bool IsNullable(this Type)`, `bool ImplementsInterface(this Type type, Type interfaceType)`.

## Querying tips (`Telerik.Sitefinity.Data.QueryableExtensions`)

`IQueryable<T> WhereAnyCulture<T>(this IQueryable<T>, Expression<Func<T, bool>> predicate)` (search across translations), `WhereLstringCompares<T>(this IQueryable<T>, string propName, QueryCompareMode mode, string val, CultureInfo culture)` (server-side `Lstring` comparison - a plain `.Where(x => x.Title == "...")` on an `Lstring` does not translate), `WhereQueryTypeIsTranslatedInCulture<T>(..., CultureInfo)`, `UnionOrRight<T>(...)`. Remember that OpenAccess `IQueryable` runs on the server until you call `ToList()`; project into DTOs *after* materializing, and filter `Status == ContentLifecycleStatus.Live && Visible` before paging.

## What to avoid

- `DateTime.Now` / `ToLocalTime()` for display - use `ToSitefinityUITime()`; `DateTime.Now` for storage - use `DateTime.UtcNow`.
- Building media or thumbnail URLs from `sf_media_content` columns - use `MediaUrl` / `ResolveThumbnailUrl(profile)`.
- `HttpContext.Current.User.Identity` - use `ClaimsManager.GetCurrentIdentity()` (Sitefinity's claims identity carries the user id and roles).
- Hand-rolled permission bit math - `IsGranted` / `GrantActions` do it.
- `Type.GetType("...")` for Module Builder types - `TypeResolutionService.ResolveType` knows the dynamically compiled assemblies.
- Reading `App_Data\Sitefinity\Configuration\*.config` from disk - `Config.Get<T>()` is the merged, environment-correct view.

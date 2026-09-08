---
name: sitefinity-adminapp-extensions
description: Use this skill when extending the Sitefinity admin backend UI (the Angular "AdminApp") with an extensions bundle - setting up and building the extensions project, custom field editors (content types AND widget designers), custom commands, grid columns, item lifecycle hooks, field-change bindings, rich-text editor toolbar items, whole-designer overrides, notifications, selectors, themes, tree nodes, DAM providers, backend OData calls with HTTP_PREFIX, the dev server, and deploying the bundle so Sitefinity discovers it. Verified against the compiled 15.4 AdminApp host bundle, a decompile of Telerik.Sitefinity.AdminBridge.dll, the 15.3 SDK typings and a production 15.4 site.
---

You are a Sitefinity AdminApp extensions expert. AdminApp extensions are Angular/TypeScript bundles that inject components and providers into Sitefinity's Angular-based admin backend (the "AdminApp" that renders content lists, content-edit screens, widget designers, the rich-text editor and the media selectors). This skill is the AdminApp counterpart to the frontend widget work in `sitefinity-widget-expert`; the two meet at the same content type from opposite ends (widget = frontend render, extension = backend edit UI).

**Verification baseline.** The ground truth for everything below is the **compiled 15.4.8636.74 AdminApp host bundle** (`AdminApp/main.*.js` of a running site) and a decompile of **`Telerik.Sitefinity.AdminBridge.dll`** (the server side that serves bundles). The `@progress/sitefinity-adminapp-sdk` typings (`app/api/v1/**/*.d.ts`, package `15.3.8523`) and the official repos were used as secondary sources and were checked against the host wherever they make a runtime claim. **Trust ranking, because the official material lags:** the dev-kit repo `Sitefinity/sitefinity-admin-app-extensions` is maintained (tag `15.4.8636.0`, commits in 2026) but its README still recommends Node v16, which cannot build its own Angular 19 pipeline; the samples repo `Sitefinity/sitefinity-admin-app-extensions-samples` has **no tags and no commits since mid-2025**, and at least one of its registrations (`FieldTypes.shortTextDefault` for an ordinary short-text field) does not match what the host emits. Where this skill and a sample disagree, the skill reflects the host. **Version cutoff: Sitefinity 15.4.** Re-verify against your host's bundle after an upgrade (the FIELD_DATA trick below takes a minute).

## What AdminApp extensions are

- Angular bundles compiled to a single JS file and dropped into the Sitefinity web app's `AdminApp/` folder. The server discovers them by filename and serves them concatenated at boot - there is **no config setting** pointing at a bundle (see "Bundle naming & discovery").
- Extensions run **inside the host's Angular app**, sharing its Angular runtime and dependency-injection tree. You are not shipping a standalone SPA; you contribute NgModules and multi-providers into an app you do not control, and you link against the host's copy of the SDK at runtime (the SDK package's `field-types.js` is literally a stub - `/* will be replaced runtime */`).
- Supported from Sitefinity `11.0.6700.0`. Breaking changes: **13.1** (Angular 9 + Angular CLI pipeline), **13.3** (package renamed to the scoped `@progress/sitefinity-adminapp-sdk`).
- Official repos: **https://github.com/Sitefinity/sitefinity-admin-app-extensions** (the dev kit: Angular CLI + custom-webpack build) with **https://github.com/Sitefinity/sitefinity-admin-app-extensions-samples** as a git submodule (`samples/`). API reference: http://admin-app-extensions-docs.sitefinity.site and https://component-framework-docs.sitefinity.site.

## Project setup and build (the official kit)

```powershell
git clone https://github.com/Sitefinity/sitefinity-admin-app-extensions --recurse-submodules
cd sitefinity-admin-app-extensions
git checkout 15.4.8636.0            # the tag that equals Administration > Version & Licensing on your host
git submodule update --init --recursive
npm install                          # Node 20 or 22 LTS (Angular 19 needs ^18.19.1 || ^20.11.1 || ^22); the README's "v16" is stale
npm start                            # dev server on http://localhost:3000
npm run build                        # dist/sample.extensions.bundle.js
npm run build:prod                   # minified (Sitefinity >= 11.1)
```

What the kit is, file by file (as shipped and as used in production):

- `angular.json` - builder `@angular-builders/custom-webpack:browser`, `main: src/__extensions_index.ts`, `outputPath: dist`, `aot: false`, `vendorChunk: false`, `customWebpackConfig.path: ./build/webpack-custom.ts` with `mergeStrategies.externals: "replace"`. The `prod` configuration turns `optimization` on; the `dev` configuration copies the SDK's `wwwroot` (a full AdminApp build) next to your bundle so `ng serve` can host the whole admin.
- `build/webpack-custom.ts` - renames the entry (`config.entry = { "sample.extensions.bundle": mainBundlePath }`), adds `ContextReplacementPlugin` for `@angular/core`, and the **`ImportPlugin` fed by `@progress/sitefinity-adminapp-sdk/manifest.json`**. The manifest lists every SDK module path; the plugin marks them as webpack **externals**, so your bundle contains none of the SDK or Angular and binds to whatever the host loaded. `optimization.moduleIds = "natural"`, `runtimeChunk = false`, dev-server `historyApiFallback = true`, `hot = false`.
- `tsconfig.app.json` - `files: ["src/__extensions_index.ts"]`; everything reachable from the entry compiles, nothing else.
- `package.json` (15.3/15.4 era): `@progress/sitefinity-adminapp-sdk` `15.3.8523`, `@progress/sitefinity-component-framework` `12.0.0`, Angular `19.2.x`, `@angular-builders/custom-webpack` `19.0.0`, `typescript` `5.5.4`, `webpack` `5.98`. Scripts: `start` = `ng serve --port 3000`, `build` = `ng build`, `build:prod` = `ng build -c prod`, `submodule:update`.

**Version alignment, stated precisely.** The SDK package version tracks the product build (`15.3.8523` = the 15.3 release), and the host provides the runtime implementation. A bundle built against `15.3.8523` runs on a `15.4.8636` host (that is exactly what the reference site does) because the externals resolve at runtime and the v1 API is stable - but you get the *typings* of the older SDK, so contracts added in the newer release are invisible to you, and a removed member fails at runtime, not at build. Rule: bump the SDK to the tag that matches the host on every upgrade, rebuild, redeploy, retest.

**Angular note.** Components are `standalone: false` and listed in the NgModule `declarations`. The samples' README still says "register them in `entryComponents`"; that property is gone since Angular 13 - `declarations` is sufficient and the samples' own modules do exactly that.

## Bundle naming & discovery

This is the single most important operational fact and the most common reason "my extension does nothing". The exact mechanism, from `Telerik.Sitefinity.AdminBridge.dll` (`FileSystemExtensionsProvider`, `ExtensionsHttpHandler`) and the host's boot code:

1. The output bundle **MUST** be named `{bundle-name}.extensions.bundle.js`. The server globs **`*.extensions.bundle.js`** in **`~/adminapp`, top directory only** (no subfolders). Rename it from the sample default by changing the entry key in `build/webpack-custom.ts`.
2. Deploy it into the **`AdminApp/`** folder of the Sitefinity web application (the same folder that holds the host's `main.*.js`, `index.html`, fonts). No registry, no web.config entry, no database record.
3. **Multiple bundles are supported.** The server reads every match **ordered by file name**, concatenates them with a newline (`BundleTransformer.Transform` is literally `AppendLine` per file), and serves the result from **`GET /admin-bridge/extensions`** with caching and gzip. The client fetches that URL by XHR, injects the text as an **inline `<script>`**, then collects every module registered through `sitefinityExtensionsStore` that has Angular's `ɵmod` marker and pushes them into the root module's `imports` before `bootstrapModule`. A module without that marker (not an `@NgModule`) is dropped with "Extensions were loaded, but were not compatible."
4. **The concatenated bundle is cached server-side for 1 day** (`SystemManager.Cache`, key `AdminAppExtensions`) and invalidated when the AdminBridge module loads - i.e. on **application restart**. So: redeploy the file, **restart the application** (recycle the app pool), hard-refresh the browser. On a dev box set `<add key="sf:DisableAdminAppExtensionsCache" value="true"/>` in `web.config` appSettings to re-read the folder on every request.
5. Because bundles are concatenated in **file-name order**, that order is also the order extension providers are consulted (first `overrideField` that returns non-null wins). Prefix names (`010-fields.extensions.bundle.js`) if two bundles could claim the same field.
6. Ship `.js` only; the `.js.map` is for development. A parse error in **any** bundle breaks the whole inline script, i.e. every bundle - check the console's first red line.
7. `sf:DisableAdminApp=true` (appSettings) turns the whole AdminApp off. The `AdminAppExtensionsConfig` section (`featureEnabled`, `customFieldsEnabled`, `rteEnabled`) is registered but **read by nothing on either side** - do not hunt for a switch there; there is none.

A deploy script that works: `ng build -c prod && xcopy /E /Y /I dist\* ..\YourSite\AdminApp\`. Keep the bundle out of source control if the site's `AdminApp/` is restored from the NuGet content package on build (see `sitefinity-debloat-repo`), or the next hydrate deletes it - copy it in as a build step instead.

## Entry point & registration

The entry file talks to a **host-provided global**, `sitefinityExtensionsStore`, whose whole API is one method (`extensions-store.d.ts`):

```typescript
// src/__extensions_index.ts
import { SitefinityExtensionStore } from "@progress/sitefinity-adminapp-sdk/app/api/v1";
import { MyExtensionsModule } from "./my-extensions.module";

declare const sitefinityExtensionsStore: SitefinityExtensionStore;   // injected by the host before your bundle runs
sitefinityExtensionsStore.addExtensionModule(MyExtensionsModule);
```

Your NgModule declares the components the host will instantiate dynamically, imports the SDK's `FrameworkModule` plus the component-framework `Sf*Module`s you use, and **provides each extension as an Angular multi-provider on its SDK token**:

```typescript
@NgModule({
    declarations: [EventDateWriteComponent, EventDateReadonlyComponent, PrintPreviewComponent, ImageCellComponent],
    imports: [CommonModule, FormsModule, FrameworkModule, SfInputModule, SfSearchModule, SfChipModule, SfLoaderModule, SfButtonModule],
    providers: [
        CUSTOM_FIELDS_PROVIDER,        // { provide: FIELDS_PROVIDER_TOKEN, useClass: ..., multi: true }
        COMMANDS_PROVIDER,             // { provide: COMMANDS_TOKEN, ... }
        COLUMNS_PROVIDER,              // { provide: COLUMNS_TOKEN, ... }
        provideHttpClient(withInterceptorsFromDi())
    ]
})
export class MyExtensionsModule { }
```

Every extension point below follows the same shape: **a token from the SDK + `multi: true` + a class implementing the SDK interface**. The host collects all providers on a token (from every bundle) and asks each in turn. For field overrides the host sorts its own `DefaultFieldsProvider` to the **end**, so extension providers always get first refusal, in bundle order, and the first non-null `FieldRegistration` wins.

**Gotcha:** dynamically instantiated components (field editors, command dialogs, grid cells, custom designers) MUST be in `declarations`, or the host cannot create them and silently falls back to the default UI.

## Extension points (the complete SDK v1 surface)

| Area (`app/api/v1/<folder>`) | Token / entry | You implement | What it does |
|---|---|---|---|
| `custom-fields` | `FIELDS_PROVIDER_TOKEN` | `FieldsProvider.overrideField(data: FieldData): FieldRegistration` | Replace a field's write/readonly UI on content types **and** widget designers |
| `widget-editor` | `WIDGET_VIEW_TOKEN` | `WidgetEditorViewProvider.overrideView(context: { widgetName }): EditorViewComponent` | Replace an entire widget designer with your component (`WidgetEditor` interface) |
| `commands` | `COMMANDS_TOKEN`, `COMMANDS_FILTER_TOKEN` | `CommandProvider.getCommands(data)` / `getCategories(data)`; `CommandsFilter.filter(operations, data)` | Add commands to grid, sidebar, bulk and item Actions menus, the edit view; remove default ones |
| `index-component` | `COLUMNS_TOKEN` | `ListColumnsProvider.getColumns(entityData)` / `getColumnsToRemove(entityData)` | Add or remove grid columns; cell = your component implementing `DataContextComponent` |
| `item` | `ITEM_HOOKS_PROVIDER_TOKEN` | `ItemHooksProvider` (`onItemLoaded`, `onEditItemInitializing/Changed/Unloading`, `onGridItemsInitializing/Changed/Unloading`) | Lifecycle hooks on list and edit views |
| `fields-behaviour` | `FIELDS_CHANGE_SERVICE_TOKEN` | `FieldChangeService.canProcess(typeFullName)` / `processChange(changedFieldName, changedValue, fields: FieldWrapper[])` | Bind fields: when one changes, write another's value/settings |
| `editor` | `EDITOR_CONFIG_TOKEN`, `EDIT_MENU_TOKEN` | `EditorConfigProvider.getToolBarItems(editorHost)` / `getToolBarItemsNamesToRemove()` / `configureEditor(configuration)`; `EditMenuProvider.getButtons(element)` | Extend/configure the Kendo rich-text editor; add contextual edit-menu buttons (the spell-check sample) |
| `toolbar-items` | `TOOLBARITEMS_TOKEN` | `ToolBarItemsProvider.getToolBarItems(editorHost)` / `getToolBarItemsNamesToRemove()` | Editor toolbar buttons only (the images/videos/insert-symbol samples) |
| `selectors` | `SELECTOR_SERVICE` (inject) | - | `openImageLibrarySelector(options)`, `openVideoLibrarySelector(options)`, `openDialog(dialogData)` - reuse the host's media pickers and modal |
| `notifications` | `NOTIFICATION_SERVICE` (inject), `SYSTEM_NOTIFICATION_ICON_TOKEN` | `NotificationService.publishBasicNotification({ message, look, duration, closeButton, filterParam })`; `SystemNotificationIconProvider.parseIcon(key)` | Toasts (`NOTIFICATION_LOOK_SUCCESS/ERROR/WARNING`); icons for system notifications (`SYSTEM_NOTIFICATION_KEYS`) |
| `theme` | `THEME_TOKEN` | `ThemeProvider.getThemes(): ThemeItem[]` | Backend color themes via `ThemeVariables` (button colors, `GlobalOutline`, ...) |
| `tree` | `CUSTOM_TREE_COMPONENT_TOKEN` | `TreeNodeComponentProvider.getComponentData(feature: TreeNodeComponentFeatures, entitySet)`; component extends `CustomTreeNodeComponentBase` | Custom node rendering in the related-data tree selector |
| `dam` | `DAM_PROVIDER_TOKEN` | class extending `DamProviderBase` (`loadMediaSelector`, `isSupported(providerTypeName)`, `assetsSelected(assets: DamAsset[])`) | Plug an external digital-asset manager into the media selectors |
| `embed-media` | - | `EmbedMediaParser.canProcess(text)` / `parse(text): Promise<Media>` | Custom embed parsing (`MediaType.Tweet` / `IFrame`) |
| `guards` | `AuthGuard`, `ConfigurationGuard` | - | `canActivate` guards for routes you add with `RouterModule.forChild` (a command that opens a print-preview route) |
| `metadata` | - | - | `DataItem { data, metadata: Entity { key, setName, typeFullName }, provider, culture, key, title }` - the item shape every hook receives |
| `http` | `HTTP_PREFIX` | - | Prefix for authenticated backend OData calls |
| `framework` | `FrameworkModule` | - | Import it in every extension module |

Official sample folders, one per area: `custom-fields`, `widget-editor`, `commands-extender`, `grid-extender`, `item-extender`, `fields-change`, `editor-extender` (insert-symbol, word-count, spell-check, sitefinity-images, sitefinity-videos, switch-text-direction), `theme`, `tree` (related-data), `library-extender` (dam), `custom-system-notifications-icons`.

## Custom field editors - the flagship use case

### Registration (fields provider)

```typescript
import { ClassProvider, Injectable } from "@angular/core";
import { FIELDS_PROVIDER_TOKEN, FieldsProvider, FieldData, FieldRegistration, FieldTypes }
    from "@progress/sitefinity-adminapp-sdk/app/api/v1";

@Injectable()
export class CustomFieldsProvider implements FieldsProvider {
    // Called ONCE per field the AdminApp renders. Return a registration to override, or null/undefined.
    overrideField(key: FieldData): FieldRegistration {
        if (key.fieldType === FieldTypes.shortText && key.fieldName === "EventDate" && key.typeName === "events") {
            return { writeComponent: EventDateWriteComponent, readComponent: EventDateReadonlyComponent, settingsType: EventDateSettings };
        }
        return null;
    }
}
export const CUSTOM_FIELDS_PROVIDER: ClassProvider = { provide: FIELDS_PROVIDER_TOKEN, useClass: CustomFieldsProvider, multi: true };
```

`FieldRegistration` = `{ writeComponent: Type<any>; readComponent?: Type<any>; settingsType?: Type<any> }`. Always supply `readComponent`: the host renders it when the item is locked or the user lacks edit rights, and without one the field shows nothing in that state. The official sample keeps registrations as `{ key: FieldData, registration: FieldRegistration }` pairs in an array and loops them in `overrideField` - copy that.

### Matching: fieldName + fieldType + typeName

`FieldData` is `{ typeName, fieldName, fieldType }` (all readonly strings). The host does no matching itself - it hands the key to your `overrideField` and takes the first non-null answer - so the granularity is yours: the sample provider matches on all three, and its `findRegistration` also supports a registration with `fieldName: null` (all fields of that type per content type), `typeName: null` (that field name on every content type), or both null (every field of the type). The three values you compare against:

| Property | Meaning | Examples |
|---|---|---|
| `fieldName` | the field's programmatic name | `"EventDate"`, `"Title"` |
| `fieldType` | the SDK field-type id | `FieldTypes.shortText` (`"sf-short-text"`), `FieldTypes.textArea` (`"sf-text-area"`), `FieldTypes.shortTextDefault` (`"sf-short-text-default"`) |
| `typeName` | the entity set of the content type, or `widget-<WidgetName>` inside a widget designer | `"events"`, `"newsitems"`, `"taskitems"`, `"widget-AllProperties"` |

- **`shortText` vs `shortTextDefault` - the sample gets this wrong.** From the host's `getFieldTypeForString`: a short-text property becomes **`shortTextDefault`** only when it is the type's **default field** (the Title-style field, `metadata.defaultFieldName`) on a non-media type; an immutable property becomes `read`; the `Name` property of taxa, forms and metatypes becomes `urlName`; **every other short-text field is `shortText`**. The samples repo registers `shortTextDefault` for an ordinary field, which matches nothing - the production site had to switch to `shortText`. When in doubt, read the value off the console (FIELD_DATA trick) and use the matching member.
- **`FieldTypes` is a runtime-provided object** - the SDK's `field-types.js` is a stub the host replaces, and the typings only list member names. The **15.4 host defines 88 members**; the 15.3 typings declare 54. Members the host has and the typings do not (usable via a cast, `(FieldTypes as any).password`): `password`, `facetTaxa`, `aiTaxa`, `choiceParameterizedSelector`, `choiceServiceUrl`, `dropdownWithText`, `multipleChoiceList`, `titleRead`, `contentAll`, `itemList`, `templateThumbnail`, `resultsListSettings`, `listFieldMappingCss`, `range`, `rangeLimitation`, `imageDimensions`, `conditionalChips`, `attributes`, `fileTypes`, `pencilButton`, `taxonSelector`, `contentTypeSelector`, `languageSelector`, `dateAndTimeSelector`, `dateTimeSelector`, `dateTimeSelectorContent`, `formRedirectPageCompositeField`, `formNavigationSteps`, `linkSelector`, `fieldSelector`, `navigateToUrl`, `statusSelector`, `lookupField`, `rolesSelector`, `iframeField`. Two quirks: `date` and `dateTime` share the value `"sf-date-time"`, and `html` is `"sf-wrapper-html"`. A raw string in `fieldType` is acceptable when the typings lack the member (the host compares strings); otherwise use the enum member, and members are camelCase - there is no `FieldTypes.ShortText`.
- **Widget designers use the same registry.** The host builds the key as `"widget-" + context.widgetName` (`PropertyEditorWidgets.FIELD_TYPE_KEY`), so `typeName` is `widget-<WidgetName>` for a widget's top-level properties - the `widget-editor` sample matches `typeName.startsWith("widget-AllProperties")`. A designer property's type resolves as `FIELD_TYPE_MAPPER.get(Type) || FieldTypes[Type] || FieldTypes.shortText`, so plain string properties arrive as **`shortText`**. **Verified on the production site and in the host code:** properties of a *complex object inside a widget* (a `TeamMember.Email` inside `IList<TeamMember>`) arrive as **`typeName: "widget-undefined"`** - the nested editor's context has no `widgetName`, and the string concatenation does the rest. Registering `{ fieldName: "Email", fieldType: FieldTypes.shortText, typeName: "widget-undefined" }` therefore targets that sub-field in *every* widget that has one; there is no per-widget scoping at that depth. Undocumented; re-check with the FIELD_DATA trick after upgrades.
- **Two providers overriding the same field:** the first provider that returns non-null wins. Within one bundle that is Angular provider order; across bundles it is the server's file-name sort (see "Bundle naming & discovery"). Prefix bundle names if you rely on it; otherwise avoid overlapping registrations.

### The FIELD_DATA discovery trick

Log every field the host asks about, then copy the exact values off the console. Not an API - a technique, and the fastest way to get the three values right:

```typescript
overrideField(key: FieldData): FieldRegistration {
    console.log("FIELD_DATA:", JSON.stringify(key));   // TEMPORARY - remove before shipping
    return this.findRegistration(key);
}
```

Build, deploy, restart, hard-refresh, open the item (or the widget designer) and read lines like `FIELD_DATA: {"fieldType":"sf-short-text","fieldName":"EventDate","typeName":"events"}`.

### Field editor anatomy: three files per editor

**Write component** - extends `FieldBase`, whose actual public contract (`field-base.d.ts`) is:

```typescript
export declare class FieldBase {
    readonly focus$: Observable<boolean>;
    writeValue(value: any): void;                       // host -> field (and your own updates)
    getValue(): any;                                    // field -> host
    processErrors(errors: { [key: string]: any }): Array<string>;   // validator errors -> messages shown under the field
}
```

The compiled host class is wider than the typings: `value` is a real getter/setter pair that delegates to `getValue()` / `writeValue()`, `settings` is a getter/setter over `_settings`, and there are `context` (`{ dataItem, model }`, used by the base `ngOnInit` to seed the value from the item, including `RelatedDataProperties`), `status$`, `hidden$` + `setHidden(bool)`, `onFocus()` / `onBlur()`, `getWarnings(): string[]`, `postProcessValue(v)`, and a default `processErrors` that already turns `required` into the localized message and runs `settings` validators through `FieldValidation`. On the edit view the host also stamps **`settings.dataItem`** and **`settings.entityData`** onto every field's settings - that is where "the item I am editing" comes from. The official templates use `value` and `settings` directly:

```html
<!-- custom-field-write.component.html (official sample) -->
<sf-input [name]="settings.key" [look]="settings.look" [placeholder]="settings.placeholder"
          [recommendedCharacters]="settings.recommendedCharacters"
          ngDefaultControl [(ngModel)]="value" (onBlur)="onBlur()" (onFocus)="onFocus()">
</sf-input>
```

```typescript
@Component({ templateUrl: "./event-date-write.component.html", standalone: false })
export class EventDateWriteComponent extends FieldBase implements OnInit {
    constructor(private http: HttpClient) { super(); }

    ngOnInit(): void {
        const current = this.getValue() as string;                       // or (this as any).value
        const item = (this.settings as any)?.dataItem?.data;               // the item being edited: Id, Title, sibling fields (host-stamped, not in the typings)
    }

    onPicked(v: string): void { this.writeValue(v); }                     // persist through the host binding

    // Override to normalize what the host hands you (official array-of-guids sample splits "a,b,c" into an array)
    writeValue(value: any): void { super.writeValue(typeof value === "string" ? value.split(",").map(s => s.trim()) : value); }

    // Validator errors from settings.getValidators() arrive here; keep super() so "required" still renders
    processErrors(errors: { [key: string]: any }): string[] {
        const messages = super.processErrors(errors);
        return errors?.pattern ? messages.concat("Invalid email") : messages;
    }
}
```

Prefer `getValue()` / `writeValue()` (typed, documented) over `(this as any).value`; both work because the host's `value` accessor is exactly that pair (the production site uses the `any` form throughout). Item context via `settings.dataItem.data` is host behaviour, not a typed contract - guard it, and expect `data` to be null for a new item.

**Readonly component** - same base, display only, rendered when the item is locked or not editable.

**Settings class** - extends `SettingsBase` (`recommendedCharacters`, `init(metadata)`, `getValidators(): ValidatorFn[]`):

```typescript
export class EventDateSettings extends SettingsBase {
    init(metadata: any) { super.init(metadata); this.recommendedCharacters = 20; }
    getValidators(): ValidatorFn[] {
        const v = super.getValidators();                 // keep the host's (required, length, ...)
        v.push(Validators.pattern(/^\d{4}-\d{2}-\d{2}$/)); // Angular validators; errors reach processErrors
        return v;
    }
}
```

`getValidators` is the supported validation hook - use it instead of hand-rolled checks in the component so Save is blocked the same way built-in fields block it.

## Widget designers

Two levels, both from the `widget-editor` sample:

1. **One field** - a `FieldsProvider` matching `typeName === "widget-<WidgetName>"` (top-level properties) or `"widget-undefined"` (complex-object sub-fields, see above).
2. **The whole designer** - a `WidgetEditorViewProvider` on `WIDGET_VIEW_TOKEN` returning `{ componentData: { type: MyDesignerComponent } }` when `context.widgetName` matches. The component implements `WidgetEditor`: `initialize(widgetMetadata)` (gets `propertyValues`, `propertyMetadata` sections, `propertyMetadataFlat`, `name`, `caption`), `setValues(propValues)`, `validate(): Observable<ValidationResult>`, `actionExecuting(context: { processChanges })`, `getModifiedProperties(): PropertyData[]` (`{ Name, Value }` pairs the host persists). This is the escape hatch when the autogenerated designer (`sitefinity-designer-attributes`) cannot express the UI.

## Commands, columns, hooks, field bindings, editor - the patterns

All four samples are the same idiom; the interesting part is the target logic:

- **Commands** (`CommandProvider`): `getCommands(data: CommandsData)` decides by `data.target` (`CommandsTarget.List = 1`, `Edit = 2`, `EditLocked = 3` - present in the 15.4 host, absent from the 15.3 typings - `Bulk = 4`, `Create = 5`) and whether `data.dataItem.data` is null. Grid toolbar = `List` + null item, category **`"Default"`** (or **`"Settings"`** for the sidebar; the host's category set is `Default`, `Delete`, `Settings`, `Workflow`, `ContentLocations`, `LibrariesGridView`, `Personalization`, `Personalized`, `PersonalizedWidgets`, `ABTesting*` - anything else is not shown); item Actions menu = `List` + item; Bulk menu = `Bulk`; edit view Actions = `Edit` + item. Each `CommandModel` (`name`, `title`, `category`, `ordinal`, `token: { type: MyCommand, properties: { dataItem } }`) points at a class implementing `Command.execute(context: ExecutionContext): Observable<any>`; register the command classes as providers too. `ExecuteOnceInBulkCommand` (`executeOnceInBulk: true`) runs once for a bulk selection. `CommandsFilter` on `COMMANDS_FILTER_TOKEN` removes default commands. A command can navigate to a route you register with `RouterModule.forChild([{ path, component, canActivate: [ConfigurationGuard, AuthGuard] }])`.
- **Grid columns** (`ListColumnsProvider`): return `ColumnModel { name, title, ordinal, componentData: { type: CellComponent }, hidden, clickable, css }`; the cell component implements `DataContextComponent` and receives `context: { dataItem, model }`. Ordinals (host constants): Main is `Number.MIN_SAFE_INTEGER` (Marketing and Translations sit at `+1`/`+2`), Insight `MAX_SAFE_INTEGER - 2`, Actions `MAX_SAFE_INTEGER` - and the host moves the `Actions` column to the end regardless of ordinal. Built-in columns sit at 100, 200, 300...; pick 201..299 to land between the second and third built-in columns, never a multiple of 100. `getColumnsToRemove` returns names like `"LastModified"`.
- **Item hooks** (`ItemHooksProvider`): all methods optional; the `Observable<void>` ones let you await work before the host proceeds. `item.data` is null in `onItemLoaded` for a new item.
- **Field bindings** (`FieldChangeService`): `canProcess(typeFullName)` (full CLR name, e.g. `Telerik.Sitefinity.News.Model.NewsItem`) then `processChange` finds the target by `fields.find(f => f.fieldModel.key === "Content")` and calls `writeValue` / mutates `fieldModel.settings`.
- **Editor toolbar** (`ToolBarItemsProvider` / `EditorConfigProvider`): `getToolBarItems(editorHost)` returns `{ name, tooltip, ordinal, template?, exec }`; inside `exec`, `editorHost.getKendoEditor()` gives the Kendo instance (`getRange()`, `selectRange()`, `paste(html)`, `trigger("change")`). Only default Kendo items can be removed. Use `SELECTOR_SERVICE` to reuse the image/video library pickers, and remember dynamic-link rules when pasting media (see `sitefinity-widget-expert`, "Dynamic links").

## Backend OData calls with HTTP_PREFIX

```typescript
import { HTTP_PREFIX } from "@progress/sitefinity-adminapp-sdk/app/api/v1";
this.http.get(`${HTTP_PREFIX}/sf/system/newsitems(${id})?$select=Title`).subscribe(...);
```

- **`HTTP_PREFIX` is a sentinel, not a path.** Its value is the literal string `"sfprefix"`. The host registers an `HTTP_INTERCEPTORS` entry that catches every request whose URL starts with it, strips the sentinel, prepends the configured site URL (dropping a leading slash), appends `sf_site` for the current site, and attaches the backend auth header. So the full path after the sentinel is yours to write (`${HTTP_PREFIX}/sf/system/...`, `${HTTP_PREFIX}/sf/api/v1/system/...` on a Next backend), and a request without the sentinel gets **no auth and no site context** - that is why a plain `/sf/system/...` call comes back 401/403.
- **Your own ServiceStack / frontend endpoints** (`/RestApi/...`, `/api/default/...`) are a different surface: call them by plain path with `HttpClient` - the backend user's cookie session authenticates them if your service checks Sitefinity identity (`sitefinity-servicestack-api`). The production contact picker does exactly this against a `/RestApi/user/search` service; it also debounces (300 ms) and cancels the previous subscription, which any autocomplete editor should copy.
- Set the OData services' CORS (`WebServices > Routes > Sitefinity > services > system > Access Control Allow Origin`) to the dev origin when running under `ng serve`.

## What you can reuse from the host - and what you cannot

Three tiers, verified against the SDK and component-framework packages:

1. **Component framework (`@progress/sitefinity-component-framework`) - fully reusable.** Nineteen `Sf*Module`s exporting these selectors: `sf-input`, `sf-search`, `sf-select`, `sf-checkboxes`, `sf-radio-group`, `sf-chip`, `sf-button`, `sf-switch`, `sf-loader`, `sf-chart-loader`, `sf-field-wrapper`, `sf-list-builder`, `sf-tabs`, `sf-notification`, `sf-icon`, `sf-badge`, `sf-tooltip`, `sf-error`, `sf-warning`, `sf-markup-generator`, `sf-component-loader`. Import the module, use the tag. This is the visual vocabulary of the AdminApp and the reason a well-built extension is indistinguishable from a native screen.
2. **Host pickers exposed through services - reusable, but only these.** `SELECTOR_SERVICE` gives you the **image library selector** (`openImageLibrarySelector({ multiple })` returning `DataItem[]`), the **video library selector** (`openVideoLibrarySelector` returning `{ url, thumbnailUrl }[]`) and the generic **modal** (`openDialog({ componentData: { type: YourComponent }, commands })`, with `BUTTON_PRIMARY_CATEGORY` / `BUTTON_CANCEL_CATEGORY` / `BUTTON_DELETE_CATEGORY` for the footer). `NOTIFICATION_SERVICE` gives toasts. The rich-text editor is reachable from a toolbar item through `editorHost.getKendoEditor()`.
3. **The host's own field editors - NOT reusable as components.** The related-data picker, taxonomy picker, document/media field, date-time, link selector, choice chips and the other 80-odd `FieldTypes` are host-internal components. `FieldTypes` values are *ids the host maps to its components* when it renders a field you did not override; the SDK exports **no** component classes (`grep "export declare class .*Component"` over `app/api/v1` returns nothing), so you cannot drop `<sf-related-data>` into your template or `import { RelatedDataComponent }`. The ways around it, in order of preference: (a) do not override the field - let the host render its editor and use a `FieldChangeService` binding or an `ItemHooksProvider` to react to it; (b) override a *different* field and read the host-rendered one through `fields` in `processChange`; (c) rebuild the picker from tier 1 components plus an `HTTP_PREFIX` OData query (that is what a document picker, a user picker or a related-content autocomplete costs: ~150 lines, as the production contact picker shows); (d) for whole-designer overrides (`WIDGET_VIEW_TOKEN`) the same limit applies - your component gets `propertyMetadata`, not the host's field components.

## Component framework quick reference

Reusable Angular UI from `@progress/sitefinity-component-framework` (v12) - use these so your editor visually matches the native AdminApp:

| Component | Module | Purpose |
|---|---|---|
| `sf-input` | `SfInputModule` | text/number input with label, hint, validation (`[name]`, `[look]`, `[placeholder]`, `[recommendedCharacters]`, `(onBlur)`, `(onFocus)`) |
| `sf-search` | `SfSearchModule` | debounced search box, clearable |
| `sf-select` / `sf-checkboxes` / `sf-radio-group` | `SfChoicesModule` | single select, multi-select, radio group |
| `sf-chip` | `SfChipModule` | removable/editable chip |
| `sf-button` | `SfButtonModule` | button (`look`: large/small/action/delete) |
| `sf-switch` | `SfSwitchModule` | on/off toggle |
| `sf-loader` | `SfLoaderModule` | skeleton / progressbar / solid-block loader (`type="skeleton" [height]="48"`) |
| `sf-field-wrapper` | `SfFieldWrapperModule` | label + validation chrome around a field - use it so your label matches native fields (`sf-input`'s own `[label]` renders the compact table-cell label) |
| `sf-list-builder` | `SfListBuilderModule` | dynamic add/edit/remove list |
| `sf-tabs` / `sf-notification` / `sf-icon` / `sf-badge` / `sf-tooltip` | `SfTabsModule` / `SfNotificationModule` / `SfIconModule` / `SfBadgeModule` / `SfTooltipModule` | tabs, inline alert, Font Awesome icon (name without `fa-`), badge, tooltip |

**Gotcha:** there is **no** autocomplete / combobox / typeahead component. Build it from `sf-input` + a positioned results list (the production contact picker is ~150 lines of component CSS for exactly that); use `mousedown` on result rows so a blur-triggered dismiss does not eat the click, and delay the dismiss.

## Dev workflow

```powershell
npm start                     # ng serve --port 3000 - serves the SDK's wwwroot AdminApp + your bundle, live rebuild
```

Host-side settings so the running Sitefinity accepts the dev-served admin (all under Administration > Settings > Advanced):

1. **Security > `AccessControlAllowOrigin`** = `http://localhost:3000` (or `*` on a dev box).
2. **Authentication > OAuthServer > AuthorizedClients**: add `ClientId = sitefinity`, blank secret, redirect URL `http://localhost:3000/auth/oauth/sign-in`.
3. **WebServices > Routes > Sitefinity > services > system > Access Control Allow Origin** = `http://localhost:3000`.

Then open `http://localhost:3000` and log in through the host's STS. In this mode the host runs with environment `devext` and loads **`/sample.extensions.bundle.js`** from the dev server root (hard-coded in the host's `getExtensionsUrl`) instead of `/admin-bridge/extensions` - so during `ng serve` keep the entry name `sample.extensions.bundle`, and rename only for the deployed build. Production: `npm run build:prod`, copy to `AdminApp/`, restart.

## Verify it works

1. Bundle present: `<name>.extensions.bundle.js` sits in the web app's `AdminApp/` folder.
2. Restart the application (the server caches the concatenated bundle for a day), then hard-refresh the admin (Ctrl+F5).
3. `GET /admin-bridge/extensions` on the site returns your code (open it in a tab; it is the concatenation of every bundle). Empty = wrong folder or suffix.
4. Registration ran: your provider's constructor log (or the `FIELD_DATA:` lines) appears in the console. Silence with a non-empty endpoint = a load error (open the console's first red line - an SDK member missing at runtime means a version mismatch, "were not compatible" means the registered object is not an `@NgModule`).
5. The editor renders: open an item of the matching `typeName` (or the widget designer) - write component in edit, readonly component when the item is locked.
6. After a Sitefinity upgrade: rebuild against the matching SDK tag, redeploy, repeat 1-5. The bundle keeps loading against a newer host, which is precisely why silent breakage goes unnoticed.

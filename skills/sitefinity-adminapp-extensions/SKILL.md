---
name: sitefinity-adminapp-extensions
description: Use this skill when extending the Sitefinity admin backend UI with Angular AdminApp extensions - building custom field editors that override backend content-edit fields, registering NgModules against the host extension store, matching fields by fieldName/fieldType/typeName, calling authenticated backend OData with HTTP_PREFIX, wiring the dev server, and deploying the extensions bundle so Sitefinity discovers it.
---

You are a Sitefinity AdminApp extensions expert. AdminApp extensions are Angular/TypeScript bundles that inject custom components into Sitefinity's Angular-based admin backend (the "AdminApp" that renders content-edit screens, the toolbox, and provider dialogs). The flagship use case is the **custom field editor**: replacing the default write/readonly UI Sitefinity generates for a content field with your own Angular component - a color-coded status picker, an autocomplete backed by a backend service, a custom validation widget, and so on. This skill is the AdminApp counterpart to the frontend widget work covered in `sitefinity-widget-expert`; the two meet at the same content type but from opposite ends (widget = frontend render, extension = backend edit UI).

**Version baseline: Sitefinity 15.4** - the AdminApp SDK is versioned in lockstep with the Sitefinity host, so the SDK npm package version tracks the product build. Check the target project's version: `(Get-Item "<site>\bin\Telerik.Sitefinity.dll").VersionInfo.FileVersion` (e.g. `15.4.8630.0` = Sitefinity 15.4). Extensions are supported from Sitefinity `11.0.6700.0` onward. The product version is also visible in the backend under **Administration => Version & Licensing**.

## What AdminApp extensions are

- Angular bundles compiled to a single JS file and dropped into the Sitefinity web app's `AdminApp/` folder. Sitefinity's admin shell discovers and loads them at runtime - there is **no config setting** pointing at a bundle; discovery is purely folder + filename convention (see "Bundle naming & discovery").
- Official SDK repo: **https://github.com/Sitefinity/sitefinity-admin-app-extensions** (the development kit with the webpack build that produces the extensions bundle; the example implementations live in **sitefinity-admin-app-extensions-samples**, referenced as a submodule). **Gotcha:** check out the git tag that equals your Sitefinity product version before building (e.g. `git checkout 13.3.7600.0`) - the repo pins SDK versions per release, and a mismatched checkout will pull an SDK that does not line up with your host.
- Extensions run inside the host's Angular app, sharing its Angular runtime and dependency-injection tree. You are not shipping a standalone SPA - you are contributing NgModules and providers into an app you do not control.

## Stack & packages

As of the Sitefinity 15.3/15.4 era, an extensions project uses:

```json
{
  "devDependencies": {
    "@progress/sitefinity-adminapp-sdk": "15.3.8523",
    "@progress/sitefinity-component-framework": "12.0.0",
    "@angular/core": "^19.0.0",
    "@angular-builders/custom-webpack": "^19.0.0",
    "typescript": "~5.5.0",
    "webpack": "^5.0.0"
  }
}
```

- **`@progress/sitefinity-adminapp-sdk`** - the extension API surface (`FieldBase`, `SettingsBase`, `SitefinityExtensionStore`, `FieldsProvider`, `HTTP_PREFIX`, `FrameworkModule`). Its version **mirrors the Sitefinity build** (e.g. `15.3.8523`), so bump it to match every time you upgrade the host.
- **`@progress/sitefinity-component-framework`** (`12.0.0`) - the reusable Angular UI components the AdminApp itself is built from (`sf-input`, `sf-select`, `sf-chip`, ...). See the quick reference below.
- Angular 19.x, `@angular-builders/custom-webpack`, webpack 5, TypeScript 5.5.

**Breaking-change history worth knowing:**

- **13.1** - migrated to Angular v9 + the Angular CLI build pipeline.
- **13.3** - the SDK package was renamed from `sitefinity-adminapp-sdk` to **`@progress/sitefinity-adminapp-sdk`** (scoped). Old imports break on upgrade.

**Gotcha:** rebuild your extensions on **every** Sitefinity upgrade. The bundle links against the host's SDK at runtime via an externals manifest (below); a bundle built against an older SDK can silently misbehave when the host ships a newer one.

## Bundle naming & discovery

This is the single most important operational fact and the most common reason "my extension does nothing":

1. The output bundle **MUST** be named `{bundle-name}.extensions.bundle.js`. The `.extensions.bundle.js` suffix is the discovery contract.
2. Deploy it into the **`AdminApp/`** folder of the Sitefinity web application.
3. Sitefinity scans `AdminApp/` for **any** file matching `*.extensions.bundle.js` - **multiple bundles are supported**, each loaded independently. There is no registry, no web.config entry, no database record. Folder + filename is the entire mechanism.
4. **Restart the application** after deploying (recycle the app pool / touch `web.config`). The bundle list is read at admin-app boot.

Webpack forces the output name via the entry key in `webpack-custom.ts`:

```typescript
// webpack-custom.ts (custom-webpack builder config)
config.entry = { "my-project.extensions.bundle": mainBundlePath };
```

The SDK ships a **`manifest.json`** that feeds an `ImportPlugin`. That plugin marks the SDK modules as **externals** so they are NOT bundled into your JS - instead your bundle links against the host's already-loaded SDK at runtime. This is why version alignment matters: you are dynamically binding to whatever SDK the host provides.

## Entry point & registration

The entry file (e.g. `__extensions_index.ts`) talks to a **host-provided global**, `sitefinityExtensionsStore`, and hands it your NgModule:

```typescript
// __extensions_index.ts
import { SitefinityExtensionStore } from "@progress/sitefinity-adminapp-sdk/app/api/v1";
import { MyExtensionsModule } from "./my-extensions.module";

// The host injects this global before your bundle runs.
declare const sitefinityExtensionsStore: SitefinityExtensionStore;

sitefinityExtensionsStore.addExtensionModule(MyExtensionsModule);
```

Your NgModule declares the write/readonly components, imports the SDK's `FrameworkModule` plus any component-framework `Sf*Module`s you use, and provides the fields provider and the Angular HTTP client:

```typescript
// my-extensions.module.ts
import { NgModule } from "@angular/core";
import { provideHttpClient, withInterceptorsFromDi } from "@angular/common/http";
import { FrameworkModule } from "@progress/sitefinity-adminapp-sdk/app/api/v1";
import { SfInputModule, SfSelectModule } from "@progress/sitefinity-component-framework";

import { EventDateWriteComponent } from "./custom-fields/event-date/event-date-write.component";
import { EventDateReadonlyComponent } from "./custom-fields/event-date/event-date-readonly.component";
import { CUSTOM_FIELDS_PROVIDER } from "./custom-fields/custom-fields.provider";

@NgModule({
    declarations: [EventDateWriteComponent, EventDateReadonlyComponent],
    imports: [FrameworkModule, SfInputModule, SfSelectModule],
    providers: [
        CUSTOM_FIELDS_PROVIDER,
        provideHttpClient(withInterceptorsFromDi())
    ]
})
export class MyExtensionsModule { }
```

**Gotcha:** field editor components are instantiated **dynamically** by the host, not referenced in any template. They MUST appear in the NgModule `declarations` array or Angular will not know how to create them, and the field silently falls back to the default editor.

## Custom field editors - the flagship use case

A field editor overrides the UI for one (or many) content fields. Registration goes through a **fields provider** - a multi-provider the host queries once per rendered field.

```typescript
// custom-fields.provider.ts
import { ClassProvider } from "@angular/core";
import { FIELDS_PROVIDER_TOKEN, FieldsProvider, FieldData, FieldRegistration, FieldTypes }
    from "@progress/sitefinity-adminapp-sdk/app/api/v1";
import { EventDateWriteComponent } from "./event-date/event-date-write.component";
import { EventDateReadonlyComponent } from "./event-date/event-date-readonly.component";
import { EventDateSettings } from "./event-date/event-date.settings";

export class CustomFieldsProvider implements FieldsProvider {
    // Called ONCE per field the AdminApp renders. Return a registration to
    // override, or null/undefined to let the default editor handle the field.
    overrideField(fieldRegistryKey: FieldData): FieldRegistration {
        if (fieldRegistryKey.fieldType === FieldTypes.shortTextDefault   // "sf-short-text"
            && fieldRegistryKey.fieldName === "EventDate"
            && fieldRegistryKey.typeName === "events") {
            return {
                writeComponent: EventDateWriteComponent,
                readComponent: EventDateReadonlyComponent,
                settingsType: EventDateSettings
            };
        }
        return null;
    }
}

export const CUSTOM_FIELDS_PROVIDER: ClassProvider = {
    provide: FIELDS_PROVIDER_TOKEN,
    useClass: CustomFieldsProvider,
    multi: true   // multiple providers can coexist; the host asks each in turn
};
```

### Matching: fieldName + fieldType + typeName

The host passes a `FieldData` "registry key" describing the field being rendered. Matching compares **three** properties - **all three must line up**:

| Property | Meaning | Example |
|---|---|---|
| `fieldName` | the field's programmatic name on the content type | `"EventDate"` |
| `fieldType` | the SDK field-type id (import the `FieldTypes` enum) | `"sf-short-text"`, `"sf-text-area"` |
| `typeName` | the content type the field lives on | `"events"`, `"newsitems"` |

**Wildcard match:** a registration where `fieldName === null && typeName === null` (matching only on `fieldType`) overrides **every** field of that type across the whole AdminApp. Use this sparingly - e.g. to restyle all `sf-short-text` fields - because it is a blunt, site-wide instrument.

`fieldType` values are SDK ids like `"sf-short-text"` and `"sf-text-area"`; prefer the importable `FieldTypes` enum over raw strings so a typo is a compile error instead of a silent no-match. **Gotcha:** the enum members are camelCase (`FieldTypes.shortTextDefault`, `FieldTypes.textArea`, `FieldTypes.choiceYesNo`, ...) - there is no PascalCase `FieldTypes.ShortText`. See `@progress/sitefinity-adminapp-sdk/app/api/v1/custom-fields/field-types.d.ts` for the full list.

## The FIELD_DATA discovery trick

You almost never know the exact `fieldName` / `typeName` up front, and guessing wastes rebuild cycles. Log every field the host asks about, then read the answer off the console. This is a **debugging technique, not documented API** - but it is the fastest way to get the three matching values right.

```typescript
overrideField(fieldRegistryKey: FieldData): FieldRegistration {
    // TEMPORARY: dump every field the AdminApp renders so we can copy the exact keys.
    console.log("FIELD_DATA:", JSON.stringify(fieldRegistryKey));
    // ... real matching below ...
    return null;
}
```

Then:

1. Build and deploy the bundle, restart, hard-refresh the admin.
2. Open the content item whose field you want to override.
3. Watch the browser console for lines like:
   `FIELD_DATA: {"fieldType":"sf-short-text","fieldName":"EventDate","typeName":"events"}`
4. Copy those exact three values into your match condition.
5. Remove the `console.log` before shipping.

## Field editor anatomy - three files per editor

Each editor is a trio: a **write** component (edit mode), a **readonly** component (display mode), and a **settings** class.

### Write component (edit mode)

```typescript
// event-date-write.component.ts
import { Component, OnInit } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { FieldBase, HTTP_PREFIX } from "@progress/sitefinity-adminapp-sdk/app/api/v1";

@Component({
    selector: "event-date-write",
    templateUrl: "./event-date-write.component.html",
    standalone: false   // declared in the NgModule, not standalone
})
export class EventDateWriteComponent extends FieldBase implements OnInit {

    constructor(private http: HttpClient) {
        super();   // FieldBase must be constructed
    }

    ngOnInit(): void {
        // The bound value lives on `value` (typed loosely - cast through `any`).
        const current = (this as any).value as string;

        // Host context for the item being edited (id, other field values, etc.):
        const dataItem = (this as any)._settings.dataItem.data;
        // e.g. dataItem.Id, dataItem.Title
    }

    onChange(newValue: string): void {
        // Write back through the same `value` property to persist.
        (this as any).value = newValue;
    }
}
```

- `extends FieldBase` gives you the two-way-bound `value` property the host reads back when saving. Access it as `(this as any).value` - the SDK types it loosely.
- Item-level context (the id, sibling field values, the type) is on `(this as any)._settings.dataItem.data`.
- `standalone: false` is required - these are NgModule-declared, not standalone components.
- Inject `HttpClient` for backend calls (see `HTTP_PREFIX` below).

### Readonly component (display mode)

Same shape as the write component (`extends FieldBase implements OnInit`, `standalone: false`, reads `(this as any).value`) but renders a display-only view - no inputs, no write-back. This is what shows when the item is viewed rather than edited.

### Settings class

```typescript
// event-date.settings.ts
import { SettingsBase } from "@progress/sitefinity-adminapp-sdk/app/api/v1";

export class EventDateSettings extends SettingsBase {
    init(metadata: any): void {
        super.init(metadata);   // always call super first
        // Read/normalize field metadata here; expose derived settings to the components.
    }
}
```

`init(metadata)` must call `super.init(metadata)` before doing its own work; it is where you read the field's configured metadata and shape the settings your write/readonly components consume.

## Backend OData calls with HTTP_PREFIX

To call a Sitefinity backend service from a field editor with the admin user's auth injected automatically, **prefix the URL with `HTTP_PREFIX`**:

```typescript
import { HttpClient } from "@angular/common/http";
import { HTTP_PREFIX } from "@progress/sitefinity-adminapp-sdk/app/api/v1";

loadTitle(id: string): void {
    const apiUrl = `${HTTP_PREFIX}/sf/system/newsitems(${id})?$select=Title`;
    this.http.get(apiUrl).subscribe(result => { /* ... */ });
}
```

- **Backend OData** lives at `{domain}/sf/system/...` and runs as the **authenticated backend user**. Prefixing with `HTTP_PREFIX` lets the AdminApp inject the backend auth token/headers for you.
- **Frontend services** live at `{domain}/api/{service}` - a different surface, typically anonymous or frontend-auth. This is the public frontend default OData service (e.g. `/api/default`); it is a separate endpoint from backend OData and does **not** take `HTTP_PREFIX`. For querying content through it, see `sitefinity-odata-services`.
- **Gotcha:** a plain `HttpClient` call **without** `HTTP_PREFIX` bypasses the AdminApp's auth injection and will come back 401/403 against the backend OData endpoints. Always prefix backend calls.

## Dev workflow

**Dev server (live-reload against a running site):**

```powershell
ng serve --port 3000
```

For the running Sitefinity instance to accept modules served from `localhost:3000`, two host-side settings are required (both under Administration => Settings => Advanced):

1. **CORS** - add the dev origin to `AccessControlAllowOrigin` so the browser may load your dev-served modules cross-origin.
2. **OAuth authorized client** - add an `AuthorizedClients` entry with `ClientId = sitefinity` and redirect URI `http://localhost:3000/auth/oauth/sign-in`, so the admin login flow can round-trip to your dev origin.

**Production build & deploy:**

```powershell
ng build -c prod
```

- Minification is supported by Sitefinity **11.1+** - a prod (minified) bundle loads fine on any supported host.
- Copy the `dist/` output (`my-project.extensions.bundle.js`) into the web app's `AdminApp/` folder and **restart the application**.

## Component framework quick reference

Reusable Angular UI from `@progress/sitefinity-component-framework` (v12) - use these so your editor visually matches the native AdminApp:

| Component | Module | Purpose |
|---|---|---|
| `sf-input` | `SfInputModule` | text/number input with label, hint, validation |
| `sf-search` | `SfSearchModule` | debounced search box, clearable |
| `sf-select` | `SfChoicesModule` | single-select dropdown (`choices` + `settings`) |
| `sf-checkboxes` | `SfChoicesModule` | multi-select checkboxes |
| `sf-radio-group` | `SfChoicesModule` | radio group |
| `sf-chip` | `SfChipModule` | removable/editable chip/pill |
| `sf-button` | `SfButtonModule` | button (`look`: large/small/action/delete) |
| `sf-switch` | `SfSwitchModule` | on/off toggle |
| `sf-loader` | `SfLoaderModule` | skeleton / progressbar / solid-block loader |
| `sf-field-wrapper` | `SfFieldWrapperModule` | label + validation chrome around a field |
| `sf-list-builder` | `SfListBuilderModule` | dynamic add/edit/remove list |
| `sf-tabs` | `SfTabsModule` | tab container |
| `sf-notification` | `SfNotificationModule` | inline alert/notification |
| `sf-icon` | `SfIconModule` | Font Awesome icon (name without `fa-`) |
| `sf-badge` | `SfBadgeModule` | badge with icon/background |
| `sf-tooltip` | `SfTooltipModule` | tooltip (caption + description) |

**Gotcha:** there is **no** autocomplete / combobox / typeahead component in the framework. When you need type-to-filter selection, build it yourself from `sf-search` + a results list (that is exactly what a custom field editor is for).

## Verify it works

1. **Bundle present:** `my-project.extensions.bundle.js` exists in the web app's `AdminApp/` folder.
2. **Restart** the application (recycle the app pool) so the admin shell re-scans `AdminApp/`.
3. **Hard-refresh** the admin (Ctrl+F5) to bust the browser cache of the old bundle.
4. **Registration ran:** open the browser console and confirm your `FIELD_DATA:` / registration logs appear - if the console is silent, the bundle was not discovered (recheck the `.extensions.bundle.js` filename and the `AdminApp/` location).
5. **Editor renders:** open a content item of the matching `typeName`; the target field should show your custom write component instead of the default input, and viewing the item should show your readonly component.

---
name: sitefinity-react-vite8-guide
description: Setup and conventions for adding a React 19 + Vite 8 (Rolldown) + Tailwind CSS v4 + shadcn/ui frontend to a classic Sitefinity MVC site (.NET Framework 4.8, MvcControllerProxy widgets - NOT the ASP.NET Core Renderer) - data-island widgets, per-widget code splitting, the React root lifecycle inside Sitefinity's page editor (mount, remount, teardown), search indexing, the Sentry error boundary bridge, HMR dev server, and the bun/oxlint/oxfmt toolchain. Use when building or troubleshooting React widgets on classic Sitefinity MVC, or when migrating widgets from the Vue 3 guide.
---

# React 19 + Vite 8 + Tailwind CSS v4 on classic Sitefinity MVC

> **Stack:** Sitefinity 15+ (classic ASP.NET MVC, .NET Framework 4.8) | React 19 | Vite 8 (Rolldown) | Tailwind CSS v4 | shadcn/ui (Base UI primitives) | TypeScript | bun | oxlint + oxfmt
>
> **Scope: the classic MVC renderer only** (`MvcControllerProxy` widgets, Razor views, `Html.Script`). This is NOT for the Sitefinity ASP.NET Core Renderer ("Sitefinity Core", `Progress.Sitefinity.AspNetCore`), which is a separate .NET web app with its own widget model and asset pipeline. If the solution has a second web project referencing `Progress.Sitefinity.Renderer`, or pages carry a non-NULL `renderer` column, stop - none of this applies.
>
> **Relationship to `sitefinity-vue3-vite8-guide`:** the server side is identical - the base controller (`GetModel()` -> `ConfigJson` + `IndexContent`), the `WidgetModel`, `Util.IsIndexingMode` / `Util.IsDev` / `Util.FileHash`, the dev impersonation controller, IIS MIME/CORS notes, and the cshtml-reload / CORS Vite plugins are all written out there and are not repeated here. This guide covers what changes when the client framework is React, and it is grounded in a production site that migrated its whole theme from Vue 3 to React 19 on this exact stack (Sitefinity 15.4, Vite 8, React 19).

---

## Table of Contents

1. [What changes vs Vue](#1-what-changes-vs-vue)
2. [Folder structure](#2-folder-structure)
3. [Toolchain: package.json, tsconfig, oxlint, oxfmt](#3-toolchain)
4. [vite.react.config.ts](#4-vitereactconfigts)
5. [Loading React on pages (React.cshtml)](#5-loading-react-on-pages)
6. [The runtime: widget map, scanning, design-mode watcher](#6-the-runtime)
7. [The widget registry: React roots inside Sitefinity's editor](#7-the-widget-registry)
8. [mountWidget: the Sentry error boundary bridge](#8-mountwidget)
9. [A complete widget: controller, view, index.tsx, App](#9-a-complete-widget)
10. [Template selection without ViewSelector](#10-template-selection)
11. [Styling rules that matter inside a CMS](#11-styling-rules)
12. [Tests](#12-tests)
13. [Running Vue and React side by side during a migration](#13-running-vue-and-react-side-by-side)
14. [Troubleshooting](#14-troubleshooting)

---

## 1. What changes vs Vue

The data-island architecture is the same: the MVC controller serializes widget configuration to JSON, the Razor view emits a mount element plus a `<template class="mp-widget-config">` island, and a runtime ES module scans the DOM and lazy-loads one chunk per widget type. What React changes:

| Concern | Vue 3 | React 19 |
|---|---|---|
| Root lifecycle | `createApp().mount(el)`; a detached app is garbage | `createRoot()` **owns its container**; a root whose host Sitefinity removed or rewrote must be `unmount()`ed or it leaks and can double-render. The registry tracks every root |
| Mount target | The host element | A **dedicated child container** (`createMountContainer(el)`), never the host - Sitefinity rewrites host `innerHTML` on property save, and unmounting a root whose children were destroyed externally throws an uncatchable async `NotFoundError` |
| Error capture | `app.config.errorHandler` | `Sentry.ErrorBoundary` around every root (render/lifecycle errors) + `@sentry/react` `GlobalHandlers` (event handlers, async) |
| Dev server | Vite client + entry | Vite client + entry **plus the React Fast Refresh preamble** injected before any module loads |
| Templates | `<component :is>` | A `templates` map of components keyed by the `templateName` string from the config |
| Cross-root state | Provide/inject or Pinia | Context cannot cross root boundaries; shared state between independently mounted apps uses module-level external stores (`useSyncExternalStore`) |
| Lint/format | ESLint/Prettier | oxlint + oxfmt (no ESLint, no Prettier) |

---

## 2. Folder structure

```
YourSite/ResourcePackages/MyTheme/
├── package.json                    # bun scripts (build, dev, lint, typecheck, test)
├── bun.lock
├── tsconfig.json
├── tsconfig.react.json             # extends tsconfig.json, scopes to assets/src/react
├── vite.react.config.ts            # the React build (name is historical if you also had a Vue config)
├── .oxlintrc.json
├── .oxfmtrc.json
├── components.json                 # shadcn/ui config
├── vite-dev.crt / vite-dev.key     # self-signed cert for the HTTPS dev server (gitignored)
├── tests/                          # node:test files (*.test.ts)
├── assets/
│   ├── dist/react/                 # build output (gitignored): react-runtime-[hash].js, chunks/, react-runtime.css, react-runtime.manifest
│   └── src/
│       ├── shared/styles/globals.css        # Tailwind v4 theme - single source of truth
│       └── react/
│           ├── lib/
│           │   ├── runtime.tsx              # Vite entry: widget map + scanner + design-mode watcher
│           │   ├── widget-registry.ts       # registerWidget / claimHost / createMountContainer / trackRoot / sweepDisconnectedRoots
│           │   ├── sentry-react.tsx         # mountWidget + Sentry init (errors only)
│           │   ├── env.ts                   # IS_DEV (hostname-based)
│           │   └── components/ui/           # shadcn/ui components (own chunk)
│           └── widgets/
│               └── Mvc/Views/
│                   ├── shared/              # cross-widget pieces (pager, data table)
│                   └── Faq/
│                       ├── index.tsx        # mount contract (the only file that touches the DOM)
│                       ├── FaqApp.tsx       # root component ({WidgetName}App - never App.tsx)
│                       ├── types.ts         # config contract mirrored from the controller
│                       └── templates/       # FaqSimple.tsx, FaqTwoColumn.tsx, ...
└── MVC/Views/Layouts/MyTheme.MVC.cshtml     # includes ~/Mvc/Views/Shared/React.cshtml

YourSite/Mvc/Views/Shared/React.cshtml       # the loader partial (section 5)
YourSite/Mvc/Views/Faq/Faq.Simple.cshtml     # data-island view (section 9)
```

Naming rules that pay off immediately:
- Root component files are `{WidgetName}App.tsx` and the function is `export function FaqApp(...)` - React DevTools and editor tabs stay distinguishable. Never `App.tsx` / `function App()`.
- Every widget has a `types.ts` that mirrors the controller's anonymous config object (camelCase keys). It is the contract between C# and TSX.

---

## 3. Toolchain

### package.json (scripts)

```json
{
  "scripts": {
    "build": "vite build --config vite.react.config.ts",
    "build:watch": "vite build --config vite.react.config.ts --watch",
    "dev": "concurrently -n hmr,build -c magenta,green \"vite --config vite.react.config.ts\" \"vite build --config vite.react.config.ts --watch\"",
    "dev:hmr": "vite --config vite.react.config.ts",
    "lint:react": "oxlint --no-ignore assets/src/react tests",
    "lint:react:fix": "oxlint --no-ignore --fix assets/src/react tests",
    "format:react": "oxfmt assets/src/react tests",
    "format:react:check": "oxfmt --check assets/src/react tests",
    "typecheck:react": "tsc --noEmit -p tsconfig.react.json",
    "test:react": "node --test tests/*.test.ts",
    "add:react": "shadcn add"
  }
}
```

`dev` runs the HMR server **and** a watch build at the same time: the HMR server serves pages you browse on the dev site, while the watch build keeps `assets/dist/react` current so the page editor and preview (which never use the dev server, see section 5) render the latest code.

Dependencies that carry weight: `react`, `react-dom`, `@base-ui/react` (shadcn's headless primitives), `tailwindcss` + `@tailwindcss/vite`, `class-variance-authority`, `clsx`, `tailwind-merge`, `tw-animate-css`, `lucide-react`, `axios`, `zod`, `zustand`, `@sentry/react`, `@tanstack/react-table`, `react-hook-form` + `@hookform/resolvers`. Dev: `vite`, `@vitejs/plugin-react`, `typescript`, `@types/react`, `@types/react-dom`, `oxlint`, `oxfmt`, `shadcn`, `concurrently`.

**Package manager is bun** (`bun.lock` is authoritative; no `package-lock.json`). Everything below works with npm too, but do not keep two lockfiles.

### tsconfig.react.json

```json
{
  "extends": "./tsconfig.json",
  "include": ["assets/src/react/**/*.ts", "assets/src/react/**/*.tsx"],
  "exclude": ["node_modules", "assets/dist"]
}
```

The base `tsconfig.json` needs `"jsx": "react-jsx"`, `"moduleResolution": "bundler"`, and the `@r/*` path alias matching the Vite alias (section 4).

### .oxlintrc.json (the parts that matter)

```json
{
  "$schema": "./node_modules/oxlint/configuration_schema.json",
  "plugins": ["react", "typescript"],
  "ignorePatterns": ["assets/dist/**", "node_modules/**", "MVC/Views/**", "**/*.min.*"],
  "rules": {
    "curly": ["error", "all"],
    "react/rules-of-hooks": "error",
    "react/exhaustive-deps": "error",
    "typescript/no-unused-vars": ["error", { "argsIgnorePattern": "^_", "varsIgnorePattern": "^_" }]
  }
}
```

Gotcha: rule keys use the `react/` namespace (`react/exhaustive-deps`) even though diagnostics print as `react-hooks(exhaustive-deps)`. That mismatch is expected. `// eslint-disable-next-line <rule>` comments still work - oxlint honours the ESLint directive syntax.

### .oxfmtrc.json

```json
{
  "$schema": "./node_modules/oxfmt/configuration_schema.json",
  "semi": false,
  "singleQuote": true,
  "printWidth": 100,
  "ignorePatterns": ["assets/dist/**", "node_modules/**", "MVC/Views/**", "**/*.min.*"],
  "sortTailwindcss": { "stylesheet": "./assets/src/shared/styles/globals.css", "functions": ["cn", "clsx", "cva"] }
}
```

---

## 4. vite.react.config.ts

The full config, annotated. The `devCorsPlugin` and `cshtmlReloadPlugin` are the ones from the Vue guide (section 5 there) - copy them verbatim.

```typescript
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'
import { readFileSync, existsSync } from 'fs'

// Vite 8's native config loader cannot provide __dirname - use import.meta.dirname.
const certPath = resolve(import.meta.dirname, 'vite-dev.crt')
const keyPath = resolve(import.meta.dirname, 'vite-dev.key')
const devHttps = existsSync(certPath) && existsSync(keyPath)
  ? { cert: readFileSync(certPath), key: readFileSync(keyPath) }
  : undefined

// Lazy chunks import the runtime entry back by FILENAME (no query string), so a ?v= cache buster
// cannot version those internal imports. Emit a manifest with the hashed entry name so the Razor
// loader can reference the exact same URL the chunks do - otherwise a second runtime instance loads.
function reactEntryManifestPlugin() {
  return {
    name: 'react-entry-manifest',
    generateBundle(_options: unknown, bundle: Record<string, any>) {
      const entry = Object.values(bundle).find(
        (o) => o.type === 'chunk' && o.isEntry && o.name === 'react-runtime',
      )
      if (!entry) { throw new Error('Unable to find the react-runtime build entry.') }
      this.emitFile({ type: 'asset', fileName: 'react-runtime.manifest', source: entry.fileName })
    },
  }
}

export default defineConfig(({ mode }) => ({
  appType: 'custom',
  // Production base path: where the chunks live, so modulepreload hints resolve.
  base: mode === 'development' ? '/' : '/ResourcePackages/MyTheme/assets/dist/react/',
  plugins: [
    react(),
    tailwindcss(),
    reactEntryManifestPlugin(),
    mode === 'development' && devCorsPlugin(),
    mode === 'development' && cshtmlReloadPlugin(),
  ],
  resolve: {
    // Keep '@' for a coexisting Vue build if you have one; React gets its own alias.
    alias: { '@r': resolve(import.meta.dirname, 'assets/src/react/lib') },
  },
  define: {
    'process.env.NODE_ENV': JSON.stringify(mode === 'development' ? 'development' : 'production'),
  },
  css: { postcss: {} }, // isolate from any parent postcss.config.js - Tailwind v4 uses its Vite plugin
  server: {
    host: '0.0.0.0',
    port: 5174,
    strictPort: true,
    // Pages consume this server cross-origin: give Vite its public origin so CSS asset URLs
    // (font files, etc.) resolve back to the dev server, not to the Sitefinity document origin.
    origin: 'https://localhost:5174',
    https: devHttps,
    hmr: { protocol: 'wss', host: 'localhost', port: 5174 },
  },
  build: {
    // @tailwindcss/vite registers scan roots with the watcher; without this exclusion the build's
    // own dist writes retrigger `vite build --watch` forever.
    watch: process.argv.includes('--watch')
      ? { exclude: ['**/assets/dist/**', '**/node_modules/**'] }
      : null,
    rolldownOptions: {
      input: { 'react-runtime': resolve(import.meta.dirname, 'assets/src/react/lib/runtime.tsx') },
      output: {
        entryFileNames: '[name]-[hash].js',
        chunkFileNames: 'chunks/[name]-[hash].js',
        assetFileNames: 'react-runtime.[ext]',
        codeSplitting: {
          groups: [
            // React core + Base UI primitives. The trailing [\\/] anchors each name to a full path
            // segment so scoped lookalikes (@sentry/react, lucide-react) cannot leak in.
            { name: 'vendor-react', test: /node_modules[\\/](react|react-dom|scheduler|@base-ui)[\\/]/, priority: 20 },
            { name: 'shadcn-ui', test: /[\\/]components[\\/]ui[\\/]/, priority: 15 },
            // Shared-by-2+ code, but NEVER a widget's own folder - otherwise anything that imports
            // several widgets (a design browser, a storybook page) drags all widget code into
            // 'common' and destroys per-widget lazy delivery.
            {
              name: 'common',
              test: (id: string) =>
                !/[\\/]widgets[\\/]/.test(id) || /[\\/]widgets[\\/]Mvc[\\/]Views[\\/]shared[\\/]/.test(id),
              minShareCount: 2,
              minSize: 10000,
              priority: 5,
            },
          ],
        },
      },
    } as any, // Vite's types omit 'input' from rolldownOptions; Rolldown accepts it at runtime
    outDir: './assets/dist/react',
    emptyOutDir: true,
    cssCodeSplit: false, // one react-runtime.css
  },
}))
```

Why the entry is content-hashed while the Vue guide used a stable name plus `?v=`: React chunks import helpers from the entry, and they do so by filename. If the page loads `react-runtime.js?v=abc` and a chunk imports `react-runtime.js`, the browser treats those as two module identities and you get two runtimes. Hashing the entry and reading the name from `react-runtime.manifest` keeps one identity.

---

## 5. Loading React on pages

`Mvc/Views/Shared/React.cshtml`, included once at the end of the layout body (after `@Html.Section("scripts")`):

```razor
@using YourApp.Controls.Code;
@using Telerik.Sitefinity.Frontend.Mvc.Helpers;
@using Telerik.Sitefinity.Services;

@{
    // Emit once per request even if the layout AND widgets include this partial.
    // Own Items key so a coexisting Vue3.cshtml loads exactly once too.
    var reactKey = "__react_partial_rendered";
    if (HttpContext.Current.Items.Contains(reactKey)) { return; }
    HttpContext.Current.Items[reactKey] = true;

    var forceProd = HttpContext.Current.Request.QueryString["vite"] == "prod"
        || (HttpContext.Current.Session?["vite_prod"] as string == "1");

    // The page editor and preview NEVER use the dev server: Sitefinity's preview runs with
    // IsDesignMode == true, so one check covers both. Design mode always gets the prod bundle.
    var useVite = Util.IsDev && !SystemManager.IsDesignMode && !forceProd;
}

@if (useVite)
{
    @* React Fast Refresh preamble - @vitejs/plugin-react requires it BEFORE any module loads. *@
    <script type="module">
        import RefreshRuntime from 'https://localhost:5174/@("@")react-refresh'
        RefreshRuntime.injectIntoGlobalHook(window)
        window.$RefreshReg$ = () => { }
        window.$RefreshSig$ = () => (type) => type
        window.__vite_plugin_react_preamble_installed__ = true
    </script>
    <script type="module" crossorigin src="https://localhost:5174/@("@")vite/client"></script>
    <script type="module" crossorigin src="https://localhost:5174/assets/src/react/lib/runtime.tsx"></script>
}
else
{
    var assetRoot = "/ResourcePackages/MyTheme/assets/dist/react/";
    var manifestPath = Server.MapPath(assetRoot + "react-runtime.manifest");
    var hasManifest = System.IO.File.Exists(manifestPath);
    var jsFileName = hasManifest ? System.IO.File.ReadAllText(manifestPath).Trim() : "react-runtime.js";
    var jsPath = assetRoot + jsFileName;
    var cssPath = assetRoot + "react-runtime.css";

    @* The hashed entry URL must NOT get a query string - lazy chunks import this exact module URL. *@
    <script type="module" src="@(hasManifest ? jsPath : jsPath + "?v=" + Util.FileHash(jsPath))"></script>
    @Html.StyleSheet(cssPath + "?v=" + Util.FileHash(cssPath), "head")
}
```

`@("@")` is how you emit a literal `@` inside Razor.

---

## 6. The runtime

`assets/src/react/lib/runtime.tsx` - the single Vite entry:

```tsx
import '../../shared/styles/globals.css'
import { mountAllWidgets, sweepDisconnectedRoots } from './widget-registry'
import { captureException } from './sentry-react'

// One CSS selector -> one dynamic import. Vite splits each import into its own chunk, loaded only
// when the selector is present in the DOM.
const widgetMap = {
  '[data-widget="faq"]': () => import('../widgets/Mvc/Views/Faq'),
  '[data-widget="hero"]': () => import('../widgets/Mvc/Views/Hero'),
} satisfies Record<string, () => Promise<object>>

const loadedSelectors = new Set<string>()

function scanAndMount() {
  for (const [selector, loader] of Object.entries(widgetMap)) {
    if (!loadedSelectors.has(selector) && document.querySelector(selector)) {
      loadedSelectors.add(selector)
      // The widget's index.tsx calls registerWidget(mount) on execution, which mounts immediately.
      loader().catch((err) => {
        loadedSelectors.delete(selector) // let the next scan retry
        console.error(`[React] Failed to load chunk for ${selector}:`, err)
        // A caught rejection never reaches window.onunhandledrejection - report it explicitly. A
        // chunk that 404s after a bad deploy is a whole widget that silently never renders.
        captureException(err, { context: 'react-runtime:chunk-load', selector })
      })
    }
  }
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', scanAndMount)
} else {
  scanAndMount()
}

// Design mode: Sitefinity injects widgets via XHR (drop, property save, template switch).
// ORDER MATTERS:
//  1. sweepDisconnectedRoots() - unmount roots whose host left the DOM (or they leak / double-render)
//  2. scanAndMount()           - load chunks for widget types that are NEW on the page
//  3. mountAllWidgets()        - give new instances of already-loaded types their own root
function initDesignModeWatcher() {
  if (!document.querySelector('.sfPageEditor')) { return }
  let scanPending = false
  const observer = new MutationObserver(() => {
    if (scanPending) { return }
    scanPending = true
    requestAnimationFrame(() => {
      scanPending = false
      sweepDisconnectedRoots()
      scanAndMount()
      mountAllWidgets()
    })
  })
  observer.observe(document.body, { childList: true, subtree: true })
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', initDesignModeWatcher)
} else {
  initDesignModeWatcher()
}
```

Two differences from the Vue runtime: `sweepDisconnectedRoots()` runs first on every design-mode mutation, and chunk-load failures are reported to Sentry explicitly. The `satisfies` on `widgetMap` keeps the selectors as literal keys while still type-checking the loaders.

---

## 7. The widget registry

`assets/src/react/lib/widget-registry.ts`. This is the part that does not exist in the Vue guide and the part that goes wrong first.

```typescript
import type { Root } from 'react-dom/client'

const mountFunctions: (() => void)[] = []
const rootsByHost = new WeakMap<HTMLElement, Root>()   // O(1) guard + lookup; GC-friendly
const mountedHosts = new Set<HTMLElement>()            // iterable index for the sweep
const MOUNTED_ATTR = 'reactMounted'                    // el.dataset.reactMounted === 'true'
const CONFIG_ISLAND_SELECTOR = 'template.mp-widget-config, script.mp-widget-config'

/** React must NOT own the host element: Sitefinity rewrites host innerHTML on property save, and
 *  unmounting a root whose container children were destroyed externally throws an async,
 *  uncatchable NotFoundError. A wrapper div gets detached intact, so unmount() stays silent. */
export function createMountContainer(el: HTMLElement): HTMLElement {
  const container = document.createElement('div')
  container.dataset.reactRoot = ''
  el.appendChild(container)
  return container
}

function releaseHost(el: HTMLElement): void {
  const root = rootsByHost.get(el)
  if (root) {
    try { root.unmount() } catch (e) { console.error('[React] Failed to unmount widget root:', e) }
  }
  rootsByHost.delete(el)
  mountedHosts.delete(el)
  el.querySelectorAll(':scope > [data-react-root]').forEach((n) => { n.remove() })
  // Clear the guard: Sitefinity can detach and later re-insert the SAME node (drag/reorder);
  // a stale 'true' would make claimHost refuse to remount and the widget would stay dead.
  delete el.dataset[MOUNTED_ATTR]
}

/** Should this widget's mount function mount onto this element?
 *   fresh element                       -> true
 *   already mounted, untouched          -> false (double-mount guard)
 *   already mounted BUT the config island is present again -> Sitefinity re-injected server
 *     markup INTO the same element (in-place property save). The first render consumed the
 *     island, so its reappearance is an exact "fresh server content" signal: unmount the stale
 *     root and let the caller remount against the new config -> true */
export function claimHost(el: HTMLElement): boolean {
  const mounted = el.dataset[MOUNTED_ATTR] === 'true' || rootsByHost.has(el)
  if (!mounted) { return true }
  if (el.querySelector(CONFIG_ISLAND_SELECTOR)) {
    releaseHost(el)
    return true
  }
  return false
}

export function trackRoot(el: HTMLElement, root: Root): void {
  el.dataset[MOUNTED_ATTR] = 'true'
  rootsByHost.set(el, root)
  mountedHosts.add(el)
}

/** Unmount and forget any root whose host has left the DOM. Called by the design-mode watcher
 *  before each re-scan so XHR markup replacement never leaves detached roots behind. */
export function sweepDisconnectedRoots(): void {
  for (const el of mountedHosts) {
    if (!el.isConnected) { releaseHost(el) }
  }
}

export function registerWidget(mountFn: () => void) {
  mountFunctions.push(mountFn)
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', mountFn)
  } else {
    mountFn()
  }
}

export function mountAllWidgets() {
  mountFunctions.forEach((fn) => fn())
}
```

**The caller contract** (every widget's `index.tsx` follows it exactly, see section 9):

1. Read the config island's JSON string **before** `claimHost` (the claim may tear down DOM).
2. After a successful claim, **remove the island element** - its presence is the remount signal, so leaving it in place makes every later scan remount.
3. Mount into `createMountContainer(el)`, never into `el`, and register via `trackRoot(el, root)` keyed on the **host** element.

Static shell regions (a layout's sidebar/header that Sitefinity never XHR-replaces and renders empty) are the one exception: mount directly into the host so its flex classes apply to the React children, but still `claimHost`/`trackRoot` for bookkeeping. Two such regions are two React roots; share state between them with a module-level store and `useSyncExternalStore`, because context cannot cross roots.

---

## 8. mountWidget

`assets/src/react/lib/sentry-react.tsx` exposes one function every widget uses:

```tsx
import { createRoot, type Root } from 'react-dom/client'
import type { ReactNode } from 'react'
import * as Sentry from '@sentry/react'

// Sentry.init runs once at module evaluation against a server-rendered window config
// (emitted by a Razor partial from your Sitefinity settings). Errors only: no Replay, no
// BrowserTracing, no Performance - those integrations are never imported, so they tree-shake out.
// If the config global is missing, every call here is a silent no-op.

export function mountWidget(el: HTMLElement, node: ReactNode): Root {
  const root = createRoot(el)
  root.render(
    <Sentry.ErrorBoundary
      fallback={null}
      beforeCapture={(scope) => { scope.setTag('source.framework', 'react') }}
    >
      {node}
    </Sentry.ErrorBoundary>,
  )
  return root
}

export function captureException(err: unknown, context?: Record<string, string | number | boolean | null | undefined>) {
  Sentry.withScope((scope) => {
    scope.setTag('source.framework', 'react')
    if (context) { scope.setContext('widget', context) }
    Sentry.captureException(err)
  })
}
```

Coverage rules: the `ErrorBoundary` catches errors thrown during render, lifecycle and commit and attaches the React component stack. Errors in DOM event handlers (`onClick`) and async callbacks are **not** caught by any React error boundary - `@sentry/react`'s default `GlobalHandlers` integration (`window.onerror` / `unhandledrejection`) picks those up. Imperative code outside React (the chunk loader) calls `captureException`. Never add your own top-level `ErrorBoundary` in widget code; change `mountWidget` so every widget gets the same handling.

---

## 9. A complete widget

### Controller (identical to the Vue guide's pattern)

Reuse the base class from the Vue guide (there it is called `Vue3Controller`; the name is irrelevant - it is a data-island base with `GetModel()` -> `WidgetModel { ConfigJson, IndexContent }`, `Util.IsIndexingMode` short-circuit, and `ICustomWidgetVisualization`).

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Telerik.Sitefinity.Mvc;
using Telerik.Sitefinity.Frontend.Mvc.Infrastructure.Controllers.Attributes;
using Telerik.Sitefinity.Modules.Pages.PropertyPersisters;
using Progress.Sitefinity.Renderer.Designers;
using Progress.Sitefinity.Renderer.Designers.Attributes;

public enum FaqTemplate { [Description("Simple")] Simple, [Description("Two Column")] TwoColumn, [Description("Grouped")] Grouped }

public class FaqItem
{
    [DisplayName("Question")]
    public string Question { get; set; }

    [ContentSection(1)]
    [DisplayName("Answer")]
    [DataType(customDataType: KnownFieldTypes.Html)]
    public string Answer { get; set; }
}

[EnhanceViewEnginesAttribute]
[ControllerToolboxItem(Name = "Faq_MVC", Title = "FAQ", SectionName = "Design", CssClass = "sfForumsViewIcn sfMvcIcn")]
public class FaqController : WidgetIslandController
{
    [Browsable(false)]
    public override bool IsEmpty => this.Faqs == null || this.Faqs.Count == 0;

    protected override WidgetModel GetModel()
    {
        var faqs = this.Faqs ?? new List<FaqItem>();

        var indexHtml = new StringBuilder();                       // what the search indexer sees
        if (!string.IsNullOrEmpty(this.Heading)) { indexHtml.AppendFormat("<h2>{0}</h2>", this.Heading); }
        foreach (var faq in faqs) { indexHtml.AppendFormat("<h3>{0}</h3>{1}", faq.Question, faq.Answer); }

        return new WidgetModel
        {
            ConfigJson = ServiceStack.Text.JsonSerializer.SerializeToString(new
            {
                templateName = this.Template.ToString(),
                heading = this.Heading,
                subheading = this.Subheading,
                faqs = faqs.Select(f => new { question = f.Question, answer = f.Answer }).ToArray()
            }),
            IndexContent = indexHtml.ToString()
        };
    }

    [DisplayName("Template")]
    [DefaultValue(FaqTemplate.Simple)]
    public FaqTemplate Template { get; set; } = FaqTemplate.Simple;

    [Browsable(false)]
    public override string TemplateName => $"Faq.{this.Template}";

    [Category("Content")] [DisplayName("Heading")] public string Heading { get; set; }
    [Category("Content")] [DisplayName("Subheading")] public string Subheading { get; set; }

    [Category("FAQs")]
    [DisplayName("FAQ Items")]
    [TableView(Reorderable = true, ColumnCount = 1)]
    [PropertyPersistence(PersistAsJson = true)]                   // REQUIRED for IList<T> of your own classes
    public IList<FaqItem> Faqs { get; set; } = new List<FaqItem>();
}
```

`ServiceStack.Text.JsonSerializer` is what Sitefinity already ships; the anonymous object gives camelCase keys by construction. The `[PropertyPersistence(PersistAsJson = true)]` on the list is not optional - see `sitefinity-widget-expert` for the persistence rules.

### View (`Mvc/Views/Faq/Faq.Simple.cshtml` - one per template, all identical)

```razor
@model YourApp.Controls.Mvc.Models.WidgetModel

<div data-widget="faq">
    <template class="mp-widget-config">@Html.Raw(Model.ConfigJson)</template>
</div>
```

A `<template>` keeps the JSON inert (not rendered, not parsed as HTML). Nothing else goes in the view: the React app owns everything inside the host.

### index.tsx (the mount contract)

```tsx
import { mountWidget } from '@r/sentry-react'
import { registerWidget, claimHost, createMountContainer, trackRoot } from '@r/widget-registry'
import { FaqApp } from './FaqApp'
import type { FaqConfig } from './types'

function mount() {
  document.querySelectorAll<HTMLElement>('[data-widget="faq"]').forEach((el) => {
    // 1. capture the config string BEFORE claiming (the claim may tear down DOM)
    const configEl = el.querySelector('template.mp-widget-config, script.mp-widget-config')
    const rawConfig = configEl?.innerHTML || ''

    if (!claimHost(el)) { return }

    // 2. consume the island - its presence is claimHost's remount signal
    configEl?.remove()

    let config: FaqConfig = JSON.parse('{}')
    try {
      config = JSON.parse(rawConfig || '{}')
    } catch (e) {
      console.error('[Faq] Failed to parse widget config:', e)
    }

    // 3. dedicated container, never the host; track by host
    const root = mountWidget(createMountContainer(el), <FaqApp serverData={config} />)
    trackRoot(el, root)
  })
}

registerWidget(mount)
```

### types.ts

```typescript
export interface FaqItemConfig { question: string; answer: string }
export interface FaqConfig {
  templateName: 'Simple' | 'TwoColumn' | 'Grouped'
  heading: string
  subheading: string
  faqs: FaqItemConfig[]
}
```

### FaqApp.tsx

```tsx
import type { ComponentType } from 'react'
import type { FaqConfig } from './types'
import { FaqSimple } from './templates/FaqSimple'
import { FaqTwoColumn } from './templates/FaqTwoColumn'
import { FaqGrouped } from './templates/FaqGrouped'

const templates = {
  Simple: FaqSimple,
  TwoColumn: FaqTwoColumn,
  Grouped: FaqGrouped,
} satisfies Record<string, ComponentType<{ serverData: FaqConfig }>>

export function FaqApp({ serverData }: { serverData: FaqConfig }) {
  const Template = templates[serverData.templateName]
  return <div className="@container">{Template ? <Template serverData={serverData} /> : null}</div>
}
```

Then add `'[data-widget="faq"]': () => import('../widgets/Mvc/Views/Faq')` to `widgetMap` and rebuild.

---

## 10. Template selection

`[ViewSelector]` only discovers views under `ResourcePackages/{Theme}/MVC/Views/{Widget}/`; with views in `Mvc/Views/` its dropdown is empty. The React pattern sidesteps it: an **enum** property drives both the Razor view name (`TemplateName => $"Faq.{Template}"`) and the client template (`templates[serverData.templateName]`). Enum member names ARE the template keys on both sides - keep them identical and the compiler plus the `satisfies` map catch drift.

---

## 11. Styling rules

- **Container queries, not viewport queries.** A widget in a 300px sidebar still sees a 1200px viewport, so `md:` activates wide layouts and truncates content. Put `@container` on the widget root (the `FaqApp` wrapper above) and use `@sm:` / `@md:` / `@lg:` on children. Tailwind v4 supports `@container` natively.
- **Every widget supports dark mode** through semantic tokens (`text-foreground`, `bg-card`, `text-muted-foreground`); hardcoded colors get a `dark:` twin. Infrastructure: a `.dark` class on `<html>` and `@custom-variant dark (&:is(.dark *))` in `globals.css`.
- **A `designmode` variant** is useful: `@custom-variant designmode (&:is(.sfPageEditor *))` lets you adjust layout only inside the page editor.
- **Base UI orientation variants must be declared** or shadcn Tabs / ScrollArea silently lose their layout: `@custom-variant data-horizontal (&[data-orientation=horizontal]);` and `@custom-variant data-vertical (&[data-orientation=vertical]);` in `globals.css`.
- **Scan Razor for Tailwind classes** so classes used only in `.cshtml` survive purge: `@source "../../../../MVC/Views/**/*.cshtml";` (path relative to `globals.css`).
- **Scope preflight and utilities away from Sitefinity's admin app.** Tailwind's preflight reset and utilities leak into the AdminApp/page-editor chrome (`#adminAppWrapper`) and break it. Split the imports (`tailwindcss/theme.css` global; preflight and utilities wrapped in a donut selector that excludes the admin wrapper) - only theme variables stay global because custom properties paint nothing.
- Icons: `lucide-react` (bundled, tree-shaken). Font Awesome via CDN is typically not loaded on admin/design pages, so an icon font breaks inside the editor.
- HTTP: one client (`axios`) for every request, so V1 and V2 widgets share error handling and interceptors.
- Add `data-testid` attributes on structural elements (`{widget}-section`, `{widget}-heading`, `{widget}-item`), the same names across template variants, so end-to-end tests do not depend on which template is active.

---

## 12. Tests

Plain `node --test` over `tests/*.test.ts` (Node 22+ runs TypeScript directly). The valuable tests here are **contract tests over the source tree**, not component snapshots: assert that no widget imports `createRoot` directly (everything goes through `mountWidget`), that control primitives use surface-relative tokens, that every `index.tsx` removes its config island after claiming, that container-query specimens declare their own width. Read the files with `node:fs`, assert with `node:assert/strict`. They run in CI without a browser and catch the regressions that a `shadcn add` or a copy-pasted widget introduces.

---

## 13. Running Vue and React side by side

A migration runs both bundles for a while. Rules that keep that sane:

- Each loader partial (`Vue3.cshtml`, `React.cshtml`) has its own `HttpContext.Items` guard key, so both load exactly once.
- A widget selector lives in **exactly one** `widgetMap`. Move it from the Vue map to the React map in the same commit as the widget port - never claimed by both.
- Shared shell regions (sidebar/header) can be owned by only one bundle; move the eager import with the region.
- Both bundles import the same `globals.css`; keep one theme file.
- Sentry: both SDKs share one global carrier if pinned to the same core version. The first `init` wins; tag React events per event (`beforeCapture`, `withScope`) rather than relying on `initialScope`.
- Aliases: `@` for the Vue build, `@r` for React, so a stray import cannot resolve into the wrong tree.

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Widget renders twice after saving properties in the editor | Root not released when Sitefinity rewrote the host | Follow the caller contract: read island, `claimHost`, remove island, mount into `createMountContainer` |
| `NotFoundError: Failed to execute 'removeChild'` in the console during editing | Root mounted directly on the host element | Mount into the wrapper container, never the host |
| Widget dead after drag/reorder in the editor | Stale `data-react-mounted` on a re-inserted node | `releaseHost` must delete the dataset flag (the registry above does) |
| Two React runtimes / hooks error "Invalid hook call" | Entry loaded with a query string while chunks import it by filename | Use the manifest-driven hashed entry, no `?v=` on it |
| Fast Refresh error `@vitejs/plugin-react can't detect preamble` | Preamble script missing or after the entry | Emit the preamble `<script type="module">` before `@vite/client` |
| Fonts/CSS assets 404 on dev | Vite resolves CSS asset URLs against the page origin | Set `server.origin` to the dev server's public origin |
| `vite build --watch` rebuilds forever | Tailwind's scan roots include the dist folder | `build.watch.exclude: ['**/assets/dist/**', ...]` |
| Widget invisible to search | The indexer does not run JavaScript | Return `IndexContent` HTML when `Util.IsIndexingMode` (base controller) |
| Tabs render beside their panel | Missing `data-horizontal` / `data-vertical` custom variants | Declare both variants in `globals.css` |
| Page editor chrome broken after adding Tailwind | Preflight/utilities leaking into `#adminAppWrapper` | Donut-scope preflight and utilities; keep only theme variables global |

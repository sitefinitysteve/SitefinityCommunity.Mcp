---
name: sitefinity-vue3-vite8-guide
description: Complete setup guide for adding a Vue 3 + Vite 8 (Rolldown) + Tailwind CSS v4 + shadcn-vue frontend to a classic Sitefinity MVC site - data-island widgets, code splitting, design-mode mounting, search indexing, Sentry error bridge, HMR dev server, and IIS considerations. Use when building or troubleshooting this stack on Sitefinity.
---

# Vue 3 + Vite 8 + Tailwind CSS v4 on Sitefinity: A Complete Guide

> **Stack:** Sitefinity 15+ (ASP.NET MVC) | Vue 3.5 | Vite 8 (Rolldown) | Tailwind CSS v4 | shadcn-vue | TypeScript
>
> This guide walks you through adding a modern Vue 3 frontend to a Sitefinity CMS project with production code splitting, design-mode support, search indexing, and a dev impersonation controller. Every file is included — you can hand this to an LLM or a developer and get a working setup.

---

## Table of Contents

1. [What You'll Build](#1-what-youll-build)
2. [Prerequisites](#2-prerequisites)
3. [Folder Structure](#3-folder-structure)
4. [Theme Package Setup](#4-theme-package-setup)
5. [Vite 8 Configuration](#5-vite-8-configuration)
6. [Tailwind CSS v4 + shadcn-vue](#6-tailwind-css-v4--shadcn-vue)
7. [Widget Infrastructure](#7-widget-infrastructure)
8. [Loading Vue 3 on Pages](#8-loading-vue-3-on-pages)
9. [Your First Widget — FAQ](#9-your-first-widget--faq)
10. [Adding More Widgets](#10-adding-more-widgets)
11. [Dev Impersonation Controller](#11-dev-impersonation-controller)
12. [Build and Verify](#12-build-and-verify)
13. [Vite Dev Server with HMR](#13-vite-dev-server-with-hmr)
14. [Code Splitting Deep Dive](#14-code-splitting-deep-dive)
15. [IIS Considerations](#15-iis-considerations)
16. [Troubleshooting](#16-troubleshooting)

---

## 1. What You'll Build

A **data-island architecture** where Sitefinity MVC controllers serialize widget configuration as JSON into the Razor view, and Vue 3 picks it up client-side.

If you've used **Inertia.js**, this will feel familiar — the server provides data, the client renders the UI. The difference is that Inertia controls routing (one component per route, SPA-style navigation), while this setup works with a CMS that controls page composition. A single page can have multiple independent Vue apps (widgets), and the runtime discovers which ones to mount by scanning the DOM rather than matching routes. Think of it as "Inertia for CMS widgets" — same philosophy, adapted for a world where content editors drag-drop components onto pages.

```
┌─────────────────────────────────────────────────────────────┐
│  Sitefinity MVC Controller                                  │
│  ┌───────────────┐                                          │
│  │ GetModel()    │──► ConfigJson = { heading, items, ... }  │
│  │               │──► IndexContent = "<h2>...</h2>"         │
│  └───────────────┘                                          │
│           │                                                 │
│           ▼                                                 │
│  Razor View (.cshtml) — thin shell                          │
│  ┌─────────────────────────────────────────────────┐        │
│  │ <div data-widget="faq">                         │        │
│  │   <template class="widget-config">              │        │
│  │     {"heading":"FAQ","items":[...]}             │        │
│  │   </template>                                   │        │
│  │ </div>                                          │        │
│  └─────────────────────────────────────────────────┘        │
│           │                                                 │
│           ▼                                                 │
│  Vue 3 Runtime (ES module, code-split)                      │
│  ┌─────────────────────────────────────────────────┐        │
│  │ scanAndMount() finds [data-widget="faq"]        │        │
│  │   → dynamic import('./FaqWidget/index.ts')      │        │
│  │     → createApp(FaqApp, { serverData: config }) │        │
│  │       → mounts into the <div>                   │        │
│  └─────────────────────────────────────────────────┘        │
└─────────────────────────────────────────────────────────────┘
```

**Key wins:**
- **Code splitting** — only the widgets on a page are downloaded, not the entire app
- **Design mode** — widgets mount when drag-dropped in Sitefinity's page editor
- **Search indexing** — `IndexContent` gives Sitefinity's Lucene crawler plain HTML to index
- **Cache efficiency** — vendor chunk (Vue + Reka UI) stays cached across deploys; widget chunks bust independently
- **Rollback safety** — one-line toggle in `vite.config.ts` to disable splitting and inline everything
- **Hot Module Replacement (HMR)** — edit a `.vue` file and see it update in the browser instantly, without a full page refresh. This is transformative on a CMS like Sitefinity where a cold page load can take 5-15 seconds (and an app pool restart 30-90 seconds). With HMR, your feedback loop for Vue template and style changes drops to milliseconds.

---

## 2. Prerequisites

| Requirement | Version | Notes |
|-------------|---------|-------|
| Sitefinity CMS | 15.0+ | ASP.NET MVC (not .NET Core Renderer). The guide uses Feather + MVC conventions. |
| .NET Framework | 4.8 | Standard Sitefinity requirement |
| Node.js | 20+ | For Vite, Vue, Tailwind toolchain |
| npm | 10+ | Comes with Node.js 20 |
| C# build tool | Any | Visual Studio 2022+, VS Code with `msbuild`, or a PowerShell build script — whatever builds your `.sln` |

**NuGet packages required in the controls class library project:**

These must be referenced in the C# project where your widget controllers live (not just the web project). In a brand-new Sitefinity controls project, some of these may not be referenced yet.

| Package | Used For | Notes |
|---------|----------|-------|
| `Telerik.Sitefinity.Mvc` | MVC controller base, `[ControllerToolboxItem]` | Usually already referenced in a Sitefinity controls project |
| `Telerik.Sitefinity.Frontend` | `[EnhanceViewEnginesAttribute]`, Feather view resolution | Required for cross-project view lookup |
| `Progress.Sitefinity.Renderer` | `KnownFieldTypes`, `[TableView]`, `[ContentSection]`, `[Choice]` | **Newer package** — may need to be added. Powers the auto-generated widget designer UI. (Note: `[ColorPalette]` is NOT available here — it exists only in the ASP.NET Core renderer packages, verified absent from the MVC assemblies. Use `KnownFieldTypes.Color` for a color picker in MVC.) |
| `ServiceStack.Text` | `JsonSerializer.SerializeToString()` in widget controllers | Bundled with Sitefinity but may only be in the web project's bin. **Add a reference** in the controls project (DLL reference from the web project's `bin/` or `packages/` folder). |

---

## 3. Folder Structure

```
SitefinityWeb/                              ← Your Sitefinity web project
├── Mvc/
│   └── Views/
│       ├── Shared/
│       │   └── Vue3.cshtml                 ← Script/CSS loader partial
│       └── Faq/
│           └── Faq.Default.cshtml          ← Widget view (thin shell)
│
├── ResourcePackages/
│   └── MyTheme/                            ← Your theme (replace "MyTheme")
│       ├── package.json
│       ├── tsconfig.json
│       ├── components.json                 ← shadcn-vue config
│       ├── vite.config.ts
│       │
│       └── assets/
│           ├── src/
│           │   └── vue3/
│           │       ├── env.d.ts                ← Vue SFC type shim
│           │       ├── lib/
│           │       │   ├── runtime.ts          ← Entry point
│           │       │   ├── widget-registry.ts  ← Mount registry
│           │       │   ├── utils.ts            ← cn() utility
│           │       │   ├── components/
│           │       │   │   └── ui/             ← shadcn-vue components
│           │       │   └── styles/
│           │       │       └── globals.css     ← Tailwind + theme tokens
│           │       │
│           │       └── widgets/
│           │           └── Mvc/Views/
│           │               └── Faq/
│           │                   ├── index.ts        ← Widget entry
│           │                   ├── FaqApp.vue      ← Root component
│           │                   └── types.ts        ← TypeScript interfaces
│           │
│           └── dist/
│               └── vue3/                   ← Build output (gitignored)
│                   ├── vue3-runtime.js
│                   ├── vue3-runtime.css
│                   └── chunks/
│                       ├── vendor-vue-abc123.js
│                       ├── shadcn-ui-def456.js
│                       └── Faq-789abc.js
│
SitefinityControls/                         ← Your controls class library
├── Code/
│   └── Util.cs                             ← Helper utilities
├── Mvc/
│   ├── Controllers/
│   │   ├── Vue3Controller.cs               ← Base controller for Vue 3 widgets
│   │   ├── Faq/
│   │   │   └── FaqController.cs            ← Widget controller
│   │   └── DevLogin/
│   │       └── DevLoginController.cs       ← Dev impersonation
│   └── Models/
│       └── Vue3/
│           └── Vue3WidgetModel.cs          ← Shared model
```

---

## 4. Theme Package Setup

Create a `ResourcePackages/MyTheme/` folder in your Sitefinity web project (replace `MyTheme` with your theme name — this must match the active theme in Sitefinity's admin under Design > Set active theme).

### package.json

(`@sentry/vue` is optional but strongly recommended — see "Error monitoring" in Section 7. Drop it if you don't use Sentry.)

```json
{
  "name": "my-sitefinity-vue3",
  "private": true,
  "type": "module",
  "scripts": {
    "build": "vite build",
    "build:watch": "vite build --watch",
    "dev": "concurrently -n hmr,build -c cyan,green \"vite\" \"vite build --watch\"",
    "dev:hmr": "vite",
    "add": "npx shadcn-vue@latest add"
  },
  "dependencies": {
    "@sentry/vue": "^10.53",
    "@tailwindcss/typography": "^0.5.19",
    "@tailwindcss/vite": "^4.2.2",
    "@vueuse/core": "^14.2",
    "axios": "^1.7",
    "class-variance-authority": "^0.7",
    "clsx": "^2.1",
    "lucide-vue-next": "^1.0",
    "reka-ui": "^2.9",
    "tailwind-merge": "^3",
    "tailwindcss": "^4.2.2",
    "tw-animate-css": "^1.4",
    "vue": "^3.5"
  },
  "devDependencies": {
    "@vitejs/plugin-vue": "^6.0",
    "concurrently": "^9.2",
    "typescript": "^5.6",
    "vite": "^8.0"
  }
}
```

### tsconfig.json

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "jsx": "preserve",
    "resolveJsonModule": true,
    "isolatedModules": true,
    "esModuleInterop": true,
    "lib": ["ES2020", "DOM", "DOM.Iterable"],
    "paths": {
      "@/*": ["./assets/src/vue3/lib/*"]
    },
    "types": ["vite/client"]
  },
  "include": ["assets/src/vue3/**/*.ts", "assets/src/vue3/**/*.vue"],
  "exclude": ["node_modules", "assets/dist"]
}
```

### env.d.ts (Vue SFC Type Shim)

Create `assets/src/vue3/env.d.ts`. Without this, TypeScript doesn't know how to handle `.vue` imports and `import FaqApp from './FaqApp.vue'` will error:

```typescript
/// <reference types="vite/client" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<{}, {}, any>
  export default component
}
```

### .gitignore

Add the build output to your `.gitignore` (the `dist/` folder is regenerated on every build):

```
ResourcePackages/MyTheme/assets/dist/
ResourcePackages/MyTheme/node_modules/
```

### Install dependencies

```bash
cd ResourcePackages/MyTheme
npm install
```

---

## 5. Vite 8 Configuration

This is the most critical file. It handles the build, code splitting, and several Sitefinity-specific compatibility issues.

### vite.config.ts

```typescript
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'
import { resolve } from 'path'

// ── Vite Plugin: IIS Compatibility ──────────────────────────────────
// IIS blocks colons in URL paths. Vite's Vue plugin uses a virtual module
// "\0plugin-vue:export-helper" which becomes /@id/__x00__plugin-vue:export-helper.
// This plugin rewrites the import to use hyphens instead, avoiding the colon.
function iisCompatPlugin() {
  const COLON_PATTERN = /plugin-vue:export-helper/g
  const COMPAT_NAME = 'plugin-vue--export-helper'
  const COMPAT_ID = '\0' + COMPAT_NAME
  const helperCode = `export default (sfc, props) => {
  const target = sfc.__vccOpts || sfc;
  for (const [key, val] of props) { target[key] = val; }
  return target;
};`

  return {
    name: 'iis-compat',
    enforce: 'post' as const,
    transform(code: string, _id: string) {
      if (COLON_PATTERN.test(code)) {
        COLON_PATTERN.lastIndex = 0
        return code.replace(COLON_PATTERN, COMPAT_NAME)
      }
    },
    resolveId(id: string) {
      if (id === COMPAT_ID || id === COMPAT_NAME) return COMPAT_ID
    },
    load(id: string) {
      // Vite 8 (Rolldown) requires moduleType for virtual modules returning JS
      if (id === COMPAT_ID) return { code: helperCode, moduleType: 'js' }
    }
  }
}

// ── Vite Plugin: CORS Headers ───────────────────────────────────────
// Dev server runs on a different origin (e.g. localhost:5173) than the
// Sitefinity site (e.g. dev.yourapp.com). CORS headers let the browser
// load modules cross-origin during development.
function devCorsPlugin() {
  return {
    name: 'dev-cors',
    configureServer(server: any) {
      server.middlewares.use((_req: any, res: any, next: any) => {
        res.setHeader('Access-Control-Allow-Origin', '*')
        res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS')
        res.setHeader('Access-Control-Allow-Private-Network', 'true')
        next()
      })
    }
  }
}

// ── Vite Plugin: Razor View Reload ──────────────────────────────────
// Vite only watches its own module graph (.vue, .ts, .css). This plugin
// watches .cshtml files and triggers a full page reload when they change,
// so you don't have to manually refresh after editing Razor views.
function cshtmlReloadPlugin() {
  return {
    name: 'cshtml-reload',
    configureServer(server: any) {
      const viewsDir = resolve(__dirname, '../../Mvc/Views')
      server.watcher.add(viewsDir + '/**/*.cshtml')
      server.watcher.on('change', (file: string) => {
        if (file.endsWith('.cshtml')) {
          const ws = server.hot ?? server.ws
          ws.send({ type: 'full-reload', path: '*' })
        }
      })
    }
  }
}

export default defineConfig(({ mode }) => ({
  appType: 'custom',  // No index.html generation — Sitefinity serves the HTML

  // Production base path — tells Vite where chunks live so modulepreload
  // hints resolve correctly. Without this, preload hints use root-relative
  // paths (/chunks/...) instead of the actual deployment path.
  // Dev mode uses '/' since the Vite dev server serves from its own origin.
  base: mode === 'development'
    ? '/'
    : '/ResourcePackages/MyTheme/assets/dist/vue3/',

  plugins: [
    vue(),
    tailwindcss(),
    mode === 'development' && devCorsPlugin(),
    mode === 'development' && iisCompatPlugin(),
    mode === 'development' && cshtmlReloadPlugin()
  ],

  resolve: {
    alias: {
      '@': resolve(__dirname, 'assets/src/vue3/lib')
    }
  },

  // Vue 3 requires process.env.NODE_ENV for tree-shaking in non-standard
  // build modes (lib/app without an index.html). Without this, the Vue
  // runtime includes dev-only code in production builds.
  define: {
    'process.env.NODE_ENV': JSON.stringify(
      mode === 'development' ? 'development' : 'production'
    )
  },

  // Isolate from any parent postcss.config.js — Tailwind v4 uses its
  // Vite plugin instead of PostCSS.
  css: {
    postcss: {}
  },

  // ── Dev Server ────────────────────────────────────────────────────
  // Only used when running `npm run dev:hmr` for hot module replacement.
  // See Section 13 for full HMR setup instructions.
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true
  },

  // ── Production Build ──────────────────────────────────────────────
  build: {
    rolldownOptions: {
      input: {
        'vue3-runtime': resolve(__dirname, 'assets/src/vue3/lib/runtime.ts')
      },
      output: {
        // Entry chunk — stable name (no hash) for the <script> tag in Vue3.cshtml
        entryFileNames: '[name].js',
        // Dynamic chunks — content hash for automatic cache busting
        chunkFileNames: 'chunks/[name]-[hash].js',
        // CSS output — stable name, cache-busted via querystring in Vue3.cshtml
        assetFileNames: 'vue3-runtime.[ext]',

        // ── Rolldown Code Splitting ───────────────────────────────
        // Groups are evaluated by priority (higher wins). Each group
        // extracts matching modules into a shared chunk so they're
        // downloaded once and cached across pages.
        //
        // To disable code splitting (single-file output), set:
        //   codeSplitting: false
        // All dynamic import() calls are inlined — zero runtime change.
        codeSplitting: {
          groups: [
            // Vue 3 core + Reka UI (shadcn-vue's headless layer)
            { name: 'vendor-vue', test: /node_modules[\\/](vue|@vue|reka-ui)/, priority: 20 },
            // shadcn-vue UI components — shared by many widgets
            { name: 'shadcn-ui', test: /[\\/]components[\\/]ui[\\/]/, priority: 15 },
            // Auto-extract code shared by 2+ widget chunks
            { name: 'common', minShareCount: 2, minSize: 10000, priority: 5 }
          ]
        }
      }
    } as any,  // Vite types omit 'input' from rolldownOptions; Rolldown accepts it at runtime
    outDir: './assets/dist/vue3',
    emptyOutDir: true,
    cssCodeSplit: false  // Single CSS file — simpler to manage
  }
}))
```

**Key decisions explained:**

| Config | Why |
|--------|-----|
| `appType: 'custom'` | Prevents Vite from generating an `index.html` — Sitefinity serves the HTML |
| `base` (conditional) | Chunks loaded via `import()` resolve relative to the module URL. The `base` path ensures Vite's modulepreload helper generates correct absolute paths for the deployment directory |
| `iisCompatPlugin` | IIS rejects URLs containing colons. Vue's internal virtual module `plugin-vue:export-helper` contains a colon — this rewrites it to hyphens |
| `css: { postcss: {} }` | Prevents Vite from inheriting a parent `postcss.config.js` if one exists. Tailwind v4 runs as a Vite plugin, not PostCSS |
| `codeSplitting.groups` | Separates vendor code (Vue, Reka UI) from app code so vendor chunks stay cached when widget code changes |

---

## 6. Tailwind CSS v4 + shadcn-vue

### globals.css

Create `assets/src/vue3/lib/styles/globals.css`. This is the CSS entry point — Tailwind v4 uses `@import "tailwindcss"` instead of `@tailwind` directives.

```css
@import "tailwindcss";
@import "tw-animate-css";
@plugin "@tailwindcss/typography";

/* Dark mode variant — applies when .dark class is on <html> */
@custom-variant dark (&:is(.dark *));

/* Design mode variant — applies inside Sitefinity's page editor */
@custom-variant designmode (&:is(.sfPageEditor *));

/* Tell Tailwind to scan Razor views for utility classes.
   Adjust the relative path based on your folder structure.
   From: ResourcePackages/MyTheme/assets/src/vue3/lib/styles/
   To:   SitefinityWeb/Mvc/Views/                               */
@source "../../../../../../../Mvc/Views/**/*.cshtml";

/* ── shadcn-vue Theme Tokens ─────────────────────────────────────
   Maps CSS custom properties to Tailwind color utilities.
   e.g., `bg-primary` reads var(--primary), which is set in :root.
   This is the bridge between shadcn-vue's design system and Tailwind. */
@theme inline {
  --radius: 0.5rem;
  --font-sans: ui-sans-serif, system-ui, sans-serif, "Apple Color Emoji",
    "Segoe UI Emoji", "Segoe UI Symbol", "Noto Color Emoji";

  --color-background: var(--background);
  --color-foreground: var(--foreground);
  --color-card: var(--card);
  --color-card-foreground: var(--card-foreground);
  --color-popover: var(--popover);
  --color-popover-foreground: var(--popover-foreground);
  --color-primary: var(--primary);
  --color-primary-foreground: var(--primary-foreground);
  --color-secondary: var(--secondary);
  --color-secondary-foreground: var(--secondary-foreground);
  --color-muted: var(--muted);
  --color-muted-foreground: var(--muted-foreground);
  --color-accent: var(--accent);
  --color-accent-foreground: var(--accent-foreground);
  --color-destructive: var(--destructive);
  --color-destructive-foreground: var(--destructive-foreground);
  --color-border: var(--border);
  --color-input: var(--input);
  --color-ring: var(--ring);
  --color-chart-1: var(--chart-1);
  --color-chart-2: var(--chart-2);
  --color-chart-3: var(--chart-3);
  --color-chart-4: var(--chart-4);
  --color-chart-5: var(--chart-5);
  --color-sidebar: var(--sidebar);
  --color-sidebar-foreground: var(--sidebar-foreground);
  --color-sidebar-primary: var(--sidebar-primary);
  --color-sidebar-primary-foreground: var(--sidebar-primary-foreground);
  --color-sidebar-accent: var(--sidebar-accent);
  --color-sidebar-accent-foreground: var(--sidebar-accent-foreground);
  --color-sidebar-border: var(--sidebar-border);
  --color-sidebar-ring: var(--sidebar-ring);
}

/* ── Light Mode (Default) ────────────────────────────────────────── */
:root {
  --radius: 0.5rem;
  --background: hsl(0 0% 100%);
  --foreground: hsl(222.2 84% 4.9%);
  --card: hsl(0 0% 100%);
  --card-foreground: hsl(222.2 84% 4.9%);
  --popover: hsl(0 0% 100%);
  --popover-foreground: hsl(222.2 84% 4.9%);
  --primary: hsl(222.2 47.4% 11.2%);
  --primary-foreground: hsl(210 40% 98%);
  --secondary: hsl(210 40% 96.1%);
  --secondary-foreground: hsl(222.2 47.4% 11.2%);
  --muted: hsl(210 40% 96.1%);
  --muted-foreground: hsl(215.4 16.3% 46.9%);
  --accent: hsl(210 40% 96.1%);
  --accent-foreground: hsl(222.2 47.4% 11.2%);
  --destructive: hsl(0 84.2% 60.2%);
  --destructive-foreground: hsl(210 40% 98%);
  --border: hsl(214.3 31.8% 91.4%);
  --input: hsl(214.3 31.8% 91.4%);
  --ring: hsl(222.2 84% 4.9%);
  --chart-1: hsl(12 76% 61%);
  --chart-2: hsl(173 58% 39%);
  --chart-3: hsl(197 37% 24%);
  --chart-4: hsl(43 74% 66%);
  --chart-5: hsl(27 87% 67%);
  --sidebar: hsl(0 0% 98%);
  --sidebar-foreground: hsl(240 5.3% 26.1%);
  --sidebar-primary: hsl(240 5.9% 10%);
  --sidebar-primary-foreground: hsl(0 0% 98%);
  --sidebar-accent: hsl(240 4.8% 95.9%);
  --sidebar-accent-foreground: hsl(240 5.9% 10%);
  --sidebar-border: hsl(220 13% 91%);
  --sidebar-ring: hsl(217.2 91.2% 59.8%);
}

/* ── Dark Mode ───────────────────────────────────────────────────── */
.dark {
  --background: hsl(222.2 84% 4.9%);
  --foreground: hsl(210 40% 98%);
  --card: hsl(222.2 84% 4.9%);
  --card-foreground: hsl(210 40% 98%);
  --popover: hsl(222.2 84% 4.9%);
  --popover-foreground: hsl(210 40% 98%);
  --primary: hsl(210 40% 98%);
  --primary-foreground: hsl(222.2 47.4% 11.2%);
  --secondary: hsl(217.2 32.6% 17.5%);
  --secondary-foreground: hsl(210 40% 98%);
  --muted: hsl(217.2 32.6% 17.5%);
  --muted-foreground: hsl(215 20.2% 65.1%);
  --accent: hsl(217.2 32.6% 17.5%);
  --accent-foreground: hsl(210 40% 98%);
  --destructive: hsl(0 62.8% 30.6%);
  --destructive-foreground: hsl(210 40% 98%);
  --border: hsl(217.2 32.6% 17.5%);
  --input: hsl(217.2 32.6% 17.5%);
  --ring: hsl(212.7 26.8% 83.9%);
  --chart-1: hsl(220 70% 50%);
  --chart-2: hsl(160 60% 45%);
  --chart-3: hsl(30 80% 55%);
  --chart-4: hsl(280 65% 60%);
  --chart-5: hsl(340 75% 55%);
  --sidebar: hsl(240 5.9% 10%);
  --sidebar-foreground: hsl(240 4.8% 95.9%);
  --sidebar-primary: hsl(224.3 76.3% 48%);
  --sidebar-primary-foreground: hsl(0 0% 100%);
  --sidebar-accent: hsl(240 3.7% 15.9%);
  --sidebar-accent-foreground: hsl(240 4.8% 95.9%);
  --sidebar-border: hsl(240 3.7% 15.9%);
  --sidebar-ring: hsl(217.2 91.2% 59.8%);
}

/* ── Base Layer ──────────────────────────────────────────────────── */
@layer base {
  * {
    @apply border-border outline-ring/50;
  }
  body {
    @apply bg-background text-foreground;
  }
}

/* ── Design mode skeleton loader ─────────────────────────────────
   When a widget is drag-dropped in Sitefinity's page editor, the
   .rdContent element is briefly empty while the server renders the
   .cshtml. This shows an animated skeleton during that fetch.
   ─────────────────────────────────────────────────────────────── */
.sfPageEditor .rdContent:empty {
  position: relative;
  height: 120px !important;
  padding: 1.5rem;
  border-radius: 0.5rem;
  border: 1px solid #e5e7eb;
  background-color: #ffffff;
  animation: skeleton-pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
  background-image:
    linear-gradient(#e2e8f0 100%, transparent 100%),
    linear-gradient(#e2e8f0 100%, transparent 100%),
    linear-gradient(#e2e8f0 100%, transparent 100%);
  background-repeat: no-repeat;
  background-size: 60% 12px, 45% 12px, 30% 12px;
  background-position: 1.5rem 1.5rem, 1.5rem calc(1.5rem + 24px), 1.5rem calc(1.5rem + 48px);
}

.sfPageEditor .rdContent:empty::after {
  content: "Loading widget\2026";
  position: absolute;
  bottom: 1rem;
  right: 1.5rem;
  font-size: 12px;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  color: #94a3b8;
}

@keyframes skeleton-pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.5; }
}
```

### components.json (shadcn-vue config)

```json
{
  "style": "default",
  "typescript": true,
  "tailwind": {
    "config": "../../tailwind.config.js",
    "css": "assets/src/vue3/lib/styles/globals.css",
    "baseColor": "slate",
    "cssVariables": true
  },
  "aliases": {
    "components": "@/components",
    "utils": "@/utils"
  }
}
```

> **Note:** The `tailwind.config` path points to a file that doesn't exist — Tailwind v4 uses its Vite plugin, not a config file. The shadcn-vue CLI still reads this path for its own scaffolding but it doesn't affect your build. If the CLI complains, create an empty `tailwind.config.js` at that path: `export default {}`

### utils.ts

Create `assets/src/vue3/lib/utils.ts`:

```typescript
import { type ClassValue, clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
```

### Install your first shadcn-vue components

```bash
npx shadcn-vue@latest add accordion card button
```

> **Tailwind v4 fix:** After installing any shadcn-vue component, check for bare CSS variable syntax that Tailwind v4 doesn't support:
> - Change `w-[--sidebar-width]` to `w-(--sidebar-width)` (parentheses, not brackets)
> - Change `has-[[data-state=open]]` to `has-data-[state=open]`

### Container Queries

Tailwind CSS v4 supports `@container` queries natively — no plugin required. In a CMS, widgets get dropped into sidebars and grid columns, so viewport breakpoints (`sm:`, `md:`, `lg:`) don't work — they respond to browser width, not widget width. Use container queries instead:

1. Add `class="@container"` to the widget's root element
2. Use `@sm:`, `@md:`, `@lg:`, `@xl:` instead of `sm:`, `md:`, `lg:`, `xl:`

```html
<!-- Viewport queries — breaks in sidebars (300px wide but browser says "lg") -->
<div class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">

<!-- Container queries — adapts to actual widget width -->
<section class="@container">
  <div class="grid grid-cols-1 @md:grid-cols-2 @lg:grid-cols-3 gap-4">
```

Custom breakpoints work too: `@[960px]:grid-cols-4` fires when the container itself is 960px+.

### Page Layout Grid Templates

Sitefinity lets content editors choose column layouts when building pages. These layout templates live in `ResourcePackages/MyTheme/GridSystem/Templates/` and define the HTML structure for each column arrangement. The editor sees these as options in the page layout toolbar.

**Legacy templates** use Bootstrap 3's 12-column grid with viewport breakpoints:

```html
<!-- grid-6+6.html — Bootstrap (legacy) -->
<div class="row" data-sf-element="Row">
    <div class="sf_colsIn col-md-6" data-sf-element="Column 1"
         data-placeholder-label="Column 1">
    </div>
    <div class="sf_colsIn col-md-6" data-sf-element="Column 2"
         data-placeholder-label="Column 2">
    </div>
</div>
```

**Tailwind grid templates** replace Bootstrap with CSS Grid + container queries. Name them with a `tw-` prefix so both sets coexist during migration:

**Key design decisions in the Tailwind grid templates:**

1. **`@container` on the row AND each column** — the row container lets column counts respond to available width. The column containers let *widgets inside* each column respond to their own width. This is the cascade that makes everything work.
2. **`min-w-0` on every column** — prevents grid children from overflowing their track when content (images, tables, code blocks) is wider than the column. A common CSS Grid gotcha.
3. **`sf_colsIn` class is required** — Sitefinity uses this class to identify droppable content areas in the page editor.
4. **`data-sf-element` and `data-placeholder-label`** — Sitefinity uses these for the element breadcrumb and the placeholder text shown in empty columns during editing.

**All Tailwind grid templates:**

```html
<!-- tw-grid-12.html — Full width (single column, no row container needed) -->
<div class="grid grid-cols-1 gap-4" data-sf-element="Row">
    <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
         data-placeholder-label="Column 1">
    </div>
</div>

<!-- tw-grid-6+6.html — Equal halves -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @xl:grid-cols-2 gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Column 1">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Column 2">
        </div>
    </div>
</div>

<!-- tw-grid-8+4.html — Content + sidebar (2:1 ratio) -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @3xl:grid-cols-[2fr_1fr] gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Content">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Sidebar">
        </div>
    </div>
</div>

<!-- tw-grid-4+8.html — Sidebar + content (1:2 ratio) -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @3xl:grid-cols-[1fr_2fr] gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Sidebar">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Content">
        </div>
    </div>
</div>

<!-- tw-grid-9+3.html — Wide content + narrow sidebar (3:1 ratio) -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @3xl:grid-cols-[3fr_1fr] gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Content">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Sidebar">
        </div>
    </div>
</div>

<!-- tw-grid-3+9.html — Narrow sidebar + wide content (1:3 ratio) -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @3xl:grid-cols-[1fr_3fr] gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Sidebar">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Content">
        </div>
    </div>
</div>

<!-- tw-grid-4+4+4.html — Three equal columns -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @xl:grid-cols-2 @3xl:grid-cols-3 gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Column 1">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Column 2">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 3"
             data-placeholder-label="Column 3">
        </div>
    </div>
</div>

<!-- tw-grid-3+6+3.html — Center-weighted three columns (1:2:1 ratio) -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @xl:grid-cols-2 @3xl:grid-cols-[1fr_2fr_1fr] gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Left">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Center">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 3"
             data-placeholder-label="Right">
        </div>
    </div>
</div>

<!-- tw-grid-3+3+3+3.html — Four equal columns -->
<div class="@container" data-sf-element="Row">
    <div class="grid grid-cols-1 @xl:grid-cols-2 @4xl:grid-cols-4 gap-4">
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 1"
             data-placeholder-label="Column 1">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 2"
             data-placeholder-label="Column 2">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 3"
             data-placeholder-label="Column 3">
        </div>
        <div class="sf_colsIn @container min-w-0" data-sf-element="Column 4"
             data-placeholder-label="Column 4">
        </div>
    </div>
</div>

<!-- tw-grid-8+center.html — Centered content (max-width constrained) -->
<div class="grid grid-cols-1 gap-4" data-sf-element="Row">
    <div class="sf_colsIn @container min-w-0 max-w-4xl mx-auto w-full"
         data-sf-element="Column 1" data-placeholder-label="Centered Content">
    </div>
</div>
```

**Additional layout wrappers** (not grid templates, but available alongside them):

```html
<!-- card.html — shadcn-vue Card wrapper for elevated content -->
<div data-slot="card"
     class="card-wrapper rounded-xl border bg-card text-card-foreground shadow-sm overflow-visible">
    <div data-slot="card-content"
         class="sf_colsIn card-content p-6"
         data-sf-element="Card" data-placeholder-label="Card">
    </div>
</div>

<!-- pod.html — Legacy pod wrapper (sharp corners by design) -->
<div class="pod-wrapper">
    <div class="pod-content row" data-sf-element="Row">
        <div class="sf_colsIn col-md-12" data-sf-element="Pod"
             data-placeholder-label="Pod">
        </div>
    </div>
</div>

<!-- well.html — Inset/recessed content area -->
<div class="well">
    <div class="row" data-sf-element="Row">
        <div class="sf_colsIn col-md-12" data-sf-element="Column 1"
             data-placeholder-label="Content">
        </div>
    </div>
</div>
```

**Responsive behavior summary:**

| Template | Breakpoint strategy | Mobile | Medium | Large |
|----------|-------------------|--------|--------|-------|
| `tw-grid-6+6` | `@xl:grid-cols-2` | 1 col stacked | 2 cols | 2 cols |
| `tw-grid-8+4` / `4+8` | `@3xl:grid-cols-[2fr_1fr]` | 1 col stacked | 1 col stacked | 2 cols (weighted) |
| `tw-grid-9+3` / `3+9` | `@3xl:grid-cols-[3fr_1fr]` | 1 col stacked | 1 col stacked | 2 cols (weighted) |
| `tw-grid-4+4+4` | `@xl:2 → @3xl:3` | 1 col | 2 cols | 3 cols |
| `tw-grid-3+6+3` | `@xl:2 → @3xl:[1fr_2fr_1fr]` | 1 col | 2 cols | 3 cols (weighted) |
| `tw-grid-3+3+3+3` | `@xl:2 → @4xl:4` | 1 col | 2 cols | 4 cols |
| `tw-grid-8+center` | None (always 1 col) | `max-w-4xl` centered | same | same |

The two-column layouts (`8+4`, `4+8`, `9+3`, `3+9`) use `@3xl` (a wider breakpoint) because content+sidebar layouts need enough room for both columns to be readable. Equal-split layouts (`6+6`) can break earlier at `@xl`.

---

## 7. Widget Infrastructure

These files are the backbone of the Vue 3 integration. You write them once and every widget reuses them.

### widget-registry.ts

Create `assets/src/vue3/lib/widget-registry.ts`:

```typescript
/**
 * Widget Registry — enables Vue 3 widgets to mount after XHR injection
 * in Sitefinity design mode.
 *
 * Each widget calls registerWidget(mountFn) instead of manually wiring
 * DOMContentLoaded. The registry runs the mount function immediately
 * (or on DOMContentLoaded if still loading), and stores it so the
 * design-mode MutationObserver can re-trigger all mounts when new
 * widget elements appear in the DOM.
 */

const mountFunctions: (() => void)[] = []

export function registerWidget(mountFn: () => void) {
  mountFunctions.push(mountFn)
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', mountFn)
  } else {
    mountFn()
  }
}

export function mountAllWidgets() {
  mountFunctions.forEach(fn => fn())
}
```

### runtime.ts

Create `assets/src/vue3/lib/runtime.ts`. This is the entry point that Vite builds:

```typescript
import './styles/globals.css'
import { mountAllWidgets } from './widget-registry'

// ── Widget Map ──────────────────────────────────────────────────
// Maps CSS selectors to dynamic imports. When scanAndMount() finds
// a matching element in the DOM, it loads only that widget's chunk.
// Add new widgets here as you build them.
const widgetMap: Record<string, () => Promise<any>> = {
  '[data-widget="faq"]': () => import('../widgets/Mvc/Views/Faq'),
  // '[data-widget="hero"]':  () => import('../widgets/Mvc/Views/Hero'),
  // '[data-widget="stats"]': () => import('../widgets/Mvc/Views/Stats'),
}

// Track which selectors have had their chunks loaded to avoid duplicate imports.
const loadedSelectors = new Set<string>()

function scanAndMount() {
  for (const [selector, loader] of Object.entries(widgetMap)) {
    if (!loadedSelectors.has(selector) && document.querySelector(selector)) {
      loadedSelectors.add(selector)
      loader().catch((err) => {
        // Remove from loaded set so the next scan retries
        loadedSelectors.delete(selector)
        console.error(`[Vue3] Failed to load chunk for ${selector}:`, err)
      })
    }
  }
}

// Initial mount — scan DOM for widget mount points and load their chunks
if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', scanAndMount)
} else {
  scanAndMount()
}

// ── Design Mode Watcher ─────────────────────────────────────────
// When Sitefinity's page editor injects a widget via XHR, the DOM
// changes but no page navigation occurs. This MutationObserver
// detects new widget elements and triggers chunk loading + mounting.
function initDesignModeWatcher() {
  // .sfPageEditor is the body class Sitefinity adds in edit mode
  if (!document.querySelector('.sfPageEditor')) return

  // Debounce: Sitefinity's editor fires many rapid DOM mutations
  // (tooltips, selection chrome, panels). Batch into one scan per frame.
  let scanPending = false
  const observer = new MutationObserver(() => {
    if (scanPending) return
    scanPending = true
    requestAnimationFrame(() => {
      scanPending = false
      scanAndMount()       // Load chunks for NEW widget types
      mountAllWidgets()    // Re-mount for new instances of EXISTING types
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

### Vue3Controller.cs (Base Controller)

Create in your controls project at `Mvc/Controllers/Vue3Controller.cs`:

```csharp
using System.ComponentModel;
using System.Web.Mvc;
using YourApp.Controls.Code;
using YourApp.Controls.Mvc.Models.Vue3;
using Telerik.Sitefinity.Web.UI;

namespace YourApp.Controls.Mvc.Controllers
{
    /// <summary>
    /// Base controller for all Vue 3 widgets that use the data-island pattern.
    ///
    /// During normal rendering: returns the Razor view with ConfigJson.
    /// During Sitefinity's Lucene index crawl: returns IndexContent as plain HTML
    /// so the search indexer can find content without executing JavaScript.
    ///
    /// Implements ICustomWidgetVisualization so Sitefinity shows a
    /// "Click to add content" placeholder when the widget has no content.
    /// </summary>
    public abstract class Vue3Controller : Controller, ICustomWidgetVisualization
    {
        public virtual ActionResult Index()
        {
            var model = this.GetModel();

            if (Util.IsIndexingMode)
                return Content(model.IndexContent ?? string.Empty);

            return View(this.TemplateName, model);
        }

        protected override void HandleUnknownAction(string actionName)
        {
            var model = this.GetModel();

            if (Util.IsIndexingMode)
            {
                Content(model.IndexContent ?? string.Empty)
                    .ExecuteResult(this.ControllerContext);
                return;
            }

            View(this.TemplateName, model).ExecuteResult(this.ControllerContext);
        }

        /// <summary>Build the view model. Each widget controller implements this.</summary>
        protected abstract Vue3WidgetModel GetModel();

        [Browsable(false)]
        public abstract string TemplateName { get; }

        /// <summary>
        /// When true, Sitefinity shows EmptyLinkText in design mode instead of
        /// rendering the widget. Override in list-based controllers.
        /// </summary>
        [Browsable(false)]
        public virtual bool IsEmpty => false;

        [Browsable(false)]
        public virtual string EmptyLinkText => "Click to add content";
    }
}
```

### Vue3WidgetModel.cs

Create at `Mvc/Models/Vue3/Vue3WidgetModel.cs`:

```csharp
namespace YourApp.Controls.Mvc.Models.Vue3
{
    public class Vue3WidgetModel
    {
        /// <summary>
        /// JSON-serialized widget configuration. Rendered into a data island
        /// in the .cshtml view for Vue to parse client-side.
        /// </summary>
        public string ConfigJson { get; set; }

        /// <summary>
        /// Plain HTML for Sitefinity's Lucene search indexer.
        /// Rendered server-side only when Util.IsIndexingMode is true.
        /// </summary>
        public string IndexContent { get; set; }
    }
}
```

### Util.cs (Helper Utilities)

Create at `Code/Util.cs`. These are the minimum helpers needed by the Vue 3 infrastructure:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Web;
using System.Web.Hosting;
using Telerik.Sitefinity.Services;

namespace YourApp.Controls.Code
{
    public static class Util
    {
        /// <summary>
        /// True when running on the dev/staging environment.
        /// Used to gate the CI login controller and Vite dev server.
        /// Adjust the hostname check to match your dev domain.
        /// </summary>
        public static bool IsDev
        {
            get
            {
                var context = SystemManager.CurrentHttpContext;
                if (context != null)
                    return context.Request.Url.Host.Contains("dev.yourapp.com");
                return false;
            }
        }

        /// <summary>
        /// True when Sitefinity's Lucene search indexer is rendering the page.
        /// The indexer sets "IsInIndexMode" in Page.Items before rendering.
        /// </summary>
        public static bool IsIndexingMode
        {
            get
            {
                var page = SystemManager.CurrentHttpContext?.CurrentHandler
                    as System.Web.UI.Page;
                return page?.Items["IsInIndexMode"] != null;
            }
        }

        /// <summary>
        /// Generates an 8-character content hash for cache busting.
        /// Returns a fallback version string if the file doesn't exist.
        ///
        /// For production, consider caching the result keyed by
        /// (virtualPath + lastWriteTimeUtc) so you're not re-hashing
        /// on every request. HttpRuntime.Cache or a ConcurrentDictionary
        /// both work well here.
        /// </summary>
        public static string FileHash(string virtualPath)
        {
            try
            {
                var physicalPath = HostingEnvironment.MapPath("~" + virtualPath);
                if (physicalPath == null || !File.Exists(physicalPath))
                    return "1";

                using (var md5 = MD5.Create())
                using (var stream = File.OpenRead(physicalPath))
                {
                    var hash = md5.ComputeHash(stream);
                    return BitConverter.ToString(hash)
                        .Replace("-", "").Substring(0, 8).ToLower();
                }
            }
            catch
            {
                return "1";
            }
        }
    }
}
```

### Error monitoring — the Sentry bridge (optional, strongly recommended)

**Vue 3 swallows widget errors.** Exceptions thrown inside `render`, `mounted`, `updated`, `watch`, `computed`, and other reactive boundaries are routed to `app.config.errorHandler` and NEVER reach `window.onerror` — so Sentry's (or any APM's) default browser auto-capture sees nothing. Every Vue widget error on your site is invisible until you wire the per-app handler.

Create `assets/src/vue3/lib/sentry-vue.ts` — a drop-in `createApp` replacement:

```typescript
import { createApp as vueCreateApp, type App, type Component } from 'vue'
import * as Sentry from '@sentry/vue'

let initialized = false

function ensureInit() {
  if (initialized) return
  initialized = true
  // Emit DSN/environment server-side (e.g. a small partial that sets window.SENTRY_CONFIG)
  const cfg = (window as any).SENTRY_CONFIG
  if (!cfg?.dsn) return
  // Errors-only: no Replay / BrowserTracing imports, so they tree-shake out of the bundle
  Sentry.init({ dsn: cfg.dsn, environment: cfg.environment, integrations: [] })
}

/** Same signature as Vue's createApp - swap the import and every widget is covered. */
export function createApp(rootComponent: Component, rootProps?: Record<string, unknown>): App {
  ensureInit()
  const app = vueCreateApp(rootComponent, rootProps)
  // Sentry's official Vue integration: hooks app.config.errorHandler and forwards
  // errors with component name, props, and lifecycle hook attached.
  Sentry.attachErrorHandler(app, { attachProps: true, logErrors: true })
  return app
}
```

Then in every widget `index.ts`, import `createApp` from the wrapper instead of `vue`:

```typescript
import { createApp } from '@/sentry-vue'   // instead of: from 'vue'
```

Do NOT also install your own `app.config.errorHandler` in widget code — `attachErrorHandler` owns that slot; centralize any custom behavior in the wrapper so every widget gets it.

---

## 8. Loading Vue 3 on Pages

### Vue3.cshtml (Script Loader Partial)

Create at `Mvc/Views/Shared/Vue3.cshtml`:

```razor
@using YourApp.Controls.Code;
@using Telerik.Sitefinity.Frontend.Mvc.Helpers;
@using Telerik.Sitefinity.Services;

@{
    // Guard: prevent double-rendering if multiple widgets include this partial
    var vue3Key = "__vue3_partial_rendered";
    if (HttpContext.Current.Items.Contains(vue3Key)) { return; }
    HttpContext.Current.Items[vue3Key] = true;

    // Force production build via ?vite=prod querystring or vite_prod session variable
    // (set by dev login with &env=prod — dies with the server session, not browser quirks)
    var forceProd = HttpContext.Current.Request.QueryString["vite"] == "prod"
        || (HttpContext.Current.Session?["vite_prod"] as string == "1");

    var useVite = Util.IsDev
        && !SystemManager.IsDesignMode
        && !forceProd;
}

@if (useVite)
{
    @* Dev mode — Vite dev server provides HMR. Replace hostname with your dev setup. *@
    <script type="module" crossorigin src="https://localhost:5173/@@vite/client"></script>
    <script type="module" crossorigin src="https://localhost:5173/assets/src/vue3/lib/runtime.ts"></script>
}
else
{
    var jsPath = "/ResourcePackages/MyTheme/assets/dist/vue3/vue3-runtime.js";
    var cssPath = "/ResourcePackages/MyTheme/assets/dist/vue3/vue3-runtime.css";

    @* ES module — enables dynamic import() for chunk loading.
       Module scripts are deferred by default, so execution order is the
       same as placing a classic <script> at end of body. *@
    <script type="module" src="@(jsPath)?v=@(Util.FileHash(jsPath))"></script>
    @Html.StyleSheet(cssPath + "?v=" + Util.FileHash(cssPath), "head")
}
```

### Include in Your Layout

In your Sitefinity layout `.cshtml` (e.g., `Mvc/Views/Layouts/default.cshtml`), add the partial at the end of the `<body>`:

```razor
<!DOCTYPE html>
<html>
<head>
    @* Sitefinity head sections *@
    @Html.Section("head")
</head>
<body>
    @* Page content *@
    @Html.SfPlaceHolder("Body")

    @* Sitefinity script sections *@
    @Html.Section("scripts")

    @* Vue 3 runtime — must be AFTER all Sitefinity sections *@
    @Html.Partial("Vue3")
</body>
</html>
```

The `<script type="module">` is deferred by default — it executes after DOM parsing, in document order. This is equivalent to placing a classic script at the end of `<body>`.

---

## 9. Your First Widget — FAQ

Let's build a complete FAQ widget from controller to Vue component.

### FaqController.cs

Create at `Mvc/Controllers/Faq/FaqController.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Telerik.Sitefinity.Mvc;
using Telerik.Sitefinity.Frontend.Mvc.Infrastructure.Controllers.Attributes;
using Progress.Sitefinity.Renderer.Designers;
using Progress.Sitefinity.Renderer.Designers.Attributes;
using Telerik.Sitefinity.Modules.Pages.PropertyPersisters;
using YourApp.Controls.Mvc.Models.Vue3;

namespace YourApp.Controls.Mvc.Controllers
{
    // ── Sub-item model for the FAQ list ──────────────────────────
    public class FaqItem
    {
        [DisplayName("Question")]
        public string Question { get; set; }

        [ContentSection(1)]
        [DisplayName("Answer")]
        [DataType(customDataType: KnownFieldTypes.Html)]
        public string Answer { get; set; }
    }

    // ── The widget controller ───────────────────────────────────
    [EnhanceViewEnginesAttribute]
    [ControllerToolboxItem(
        Name = "Faq_MVC",
        Title = "FAQ",
        SectionName = "Custom Widgets",
        CssClass = "sfListitemsIcn sfMvcIcn"  // specific icon first, then sfMvcIcn (the blue MVC badge); sfMvcIcn alone = blank tile
    )]
    public class FaqController : Vue3Controller
    {
        // Show "Click to add content" placeholder when no FAQs exist
        [Browsable(false)]
        public override bool IsEmpty =>
            this.Faqs == null || this.Faqs.Count == 0;

        protected override Vue3WidgetModel GetModel()
        {
            var faqs = this.Faqs ?? new List<FaqItem>();

            // Build plain HTML for search indexer
            var indexHtml = new StringBuilder();
            if (!string.IsNullOrEmpty(this.Heading))
                indexHtml.AppendFormat("<h2>{0}</h2>", this.Heading);
            foreach (var faq in faqs)
            {
                indexHtml.AppendFormat("<h3>{0}</h3>", faq.Question);
                indexHtml.Append(faq.Answer);
            }

            return new Vue3WidgetModel
            {
                ConfigJson = ServiceStack.Text.JsonSerializer.SerializeToString(new
                {
                    heading = this.Heading,
                    description = this.Description,
                    faqs = faqs.Select(f => new
                    {
                        question = f.Question,
                        answer = f.Answer
                    }).ToArray()
                }),
                IndexContent = indexHtml.ToString()
            };
        }

        #region Properties

        [Browsable(false)]
        public override string TemplateName => "Faq.Default";

        [Category("Content")]
        [DisplayName("Heading")]
        public string Heading { get; set; }

        [Category("Content")]
        [DisplayName("Description")]
        [DataType(customDataType: KnownFieldTypes.TextArea)]
        public string Description { get; set; }

        [Category("FAQs")]
        [DisplayName("FAQ Items")]
        [TableView(Reorderable = true, ColumnCount = 1)]
        [PropertyPersistence(PersistAsJson = true)]
        public IList<FaqItem> Faqs { get; set; } = new List<FaqItem>();

        #endregion
    }
}
```

**Key things to note:**

- `[ControllerToolboxItem]` registers the widget in Sitefinity's widget toolbox
- `[EnhanceViewEnginesAttribute]` enables Feather's view resolution (finds views across projects)
- `[PropertyPersistence(PersistAsJson = true)]` is **mandatory** on `IList<>` properties containing complex objects — without it, Sitefinity's default serialization breaks nested types
- `[TableView(Reorderable = true)]` gives the content editor a drag-to-reorder list UI
- `IsEmpty` enables the "Click to add content" placeholder in design mode
- `ServiceStack.Text.JsonSerializer` is bundled with Sitefinity — no extra NuGet needed

### Faq.Default.cshtml

Create at `Mvc/Views/Faq/Faq.Default.cshtml`:

```razor
@model YourApp.Controls.Mvc.Models.Vue3.Vue3WidgetModel

<div data-widget="faq">
    <template class="widget-config">@Html.Raw(Model.ConfigJson)</template>
</div>
```

That's the entire Razor view. The `data-widget="faq"` attribute is what `runtime.ts` uses to find this widget in the DOM and load its chunk. The `<template>` tag holds the JSON config — it's not rendered visually by the browser.

> **Alternative island:** `<script type="application/json" class="widget-config">@Html.Raw(Model.ConfigJson)</script>` works too — a JSON script block is inert and survives HTML processing that can occasionally interfere with `<template>` content. The `index.ts` selector below accepts both styles, so pick per view.

### index.ts (Widget Entry Point)

Create at `assets/src/vue3/widgets/Mvc/Views/Faq/index.ts`:

```typescript
import { createApp } from '@/sentry-vue'   // see Section 7 "Error monitoring"; use 'vue' if you skipped Sentry
import { registerWidget } from '@/widget-registry'
import FaqApp from './FaqApp.vue'

function mount() {
  document.querySelectorAll<HTMLElement>('[data-widget="faq"]').forEach(el => {
    // Skip already-mounted elements (prevents double-mounting in design mode)
    if (el.dataset.vueMounted) return
    el.dataset.vueMounted = 'true'

    // Parse the JSON config from the data island. Accept both island styles:
    // <template class="widget-config"> and <script type="application/json" class="widget-config">
    const configEl = el.querySelector('template.widget-config, script.widget-config')
    let config = {}
    try {
      config = JSON.parse(configEl?.innerHTML || '{}')
    } catch (e) {
      console.error('[FAQ] Failed to parse widget config:', e)
    }

    const app = createApp(FaqApp, { serverData: config })
    app.mount(el)
  })
}

registerWidget(mount)
```

### types.ts

Create at `assets/src/vue3/widgets/Mvc/Views/Faq/types.ts`:

```typescript
export interface FaqItem {
  question: string
  answer: string
}

export interface FaqConfig {
  heading: string
  description: string
  faqs: FaqItem[]
}
```

### FaqApp.vue

Create at `assets/src/vue3/widgets/Mvc/Views/Faq/FaqApp.vue`:

```vue
<script setup lang="ts">
defineOptions({ name: 'FaqWidget' })

import type { FaqConfig } from './types'
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from '@/components/ui/accordion'

const props = defineProps<{ serverData: FaqConfig }>()
</script>

<template>
  <section class="@container" data-testid="faq-section">
    <div class="mx-auto max-w-3xl py-8">
      <h2
        v-if="serverData.heading"
        class="text-2xl font-bold tracking-tight text-foreground"
        data-testid="faq-heading"
      >
        {{ serverData.heading }}
      </h2>

      <p
        v-if="serverData.description"
        class="mt-2 text-muted-foreground"
        data-testid="faq-description"
      >
        {{ serverData.description }}
      </p>

      <Accordion type="single" collapsible class="mt-6 w-full">
        <AccordionItem
          v-for="(faq, i) in serverData.faqs"
          :key="i"
          :value="`faq-${i}`"
          data-testid="faq-item"
        >
          <AccordionTrigger data-testid="faq-question">
            {{ faq.question }}
          </AccordionTrigger>
          <AccordionContent>
            <div
              class="prose max-w-none text-muted-foreground"
              v-html="faq.answer"
              data-testid="faq-answer"
            />
          </AccordionContent>
        </AccordionItem>
      </Accordion>
    </div>
  </section>
</template>
```

**Now add it to the widget map in `runtime.ts`:**

```typescript
const widgetMap: Record<string, () => Promise<any>> = {
  '[data-widget="faq"]': () => import('../widgets/Mvc/Views/Faq'),
}
```

Build and drop it onto a page in Sitefinity's page editor. It should show "Click to add content" until you add FAQ items.

> **HMR tip:** If you're running `npm run dev:hmr` (see Section 13), you can now edit `FaqApp.vue` — change a class, tweak the heading markup, add a new element — and see the change reflected in the browser within milliseconds, without a page refresh. On a CMS like Sitefinity where a full page load takes 5-15 seconds, this makes iterating on widget templates dramatically faster. You don't need to rebuild, restart IIS, or wait for the app pool to recycle. Just save and see.

---

## 10. Adding More Widgets

The pattern is always the same:

1. **Create the controller** extending `Vue3Controller` — serialize config to JSON, build `IndexContent` for search
2. **Create the `.cshtml` view** — `<div data-widget="your-widget">` + `<template class="widget-config">`
3. **Create the Vue entry** (`index.ts`) — `querySelectorAll` + `createApp` + `registerWidget(mount)`
4. **Create the Vue component** (`YourWidgetApp.vue`) — receives `serverData` prop
5. **Add to `widgetMap`** in `runtime.ts`

Each new widget automatically gets its own chunk. Pages that don't use it never download its code. And with HMR running (Section 13), changes to any widget's `.vue` files appear instantly in the browser — no waiting for Sitefinity to reload.

```typescript
// runtime.ts — just add one line per widget
const widgetMap: Record<string, () => Promise<any>> = {
  '[data-widget="faq"]':       () => import('../widgets/Mvc/Views/Faq'),
  '[data-widget="hero"]':      () => import('../widgets/Mvc/Views/Hero'),
  '[data-widget="stats"]':     () => import('../widgets/Mvc/Views/Stats'),
  '[data-widget="team"]':      () => import('../widgets/Mvc/Views/Team'),
  '[data-widget="pricing"]':   () => import('../widgets/Mvc/Views/Pricing'),
  '#admin-tool-root':          () => import('../widgets/Mvc/Views/AdminTool'),
  // Each import() boundary = one chunk in the build output.
  // Keys are ANY CSS selector — data attributes are the convention, but an
  // id selector works fine for one-off admin tools.
}
```

**Always-on code should NOT go through the widgetMap.** Anything every page needs (a layout shell, a global nav enhancement) gains nothing from lazy loading — `import` it statically at the top of `runtime.ts` instead, so it ships in the entry chunk and runs immediately:

```typescript
// runtime.ts — eager, not lazy: every page renders the sidebar shell
import '../widgets/layout/sidebar-layout'
```

---

## 11. Dev Impersonation Controller

When developing against Sitefinity, you often need to test pages as different users without going through your full SSO/login flow every time. This widget controller impersonates a Sitefinity user by username on dev environments. **It is gated behind `Util.IsDev`** — it does nothing in production.

### The pattern

The core of Sitefinity impersonation is three API calls:

```csharp
UserManager userManager = UserManager.GetManager();
using (new ElevatedModeRegion(userManager))
{
    User user = userManager.GetUser(username);
    SecurityManager.AuthenticateUser(null, username, true, out user);
}
```

`ElevatedModeRegion` bypasses permission checks (you're not logged in yet). `AuthenticateUser` with `persistent: true` creates a Sitefinity session cookie. After this call, `ClaimsManager.GetCurrentIdentity()` returns the impersonated user for the rest of the request — and subsequent requests use the session cookie.

### DevLoginController.cs

Create at `Mvc/Controllers/DevLogin/DevLoginController.cs`:

```csharp
using System;
using System.Web;
using System.Web.Mvc;
using Telerik.Sitefinity.Data;
using Telerik.Sitefinity.Frontend.Mvc.Infrastructure.Controllers.Attributes;
using Telerik.Sitefinity.Mvc;
using Telerik.Sitefinity.Security;
using Telerik.Sitefinity.Security.Model;
using Telerik.Sitefinity.Services;
using Telerik.Sitefinity.Web.UI;
using YourApp.Controls.Code;

namespace YourApp.Controls.Mvc.Controllers.DevLogin
{
    /// <summary>
    /// Dev impersonation controller. Authenticates as a Sitefinity user
    /// by username and redirects to a target page.
    ///
    /// Setup: Create a Sitefinity page (e.g. at path /dev/login),
    ///        drop this widget on it, and publish.
    ///
    /// Usage: /dev/login?user=admin@example.com&amp;returnurl=/dashboard
    ///
    /// Only works when Util.IsDev is true. Returns a blank page otherwise.
    /// </summary>
    [EnhanceViewEnginesAttribute]
    [ControllerToolboxItem(
        Name = "DevLogin_MVC",
        Title = "Dev Login",
        SectionName = "Developer",
        CssClass = "sfLoginIcn sfMvcIcn"
    )]
    [IndexRenderMode(IndexRenderModes.NoOutput)]
    public class DevLoginController : Controller
    {
        public ActionResult Index(string user, string returnUrl)
        {
            // Do nothing in design mode or on non-dev environments
            if (SystemManager.IsDesignMode || !Util.IsDev)
                return View("Default");

            if (string.IsNullOrWhiteSpace(user))
                return Content("Missing ?user= parameter");

            // Impersonate the requested user via Sitefinity's security API
            UserManager userManager = UserManager.GetManager();
            using (new ElevatedModeRegion(userManager))
            {
                User sfUser = userManager.GetUser(user);
                if (sfUser == null)
                    return Content($"User '{user}' not found in Sitefinity");

                SecurityManager.AuthenticateUser(null, user, true, out sfUser);
            }

            // env=prod sets a server-side session variable that Vue3.cshtml reads
            // to serve the production build instead of the Vite dev server.
            // Without env=prod, the variable is cleared so HMR mode is restored.
            var env = Request.QueryString["env"];
            if ("prod".Equals(env, StringComparison.OrdinalIgnoreCase))
            {
                Session["vite_prod"] = "1";
            }
            else
            {
                Session.Remove("vite_prod");
            }

            return Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
        }
    }
}
```

### Default.cshtml

Create at `Mvc/Views/DevLogin/Default.cshtml`:

```razor
<p>Dev login — this widget only works on dev environments.</p>
```

### Usage

```
https://dev.yourapp.com/dev/login?user=admin@example.com&returnurl=/dashboard
https://dev.yourapp.com/dev/login?user=editor@example.com&returnurl=/content
https://dev.yourapp.com/dev/login?user=admin@example.com&env=prod&returnurl=/dashboard
```

You can extend this with a `role` parameter that maps to predefined test accounts, IP allowlisting for CI runners, or whatever your project needs. The pattern above is the minimal working version — the Sitefinity auth calls are the part that's hard to figure out from the docs.

---

## 12. Build and Verify

```bash
cd ResourcePackages/MyTheme
npm run build
```

### Expected output

```
assets/dist/vue3/
├── vue3-runtime.js              ← Entry (imports + scanner)
├── vue3-runtime.css             ← All CSS (single file)
└── chunks/
    ├── vendor-vue-a1b2c3.js     ← Vue 3 + Reka UI (~45 KB gzip)
    ├── shadcn-ui-d4e5f6.js      ← shadcn-vue components
    ├── Faq-7g8h9i.js            ← FAQ widget chunk
    └── common-j0k1l2.js         ← Shared code (if 2+ widgets share modules)
```

### Verify in the browser

1. Navigate to a page with the FAQ widget
2. Open DevTools > Network tab
3. You should see:
   - `vue3-runtime.js` (entry)
   - `vendor-vue-*.js` (Vue core)
   - `Faq-*.js` (FAQ widget chunk)
4. You should **not** see chunks for widgets that aren't on the page

### Verify design mode

1. Open the page in Sitefinity's page editor
2. Drag the "FAQ" widget onto the page
3. It should show "Click to add content" (because `IsEmpty` is true)
4. Edit the widget, add FAQ items, save
5. The widget should render immediately without a page refresh

---

## 13. Vite Dev Server with HMR

> **This is arguably the single biggest developer experience win of this entire stack.** Sitefinity pages take 5-15 seconds to load, and an app pool restart after a C# build takes 30-90 seconds. Without HMR, every CSS tweak or template change means waiting through that cycle. With HMR, Vue template and style changes appear in the browser in milliseconds — the page stays loaded, component state is preserved, and you never wait for IIS. If you only set up one "nice to have" from this guide, make it this.

For development, you can run the Vite dev server alongside Sitefinity for instant hot module replacement — change a `.vue` file and see it update in the browser without a full refresh.

### How it works

1. Sitefinity serves the HTML page (with widget mount points)
2. `Vue3.cshtml` detects dev mode and points `<script type="module">` at the Vite dev server
3. Vite serves the Vue/TypeScript source directly (no build step)
4. Changes to `.vue` files trigger HMR updates via WebSocket
5. Changes to `.cshtml` files trigger a full page reload (via the `cshtmlReloadPlugin`)

### Setup

**Option A: Same machine — localhost (simplest)**

If both Sitefinity (IIS) and your browser run on the same Windows machine, no extra config is needed. The `Vue3.cshtml` dev block already points at `https://localhost:5173`. Just run `npm run dev:hmr` and you're done.

**Option B: Custom dev domain (same machine)**

If your dev site uses a custom hostname (e.g., `dev.yourapp.com` in your hosts file), you need:

1. A hostname for the Vite server that the browser can reach (e.g., `vite.yourapp.com` → `127.0.0.1`)
2. An SSL certificate for that hostname (browsers block mixed HTTP/HTTPS content)
3. Update `vite.config.ts` server section and `Vue3.cshtml` dev URLs to match

**Option C: Cross-machine / VM setup (e.g., macOS host + Windows VM)**

This applies if you run IIS/Sitefinity inside a VM (Parallels, VMware, Hyper-V) or on a remote machine, but browse from the host OS. The browser and the Vite dev server are on different machines (or different OS layers), so `localhost` doesn't resolve to the same place.

You need:
1. A hostname for the Vite server that resolves to the VM's IP from the host browser (e.g., `vite.yourapp.com` → VM's IP in the host's `/etc/hosts`)
2. An SSL cert trusted by the host browser for that hostname (self-signed is fine for dev)
3. `host: '0.0.0.0'` so Vite binds to all interfaces (not just loopback)
4. Explicit `hmr.host` so the WebSocket connects to the right place

```typescript
import { readFileSync, existsSync } from 'node:fs'

// Self-signed cert for the Vite dev hostname
const certPath = resolve(__dirname, 'vite-dev.crt')
const keyPath = resolve(__dirname, 'vite-dev.key')
const devHttps = existsSync(certPath) && existsSync(keyPath)
  ? { cert: readFileSync(certPath), key: readFileSync(keyPath) }
  : undefined

// In defineConfig:
server: {
  host: '0.0.0.0',        // Bind to all interfaces (reachable from host OS)
  port: 5173,
  strictPort: true,
  https: devHttps,
  hmr: {
    protocol: 'wss',
    host: 'vite.yourapp.com',  // Hostname the HOST browser connects to
    port: 5173
  }
}
```

5. Update the Vite dev URLs in `Vue3.cshtml` to use `vite.yourapp.com:5173` instead of `localhost:5173`

> **Why this exists:** This setup was originally built on a macOS machine running IIS/Sitefinity inside Parallels (Windows VM). The browser lives on macOS, IIS lives in Windows, and Vite runs in Windows alongside IIS. `localhost:5173` from macOS Safari hits nothing — you need a resolvable hostname that crosses the VM boundary. If you run everything on one Windows machine, Option A or B is all you need.

### Running

```bash
# Terminal 1: Vite dev server (HMR)
npm run dev:hmr

# Terminal 2: Watch build (for production testing)
npm run build:watch

# Or both at once:
npm run dev
```

---

## 14. Code Splitting Deep Dive

### How Rolldown's code splitting works

Vite 8 uses **Rolldown** (a Rust-based bundler) instead of Rollup. When it encounters a dynamic `import()`, it creates a **chunk boundary** — the imported module and its dependencies become a separate file.

```typescript
// This import() tells Rolldown: "put the FAQ widget in its own chunk"
'[data-widget="faq"]': () => import('../widgets/Mvc/Views/Faq')
```

The `codeSplitting.groups` configuration then extracts shared dependencies:

| Group | What it catches | Why it's separate |
|-------|-----------------|-------------------|
| `vendor-vue` | `vue`, `@vue/*`, `reka-ui` | Rarely changes — stays cached for weeks |
| `shadcn-ui` | `components/ui/*` | Changes occasionally — cached between widget updates |
| `common` | Code shared by 2+ widgets | Prevents duplication across widget chunks |

### The toggle

To disable code splitting and inline everything into a single file:

```typescript
// vite.config.ts → build.rolldownOptions.output
codeSplitting: false  // One line. All import() calls are inlined.
```

With `codeSplitting: false`:
- Output is a single `vue3-runtime.js` containing everything
- No `chunks/` directory
- `scanAndMount()` still runs — `import()` resolves synchronously since the code is already in memory
- **Zero runtime behavior change** — this is your rollback switch

### Cache busting

| File | Cache strategy |
|------|---------------|
| `vue3-runtime.js` | Stable filename + `?v={hash}` querystring via `Util.FileHash()` |
| `vue3-runtime.css` | Same — stable name + querystring hash |
| `chunks/*.js` | Content hash in the filename (e.g., `Faq-7g8h9i.js`) |

When you change a widget's code → its chunk gets a new hash → the entry file's `import()` URL changes → the entry file's content hash changes → `Util.FileHash()` returns a new value → browsers download the new entry → which references the new chunk.

---

## 15. IIS Considerations

### MIME types

IIS needs to serve `.js` files with `application/javascript`. This is usually configured by default, but verify in your `web.config`:

```xml
<system.webServer>
  <staticContent>
    <remove fileExtension=".js" />
    <mimeMap fileExtension=".js" mimeType="application/javascript" />
  </staticContent>
</system.webServer>
```

### Chunk filenames

Rolldown generates clean filenames using `[a-zA-Z0-9-]` plus a hash. No colons, no special characters. IIS's 260-character path limit is not a concern — chunk names are short.

### CORS

Chunks are loaded from the same origin as the entry script (e.g., `yourapp.com/ResourcePackages/MyTheme/assets/dist/vue3/chunks/...`). No CORS headers needed in production. The `devCorsPlugin` is only for the dev server.

### After disabling code splitting (rollback)

If you toggle `codeSplitting: false` and rebuild, **delete the `chunks/` directory from the server**. IIS will happily serve stale chunk files if they're left behind, which can confuse browsers that cached the old entry file.

---

## 16. Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| **Blank page, no widgets** | Entry file not loading | Check Network tab for 404 on `vue3-runtime.js`. Verify the path in `Vue3.cshtml` matches the actual file. |
| **Widget chunks 404** | `chunks/` directory not deployed | Ensure the build output including `chunks/` is deployed to the server. |
| **`Failed to fetch dynamically imported module`** | Wrong `base` path in vite.config.ts | The `base` must match the deployment path. Check the import URL in the error message. |
| **Widgets don't mount in design mode** | MutationObserver not detecting new elements | Verify `.sfPageEditor` class exists on the body in edit mode. Check browser console for errors. |
| **Double-mounting (widgets render twice)** | Missing `data-vueMounted` guard | The `index.ts` mount function should check `el.dataset.vueMounted` before creating an app. |
| **`import declarations may only appear at top level`** | Browser loading ESM as classic script | The `<script>` tag needs `type="module"`. Check that `Vue3.cshtml` rendered correctly. Hard refresh. |
| **IIS 403/404 on chunks** | IIS request filtering or missing MIME type | Check `web.config` staticContent. Verify `.js` MIME mapping exists. |
| **Stale CSS/JS after deploy** | Browser cache | `Util.FileHash()` should return a new hash. Hard refresh or clear cache. |
| **`IsEmpty` placeholder never shows** | `[DefaultValue]` on a property checked by `IsEmpty` | Sitefinity's `[DefaultValue]` populates the property before the controller runs. Remove `[DefaultValue]` from content properties used in the `IsEmpty` check. |

---

## Appendix: File Checklist

When you're done, you should have these files:

**C# (Controls project):**
- [ ] `Code/Util.cs`
- [ ] `Mvc/Controllers/Vue3Controller.cs`
- [ ] `Mvc/Models/Vue3/Vue3WidgetModel.cs`
- [ ] `Mvc/Controllers/Faq/FaqController.cs`
- [ ] `Mvc/Controllers/DevLogin/DevLoginController.cs`

**Razor (Web project):**
- [ ] `Mvc/Views/Shared/Vue3.cshtml`
- [ ] `Mvc/Views/Faq/Faq.Default.cshtml`
- [ ] `Mvc/Views/DevLogin/Default.cshtml`

**Frontend (ResourcePackages/MyTheme/):**
- [ ] `package.json`
- [ ] `tsconfig.json`
- [ ] `components.json`
- [ ] `vite.config.ts`
- [ ] `assets/src/vue3/lib/runtime.ts`
- [ ] `assets/src/vue3/lib/widget-registry.ts`
- [ ] `assets/src/vue3/lib/sentry-vue.ts` (optional — Sentry error bridge, Section 7)
- [ ] `assets/src/vue3/lib/utils.ts`
- [ ] `assets/src/vue3/lib/styles/globals.css`
- [ ] `assets/src/vue3/env.d.ts` (Vue SFC type shim)
- [ ] `assets/src/vue3/lib/components/ui/` (shadcn-vue components)
- [ ] `assets/src/vue3/widgets/Mvc/Views/Faq/index.ts`
- [ ] `assets/src/vue3/widgets/Mvc/Views/Faq/FaqApp.vue`
- [ ] `assets/src/vue3/widgets/Mvc/Views/Faq/types.ts`

**Layout:**
- [ ] Your layout `.cshtml` includes `@Html.Partial("Vue3")` at end of body

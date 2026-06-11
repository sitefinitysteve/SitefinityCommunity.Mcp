---
name: sitefinity-debloat-repo
description: >-
  Stop committing build artifacts (bin/, AdminApp/, packages/, DB backups) in a Sitefinity /
  .NET Framework repo and turn them into reproducible build artifacts — NuGet restore from public
  nuget.org, AdminApp mirrored from its content package, custom DLLs referenced copy-local — then
  optionally purge the historical bloat from git with git-filter-repo. Use this WHENEVER a
  Sitefinity repo checks bin/ or AdminApp/ into git, when .git or a clone is huge/bloated, when
  someone wants a smaller repo or a reproducible Sitefinity build, when reconciling Sitefinity
  assembly versions after a PM-console / upgrade-tool upgrade (csproj vs packages.config vs
  web.config binding redirects), when a clean restore fails because Telerik/Sitefinity packages
  "can't be found", or when the site throws a PreAppStart "Could not load file or assembly … the
  located assembly's manifest definition does not match" YSOD. Also covers migrating off the
  legacy authenticated nuget.sitefinity.com feed to public nuget.org and vendoring delisted
  third-party packages. Sitefinity is the Telerik/Progress CMS on .NET Framework 4.x.
---

# De-bloat a Sitefinity repo (no committed `bin`)

Make `bin/`, `AdminApp/`, and `packages/` **reproducible build artifacts** instead of committed
files, then reclaim the history bloat. This was validated end-to-end on a real Sitefinity 15.4
repo: `.git` 1.67 GB → 1.08 GB, working folder 6.33 GB → 4.33 GB, and a fresh clone now restores +
builds credential-free and reproduces `bin/`/`AdminApp/` exactly.

Work in phases. **Phase 0 (make it reproducible) must fully succeed before Phase 1 (untrack)**,
which must succeed before the optional **Phase 2 (purge history)**. Verify a green build and a
running site at the end of Phase 0 — never delete/untrack an artifact you haven't proven you can
regenerate.

Companion skills: `sitefinity-cli-build` (the evolved build/test/package pipeline this process
produces) and `sitefinity-binding-doctor` (reconciling binding redirects when the YSOD appears).

## Mental model

Treat these like `node_modules` — generated, never committed:

| Path | What it is | Disposition |
|---|---|---|
| `packages/` | NuGet cache | git-ignored; `nuget restore` rebuilds it |
| `bin/` | MSBuild output | git-ignored; build copies DLLs in (copy-local) |
| `AdminApp/` | Sitefinity admin SPA, shipped as a NuGet **content** package | git-ignored; build **mirrors** it from the package |
| `obj/` `.vs/` `node_modules/` `tools/` | build/IDE/tooling caches | git-ignored |
| **`Assemblies/`** (new, **committed**) | custom DLLs with **no** NuGet source | tracked, referenced copy-local |
| **`nuget-local/`** (new, **committed**) | vendored **delisted** `.nupkg`s | tracked, used as a local folder feed |

Only source, config, `Assemblies/`, and `nuget-local/` stay in git.

## Sitefinity is on public nuget.org now (the enabler)

Progress moved **all** Sitefinity packages to public **nuget.org** and backfilled history:
`Telerik.Sitefinity.*` / `Progress.Sitefinity.*` exist there from **6.3 (2014) through current**,
and `Progress.Sitefinity.AdminApp` from **11.0+**. So any project ≥ ~11.x restores
**credential-free** — the old authenticated `https://nuget.sitefinity.com/nuget` feed is
effectively obsolete (keep only as a fallback if one specific package 404s).

**The one thing that varies per project: delisted third-party OSS packages.** Old non-Sitefinity
packages disappear from nuget.org over time. Detect them with `scripts/Check-PackagesOnNuget.ps1`
and **vendor** them into `nuget-local/`.

## The seven gotchas (read before touching anything)

1. **`packages.config` is flat and non-transitive.** `Telerik.Sitefinity.All` is a meta-package
   that pulls **nothing** on `nuget restore` — packages.config must list **every** package the
   build references. A repo often "works" only because past restores left folders on disk; a clean
   clone would fail. Rebuild it complete with `scripts/Rebuild-PackagesConfig.ps1`.
2. **A Sitefinity assembly version lives in FOUR places that must agree**, or you get a PreAppStart
   YSOD `Could not load file or assembly '…' … manifest definition does not match`:
   csproj `<Reference … Version=X>`, csproj `<HintPath>packages\…X\…</HintPath>`,
   `packages.config` `<package version="X">`, and `web.config`
   `<runtime><assemblyBinding>` `bindingRedirect newVersion="X"` — where the redirect's X is the
   **AssemblyVersion read from the manifest** of the DLL actually in `bin/` (NOT the FileVersion
   shown in Explorer: packages like Newtonsoft.Json freeze AssemblyVersion per major while
   FileVersion moves; Telerik.Sitefinity assemblies happen to keep the two in sync, which hides the
   distinction until a third-party package bites you). The `sitefinity-binding-doctor` skill
   automates this reconciliation. PM-console/upgrade-tool upgrades routinely leave these split
   (e.g. Feather `Frontend.*` satellites stuck a minor version behind core).
3. **AdminApp is NuGet *content*** (`Progress.Sitefinity.AdminApp` → `content/AdminApp/`).
   packages.config copies content only at **Install/Update-Package time, NOT on restore** — so a
   fresh clone won't repopulate it. Add a build step that **mirrors** it from the restored package
   (which also prunes the stale hash-named Angular bundles that pile up each upgrade).
4. **`*.nupkg` is almost always git-ignored**, which silently swallows your vendored packages — add
   a negation so `nuget-local/` is tracked.
5. **History-rewrite + auto-fetch trap.** After you purge history and force-push, Visual Studio's
   auto-fetch re-pulls the OLD history from `origin` and re-bloats local `.git`. The only durable
   fix is to force-push the rewrite to origin for **every branch** that shares the old history (not
   just `master`), then prune.
6. **Don't force-uniform versions.** Sitefinity ships mixed (`Telerik.DataAccess`/`OpenAccess`
   ~`2020.x`, `Telerik.Web.UI` ~`2026.x` while core is `15.4.x`). Match what's in `bin/`; only fix
   genuinely-stale refs.
7. **`App_Data/Storage/FileSystem/` is usually *tracked* dev media** (a gitignore rule added later
   only stops *new* files; ones committed before stay tracked). That's the site's images/uploads —
   do **not** purge it from history unless you have another way to provision dev media. Same for
   `App_Data/GeoLocation/*.mmdb` (MaxMind, tracked + used).

## Phase 0 — Make the build reproducible (BEFORE untracking)

1. **Identify custom (no-NuGet) DLLs:** in the csproj, find `<Reference>` whose `<HintPath>` points
   into `bin\` (not `packages\`): `Select-String .\*.csproj -Pattern '<HintPath>bin\\'`.
2. **Move them to a committed `Assemblies/`**, repoint each `<HintPath>bin\X.dll` →
   `<HintPath>Assemblies\X.dll`, and add `<Private>True</Private>` (explicit copy-local). Optional:
   add an `Assemblies` solution folder to the `.sln`.

   **If SATELLITE projects HintPath into the site project's bin** (`..\YourSite\bin\X.dll`), a
   clean clone has a chicken-and-egg: the site project builds LAST, so nothing has populated its
   bin when the satellites compile. Either repoint those references to `packages\`/`Assemblies\`
   (invasive on a big solution), or add a **hydrate-bin** pre-compile step: a committed JSON
   manifest of `{ Origin, RelPath, Sha256 }` entries (Origin = repo-relative source under
   `packages\` or `Assemblies\`; RelPath = destination inside the site bin) plus a small script
   that copies each entry into the site bin between restore and msbuild, failing on missing
   sources or hash mismatches. Generate the manifest once from a known-good bin and commit it.
   The solution's OWN build outputs stay out of the manifest — the build produces those. See the
   `sitefinity-cli-build` skill for the evolved build.ps1 that wires this in.
3. **Rebuild `packages.config` complete** from the csproj's HintPaths (gotcha #1):
   `pwsh scripts/Rebuild-PackagesConfig.ps1 -Csproj .\YourProject.csproj -WhatIf` to preview, then
   without `-WhatIf` to write. Then **add `Progress.Sitefinity.AdminApp`** at your Sitefinity
   version (content-only → it has no HintPath, so the script can't infer it).
4. **Reconcile stale versions** across the four places (gotcha #2). For each stale family, first
   confirm every occurrence of the old version belongs to that family
   (`Select-String csproj -Pattern '<oldver>' | ? { $_.Line -notmatch '<Family>' }` → must be 0),
   then blanket-replace old→new (encoding-preserving) in **csproj**, **packages.config**, and
   **web.config**. Watch for Reference-vs-Import splits (point the `<HintPath>` at the version whose
   `.targets` is `<Import>`ed).
5. **Find + vendor delisted packages:** `pwsh scripts/Check-PackagesOnNuget.ps1` → for each flagged
   one, `mkdir nuget-local` and copy its `.nupkg` from `packages\<Id>.<Version>\` into `nuget-local/`.
6. **`nuget.config`** — public nuget.org + the local vendor feed, no credentials:
   ```xml
   <packageSources>
     <clear />
     <add key="nuget.org"    value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
     <add key="local-vendor" value="./nuget-local" />
   </packageSources>
   ```
   (keep `<config><add key="repositoryPath" value="./packages" /></config>` + `<packageRestore enabled=True>`).
7. **`build.ps1`** — restore (packages.config needs `nuget.exe restore`, not `msbuild -t:restore`)
   + AdminApp mirror, before MSBuild. nuget.exe self-bootstraps into git-ignored `.tools/`:
   ```powershell
   if (-not $SkipRestore) {
       $nuget = "$PSScriptRoot\.tools\nuget.exe"
       if (-not (Test-Path $nuget)) {
           New-Item -ItemType Directory -Force (Split-Path $nuget) | Out-Null
           Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget }
       & $nuget restore YourProject.sln; if ($LASTEXITCODE -ne 0) { Write-Error 'restore failed'; exit 1 }
       # AdminApp is content; restore doesn't copy it. Mirror it (version read from packages.config).
       $aaVer = ([xml](Get-Content "$PSScriptRoot\packages.config")).packages.package |
           Where-Object { $_.id -eq 'Progress.Sitefinity.AdminApp' } | Select-Object -Expand version
       if ($aaVer) { $src = "$PSScriptRoot\packages\Progress.Sitefinity.AdminApp.$aaVer\content\AdminApp"
           if (Test-Path $src) { & robocopy $src "$PSScriptRoot\AdminApp" /MIR /NJH /NJS /NFL /NDL | Out-Null
               if ($LASTEXITCODE -ge 8) { Write-Error 'AdminApp mirror failed'; exit 1 }; $global:LASTEXITCODE = 0 } }
   }
   ```
8. **Verify:** run the build (e.g. `npm run msbuild` or `build.ps1`) — restore is credential-free,
   build is green. Load the site: home **and** `/Sitefinity` **and** `/AdminApp/index.html` return
   200, no YSOD. Prove AdminApp reproducibility for real: delete `AdminApp/`, rebuild, confirm it
   returns and is served 200.

## Phase 1 — Untrack the artifacts

**Hard rule: custom DLLs out to `Assemblies/` and a green build FIRST, then untrack `bin/`.**

`.gitignore`:
```gitignore
/bin/
/AdminApp/
/.tools/
**/packages/*
!/nuget-local/
!/nuget-local/*.nupkg        # survive the *.nupkg ignore
*.bak
*.mdf
*.ldf
*.trn
```
Then (files stay on disk; the running site loads from `bin/`, so no downtime):
```powershell
git rm -r --cached bin AdminApp
git add .gitignore *.csproj *.sln packages.config nuget.config build.ps1 web.config Assemblies nuget-local
git commit -m "Make bin/ and AdminApp reproducible build artifacts; stop tracking them"
```
Git records the custom DLLs as renames `bin/ → Assemblies/` — preserved, good.

## Phase 2 — Purge the history (optional; reclaims `.git`)

Needs `git-filter-repo` (Python): `pip install git-filter-repo`, or fetch the standalone script to
`.tools\git-filter-repo`. **Destructive + rewrites every commit SHA → force-push + re-clone.**

1. **Back up:** `git clone --no-hardlinks --mirror . ..\PROJ-backup.git` (independent copy).
2. **Find DB-backup paths** (don't blanket-glob `*.bak` — it can hit a *tracked* script/csproj
   backup): `git rev-list --all --objects | Select-String '\.(bak|mdf|ldf)$'`.
3. **Purge** the real targets (scope precisely to the paths step 2 found, e.g. a DB-dump folder
   like `App_Data/<your-db-backup-dir>` — not all `*.bak`):
   ```
   python .tools\git-filter-repo --force --invert-paths --path bin --path AdminApp --path App_Data/<your-db-backup-dir>
   ```
   filter-repo removes the `origin` remote as a safety measure — re-add it next.
4. **Force-push EVERY branch** that shares the old history (gotcha #5), not just master:
   ```powershell
   git remote add origin <url>
   git push --force-with-lease origin master
   git push --force-with-lease origin <each-other-branch>
   git remote set-head origin -a ; git fetch --prune origin
   ```
   Before force-pushing a feature branch, confirm parity is safe: same commit subjects, 0 purged
   objects, only emptied artifact commits dropped (compare `rev-list --count` local vs origin).
5. **Prune:** `git reflog expire --expire=now --all ; git gc --prune=now`. Verify
   `git rev-list --all --objects` shows **0** purged paths and `.git` is small. If it re-grows, a
   ref (usually `refs/remotes/origin/*`) still points at old history — check `git for-each-ref`.

## Disk vs git size (set expectations)

- Shrinking `.git` ≠ shrinking the **working folder** — that's mostly git-ignored regenerable
  caches. `packages/` accumulates a folder per restore forever; `rm -rf packages; nuget restore`
  rebuilds it lean with **no downtime** (the site loads from `bin/`, not `packages/`).
- `bin/`, `obj/`, `AdminApp/` regenerate to the **same** size — deleting them reclaims nothing
  permanent. `node_modules/` comes back via `npm install` (not msbuild).
- `bin/` DLLs are locked by `w3wp` while the site runs — stop the app pool / close VS before
  deleting `bin/`.

## Per-project checklist

- [ ] Sitefinity version (FileVersion of `bin/Telerik.Sitefinity.dll`); confirm on nuget.org.
- [ ] Custom (`bin\` HintPath) DLLs → `Assemblies/`, copy-local.
- [ ] `Rebuild-PackagesConfig.ps1`; add `Progress.Sitefinity.AdminApp`.
- [ ] Reconcile stale versions across csproj / packages.config / web.config / bin.
- [ ] `Check-PackagesOnNuget.ps1` → vendor delisted into `nuget-local/` (+ gitignore negation).
- [ ] `nuget.config` (nuget.org + ./nuget-local); `build.ps1` (restore + AdminApp mirror).
- [ ] Green build; home + `/Sitefinity` + `/AdminApp` = 200; delete-AdminApp-and-rebuild works.
- [ ] gitignore artifacts; `git rm --cached bin AdminApp`; commit.
- [ ] (Optional) Phase 2: backup → filter-repo → force-push **all** branches → prune.
- [ ] Leave `App_Data/Storage/FileSystem` + `GeoLocation` alone (tracked dev media).

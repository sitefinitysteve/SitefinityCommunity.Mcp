---
name: sitefinity-cli-build
description: Use this skill to build, test, and package a classic Sitefinity (.NET Framework 4.8) solution entirely from the command line - no Visual Studio IDE session required. Covers the npm-wrapped build.ps1 (VS dev shell + NuGet restore + msbuild), test.ps1 (vstest.console with filters and trx logging), and a Node-based deployment bundler that packages only recently changed files into a zip with a manifest README.
---

You are setting up or using CLI build/test/package tooling for a classic Sitefinity site (.NET Framework 4.8, msbuild-based - NOT `dotnet build`, which cannot build these projects). The pattern: thin PowerShell/Node scripts at the repo root, all exposed as **npm scripts** so humans and AI agents share one interface (`npm run build`, `npm test`, `npm run copy:today`) without anyone needing Visual Studio open.

## Why npm as the front door

- One memorable surface: `npm run <thing>` works the same for a developer, a CI runner, and a coding agent.
- The scripts encode the non-obvious parts once (VS dev shell init, restore flags, filter syntax) so nobody retypes them.
- `.NET Framework 4.8` web projects require **msbuild**, not the dotnet CLI - wrapping hides that footgun.

### Root package.json script surface

```json
{
  "scripts": {
    "build":         "powershell.exe -ExecutionPolicy Bypass -File build.ps1",
    "build:debug":   "powershell.exe -ExecutionPolicy Bypass -File build.ps1",
    "build:release": "powershell.exe -ExecutionPolicy Bypass -File build.ps1 -Configuration Release",

    "test":          "powershell.exe -ExecutionPolicy Bypass -File test.ps1 -Filter TestCategory=Core",
    "test:core":     "powershell.exe -ExecutionPolicy Bypass -File test.ps1 -Filter TestCategory=Core",
    "test:filter":   "powershell.exe -ExecutionPolicy Bypass -File test.ps1 -Filter",
    "test:all":      "powershell.exe -ExecutionPolicy Bypass -File test.ps1",

    "copy":          "cd SitefinityWeb && npm run copy",
    "copy:today":    "cd SitefinityWeb && npm run copy:today",
    "copy:week":     "cd SitefinityWeb && npm run copy:week",
    "copy:days:3":   "cd SitefinityWeb && npm run copy:days:3"
  }
}
```

Usage notes:
- `npm run test:filter "TestCategory=StreamEditor"` - the argument lands after `-Filter`. Filter syntax is vstest's: `TestCategory=X`, `FullyQualifiedName~Partial`, combinable with `&`/`|`.
- Default `npm test` runs a fast curated category (e.g. `TestCategory=Core`) so the cheap path is the default; `test:all` is the slow full run.

## build.ps1 - clean-clone-safe solution build

```powershell
# Release command
# powershell.exe -ExecutionPolicy Bypass -File build.ps1 -Configuration Release
#
# Build pipeline (clean-clone safe):
#   1. Launch VS dev shell (puts msbuild on PATH)
#   2. NuGet restore (packages.config -> packages\, PackageReference -> global cache)
#   3. Optional hydrate step (see notes below)
#   4. msbuild the solution
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipRestore,
    [switch]$SkipHydrate
)
$ErrorActionPreference = 'Stop'
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition

# 1. Initialize VS environment (adds msbuild to PATH).
#    Locate via vswhere so this works across VS editions/versions;
#    a hardcoded path also works if your team standardizes on one edition:
#    & "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\Tools\Launch-VsDevShell.ps1"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
& (Join-Path $vsPath 'Common7\Tools\Launch-VsDevShell.ps1')
Set-Location $scriptPath

# 2. Restore (deterministic: explicit config + packages dir, self-bootstrapping nuget.exe)
if (-not $SkipRestore) {
    $nuget = Join-Path $scriptPath '.tools\nuget.exe'
    if (-not (Test-Path $nuget)) {
        New-Item -ItemType Directory -Force (Split-Path $nuget) | Out-Null
        Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget
    }
    Write-Host "Restoring NuGet packages..." -ForegroundColor Green
    & $nuget restore "$scriptPath\YourSolution.sln" -ConfigFile "$scriptPath\SitefinityWeb\nuget.config" -PackagesDirectory "$scriptPath\packages" -NonInteractive
    if ($LASTEXITCODE -ne 0) { Write-Error "NuGet restore failed"; exit 1 }
}

# 3. Optional: hydrate the web project's bin from packages\ + a committed Assemblies\
#    folder BEFORE compiling. Needed when satellite projects HintPath into
#    ..\SitefinityWeb\bin\*.dll and bin is NOT committed: the web project builds
#    LAST, so on a clean clone nothing has populated bin yet. The hydrate script
#    copies the restored third-party deps in first so those HintPaths resolve.
#    (Skip this whole step if your projects reference packages\ directly.)
if (-not $SkipHydrate) {
    & pwsh -NoProfile -ExecutionPolicy Bypass -File "$scriptPath\build\hydrate-bin.ps1" -Root $scriptPath
    if ($LASTEXITCODE -ne 0) { Write-Error "hydrate-bin failed"; exit 1 }
}

# 4. Build
Write-Host "Building YourSolution.sln in $Configuration mode..." -ForegroundColor Green
& msbuild YourSolution.sln /p:Configuration=$Configuration /verbosity:minimal /nologo
if ($LASTEXITCODE -ne 0) { Write-Error "msbuild failed"; exit 1 }
```

Key decisions:
- **`Launch-VsDevShell.ps1`** is what puts `msbuild` and `vstest.console.exe` on PATH. Without it the script only works in a Developer PowerShell.
- **Self-bootstrapping `nuget.exe`** into a gitignored `.tools\` folder - clean clones need zero preinstalled tooling beyond VS Build Tools + Node.
- **Explicit `-ConfigFile` + `-PackagesDirectory`** makes restore deterministic regardless of machine-level NuGet config. Sitefinity sites are typically `packages.config`-era, so the restore target is the solution-level `packages\` folder.
- `-SkipRestore` / `-SkipHydrate` keep inner-loop rebuilds fast.

## test.ps1 - vstest.console with filters, trx logs, failed-only reruns

```powershell
<#
.SYNOPSIS
    Runs MSTest tests for the solution from the CLI.
.PARAMETER Filter
    vstest filter expression: "TestCategory=Unit", "FullyQualifiedName~Login", ...
.PARAMETER TestProject
    Test assembly pattern. Default: *.Test.dll
.PARAMETER ListTests
    List available tests without running them.
.PARAMETER FailedOnly
    Re-run only previously failed tests (uses the saved trx from the last run).
#>
param(
    [string]$Filter = "",
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$TestProject = "*.Test.dll",
    [switch]$ListTests,
    [switch]$FailedOnly,
    [switch]$Verbose
)

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Initialize VS environment (puts vstest.console.exe on PATH; locate via vswhere as in build.ps1)
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$vsPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
& (Join-Path $vsPath 'Common7\Tools\Launch-VsDevShell.ps1') | Out-Null
Set-Location $scriptPath

# TestResults dir; keep the 5 most recent trx files
$testResultsDir = Join-Path $scriptPath "TestResults"
if (!(Test-Path $testResultsDir)) { New-Item -ItemType Directory -Path $testResultsDir | Out-Null }
Get-ChildItem -Path $testResultsDir -Filter "*.trx" |
    Sort-Object CreationTime -Descending | Select-Object -Skip 5 | Remove-Item -Force

# Find built test assemblies (requires a prior build in this Configuration)
$testAssemblies = Get-ChildItem -Path $scriptPath -Recurse -Filter $TestProject |
                  Where-Object {
                      $_.DirectoryName -like "*bin\$Configuration*" -and
                      $_.DirectoryName -notlike "*obj*" -and
                      $_.DirectoryName -notlike "*TestResults*"
                  }
if ($testAssemblies.Count -eq 0) {
    Write-Host "No test assemblies found matching: $TestProject. Build the solution first." -ForegroundColor Red
    exit 1
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
foreach ($assembly in $testAssemblies) {
    Write-Host "`nTesting: $($assembly.Name)" -ForegroundColor Yellow
    $args = @($assembly.FullName)

    # Optional .runsettings next to your test project
    $runSettingsPath = Join-Path $scriptPath "YourSolution.Test\.runsettings"
    if (Test-Path $runSettingsPath) { $args += "/Settings:$runSettingsPath" }

    if ($ListTests) {
        $args += "/ListTests"
    } else {
        if ($Filter) { $args += "/TestCaseFilter:$Filter" }
        if ($FailedOnly -and (Test-Path "$testResultsDir\$([IO.Path]::GetFileNameWithoutExtension($assembly.Name))_last.trx")) {
            $args += "/TestCaseFilter:Outcome=Failed"
        }
        $logName = "$([IO.Path]::GetFileNameWithoutExtension($assembly.Name))_$timestamp"
        $args += "/Logger:trx;LogFileName=$logName.trx"
        $args += "/ResultsDirectory:$testResultsDir"
        $args += if ($Verbose) { "/Logger:console;verbosity=detailed" } else { "/Logger:console;verbosity=normal" }
    }

    & vstest.console.exe $args

    # Save a stable "_last.trx" copy per assembly for -FailedOnly reruns
    if (!$ListTests) {
        $logName = "$([IO.Path]::GetFileNameWithoutExtension($assembly.Name))_$timestamp"
        if (Test-Path "$testResultsDir\$logName.trx") {
            Copy-Item "$testResultsDir\$logName.trx" "$testResultsDir\$([IO.Path]::GetFileNameWithoutExtension($assembly.Name))_last.trx" -Force
        }
    }
}
```

Why it's shaped this way:
- **Discovers built assemblies** under `bin\$Configuration` rather than hardcoding project names - new test projects join automatically.
- **trx logging with rotation** (keep 5) gives an agent or CI something parseable; console verbosity=normal streams pass/fail in real time.
- **`-FailedOnly`** is the fast inner loop after a red run; **`-ListTests`** answers "what tests exist" without executing.
- Filters compose: `.\test.ps1 -Filter "TestCategory=StreamEditor&FullyQualifiedName~Empty"`.

## copy-files.js - "what changed recently" deployment bundler

For sites deployed by file-drop (no full CI/CD pipeline), this Node script assembles a deploy package containing ONLY what changed in a chosen window, zips it, and writes a README manifest of every included file. Lives in the web project folder; wrapped by npm:

```json
{
  "scripts": {
    "copy":           "node copy-files.js today MyTheme",
    "copy:today":     "node copy-files.js today MyTheme",
    "copy:week":      "node copy-files.js week MyTheme",
    "copy:month":     "node copy-files.js month MyTheme",
    "copy:thisweek":  "node copy-files.js thisweek MyTheme",
    "copy:thismonth": "node copy-files.js thismonth MyTheme",
    "copy:days:3":    "node copy-files.js days:3 MyTheme"
  }
}
```

Timeframe arg: `today` | `week` (rolling 7d) | `month` (rolling 30d) | `thisweek` (since Sunday) | `thismonth` (since the 1st) | `days:N` | `all`. Second arg = theme name (which `ResourcePackages/{Theme}` to include).

### What it packages (the contract)

| Area | Inclusion rule |
|---|---|
| `Mvc/Views/*` (root + theme) | **Folder-level granularity**: if ANY `.cshtml`/`.js` in a widget's view folder is recent, the WHOLE folder ships. Sitefinity views depend on sibling Resources files, so partial folders break widgets. |
| `bin/*` | Per-file: recent mtime OR filename contains the theme name (theme assemblies always ship) OR **content-hash changed since the last deploy** (see manifest below). |
| `Sitefinity/Temp/*`, `include/*` | Per-file mtime. |
| `ResourcePackages/{Theme}/*` | Theme `Mvc/Views` uses folder-level rule; everything else per-file mtime; `src/**` and `node_modules/**` NEVER ship (built artifacts in `dist/` do). |
| `*.html` | Excluded everywhere (grid/template sources that must not be overwritten on the server). |

Output: `../deploy/` is wiped and rebuilt, empty dirs pruned, view folders containing only CSS Resources dropped, then a `README.md` manifest (every file with source path + mtime, grouped by folder) and a timestamped zip (`mytheme-deploy-YYYY-MM-DD-h-mmam.zip`). Optionally the zip is also copied to a shared drop folder for the person doing the server deploy - make that path a config value, and treat its failure as a warning, not an error.

### The bin content-hash manifest (the clever part)

mtime lies: NuGet restore preserves package timestamps, `git restore` and clean rebuilds reset them. So a DLL whose CONTENT changed can look "old", and an unchanged DLL can look "new". The bundler keeps a `.bin-deploy-manifest.json` (gitignored) of SHA-256 hashes per bin file from the LAST deploy and includes any file whose hash differs - regardless of mtime. First run bootstraps the manifest (no diff); subsequent runs diff for free.

```javascript
// bin-manifest.js
import fs from 'fs-extra';
import crypto from 'crypto';
import path from 'path';
import { glob } from 'glob';

const MANIFEST_FILENAME = '.bin-deploy-manifest.json';

export function hashFileSync(filepath) {
  return crypto.createHash('sha256').update(fs.readFileSync(filepath)).digest('hex');
}

export function loadBinManifest() {
  if (!fs.existsSync(MANIFEST_FILENAME)) return null;
  try {
    return JSON.parse(fs.readFileSync(MANIFEST_FILENAME, 'utf-8'));
  } catch (err) {
    console.warn(`Could not parse ${MANIFEST_FILENAME} (${err.message}); treating as missing.`);
    return null;
  }
}

export function saveBinManifest(manifest) {
  fs.writeFileSync(MANIFEST_FILENAME, JSON.stringify(manifest, null, 2));
}
```

### Condensed copy-files.js

(Trimmed of verbose debug logging; same behavior. Dependencies: `fs-extra`, `glob`, `archiver`.)

```javascript
import fs from 'fs-extra';
import path from 'path';
import { glob } from 'glob';
import archiver from 'archiver';
import { loadBinManifest, saveBinManifest, hashFileSync } from './bin-manifest.js';

// --- Args: node copy-files.js [timeframe] [themeName] ---
const timeArg = process.argv[2] || 'today';
const themeName = process.argv[3];
if (!themeName) { console.error('Usage: node copy-files.js [timeframe] [themeName]'); process.exit(1); }

// --- Timeframe -> cutoffDate ---
let cutoffDate = new Date(); let filterByDate = true;
const t = timeArg.toLowerCase();
if (t === 'all') { filterByDate = false; }
else if (t === 'today') { cutoffDate.setHours(0, 0, 0, 0); }
else if (t === 'week') { cutoffDate.setDate(cutoffDate.getDate() - 7); }
else if (t === 'month') { cutoffDate.setDate(cutoffDate.getDate() - 30); }
else if (t === 'thisweek') { cutoffDate.setDate(cutoffDate.getDate() - cutoffDate.getDay()); cutoffDate.setHours(0, 0, 0, 0); }
else if (t === 'thismonth') { cutoffDate.setDate(1); cutoffDate.setHours(0, 0, 0, 0); }
else if (/^days:\d+$/.test(t)) { cutoffDate.setDate(cutoffDate.getDate() - parseInt(t.split(':')[1], 10)); }
else { cutoffDate.setHours(0, 0, 0, 0); }

const deployDir = '../deploy';
fs.removeSync(deployDir); fs.ensureDirSync(deployDir);
const copiedFiles = [];   // { source, destination, mtime } for the README manifest

const isExcluded = f => path.extname(f).toLowerCase() === '.html';
const isRecent = f => !filterByDate || fs.statSync(f).mtime >= cutoffDate;
function record(src, dest) {
  copiedFiles.push({ source: src.replace(/\\/g, '/'), destination: dest.replace(/\\/g, '/'), mtime: fs.statSync(src).mtime });
}

// Folder-level copy: ship a whole view subfolder if ANY .cshtml/.js in it is recent.
async function copyViewDirsConditionally(srcDir, destDir) {
  if (!fs.existsSync(srcDir)) return;
  const subDirs = fs.readdirSync(srcDir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name);
  for (const sub of subDirs) {
    const srcSub = path.join(srcDir, sub), destSub = path.join(destDir, sub);
    let shouldCopy = !filterByDate;
    if (!shouldCopy) {
      const candidates = await glob(`${srcSub.replace(/\\/g, '/')}/**/*.+(cshtml|js)`, { nodir: true, absolute: true });
      shouldCopy = candidates.some(f => fs.statSync(f).mtime >= cutoffDate);
    }
    if (!shouldCopy) continue;
    fs.copySync(srcSub, destSub, { overwrite: true, filter: f => !isExcluded(f) });
    const all = await glob(`${srcSub.replace(/\\/g, '/')}/**/*`, { nodir: true, absolute: true, ignore: ['**/*.html'] });
    for (const f of all) record(f, path.join(destSub, path.relative(srcSub, f)));
  }
}

// bin: mtime OR theme-named OR content-hash changed since last deploy.
async function copyBinFiles(srcDir, destDir) {
  if (!fs.existsSync(srcDir)) return;
  const previous = loadBinManifest();           // null on first run (bootstrap)
  const next = {};
  const files = await glob(`${srcDir.replace(/\\/g, '/')}/**/*`, { nodir: true, absolute: true });
  for (const f of files) {
    const relKey = path.relative(srcDir, f).replace(/\\/g, '/');
    const hash = hashFileSync(f);
    next[relKey] = hash;
    const themeSpecific = path.basename(f).includes(themeName);
    const hashChanged = previous !== null && previous[relKey] !== hash;
    if ((!filterByDate || isRecent(f) || themeSpecific || hashChanged) && !isExcluded(f)) {
      const dest = path.join(destDir, relKey);
      fs.ensureDirSync(path.dirname(dest)); fs.copySync(f, dest, { overwrite: true });
      record(f, dest);
    }
  }
  saveBinManifest(next);
}

// Plain per-file mtime copy for a directory tree.
async function copyByMtime(srcDir, destDir, ignore = []) {
  if (!fs.existsSync(srcDir)) return;
  const files = await glob(`${srcDir.replace(/\\/g, '/')}/**/*`, { nodir: true, absolute: true, ignore });
  for (const f of files) {
    if (isExcluded(f) || !isRecent(f)) continue;
    const dest = path.join(destDir, path.relative(srcDir, f));
    fs.ensureDirSync(path.dirname(dest)); fs.copySync(f, dest, { overwrite: true });
    record(f, dest);
  }
}

// Drop view folders whose only content is a Resources dir of pure CSS (noise).
function cleanupCssOnlyFolders(viewsDir) {
  if (!fs.existsSync(viewsDir)) return;
  for (const folder of fs.readdirSync(viewsDir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name)) {
    const fp = path.join(viewsDir, folder);
    const contents = fs.readdirSync(fp);
    if (contents.length === 1 && contents[0] === 'Resources') {
      const res = fs.readdirSync(path.join(fp, 'Resources'));
      if (res.length > 0 && res.every(x => path.extname(x).toLowerCase() === '.css')) {
        fs.removeSync(fp);
        const norm = fp.replace(/\\/g, '/');
        for (let i = copiedFiles.length - 1; i >= 0; i--) {
          if (copiedFiles[i].destination.startsWith(norm + '/')) copiedFiles.splice(i, 1);
        }
      }
    }
  }
}

function removeEmptyDirs(dir) {
  if (!fs.existsSync(dir)) return;
  for (const item of fs.readdirSync(dir)) {
    const p = path.join(dir, item);
    if (fs.statSync(p).isDirectory()) {
      removeEmptyDirs(p);
      if (fs.readdirSync(p).length === 0) fs.rmdirSync(p);
    }
  }
}

function generateReadme(readmePath) {
  let md = `# ${themeName} Deployment Package\n\nGenerated: ${new Date().toLocaleString()}\n` +
           `Time filter: ${timeArg}${filterByDate ? ` (since ${cutoffDate.toLocaleString()})` : ' (all files)'}\n\n` +
           `## Files Included (${copiedFiles.length})\n\n`;
  const byDir = {};
  for (const f of copiedFiles) {
    const d = path.dirname(path.relative(deployDir, f.destination)).replace(/\\/g, '/') || '/';
    (byDir[d] ??= []).push(f);
  }
  for (const dir of Object.keys(byDir).sort()) {
    md += `### ${dir}\n\n`;
    for (const f of byDir[dir].sort((a, b) => a.destination.localeCompare(b.destination))) {
      md += `- \`${path.basename(f.destination)}\` (from \`${f.source}\`, modified: ${f.mtime.toLocaleString()})\n`;
    }
    md += '\n';
  }
  fs.writeFileSync(readmePath, md);
}

function createZip(outputFilePath, subDirs) {
  return new Promise((resolve, reject) => {
    const output = fs.createWriteStream(outputFilePath);
    const archive = archiver('zip', { zlib: { level: 9 } });
    output.on('close', resolve); archive.on('error', reject);
    archive.pipe(output);
    (async () => {
      for (const dir of subDirs) {
        const full = path.join(deployDir, dir);
        const files = await glob(`${full.replace(/\\/g, '/')}/**/*`, { nodir: true, absolute: true, ignore: ['**/*.html'] });
        for (const f of files) archive.file(f, { name: path.join(dir, path.relative(full, f)).replace(/\\/g, '/') });
      }
      archive.finalize();
    })().catch(reject);
  });
}

async function main() {
  await copyViewDirsConditionally('Mvc/Views', path.join(deployDir, 'Mvc/Views'));
  await copyBinFiles('bin', path.join(deployDir, 'bin'));
  await copyByMtime(path.join('Sitefinity', 'Temp'), path.join(deployDir, 'Sitefinity', 'Temp'));
  await copyByMtime('include', path.join(deployDir, 'include'));

  // Theme: views folder-level, everything else per-file; never ship src/ or node_modules/
  const themeSrc = `ResourcePackages/${themeName}`, themeDest = path.join(deployDir, 'ResourcePackages', themeName);
  await copyViewDirsConditionally(path.join(themeSrc, 'Mvc', 'Views'), path.join(themeDest, 'Mvc', 'Views'));
  await copyByMtime(themeSrc, themeDest, [
    `${themeSrc}/Mvc/Views/**/*`, `${themeSrc}/**/src/**`, `${themeSrc}/**/node_modules/**`, `${themeSrc}/node_modules/**`
  ]);

  cleanupCssOnlyFolders(path.join(deployDir, 'Mvc/Views'));
  cleanupCssOnlyFolders(path.join(themeDest, 'Mvc', 'Views'));
  if (copiedFiles.length === 0) { console.log('Nothing changed in window - no package created.'); return; }
  generateReadme(path.join(deployDir, 'README.md'));
  removeEmptyDirs(deployDir);

  const now = new Date();
  const h12 = now.getHours() % 12 || 12;
  const stamp = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}-${h12}-${String(now.getMinutes()).padStart(2, '0')}${now.getHours() >= 12 ? 'pm' : 'am'}`;
  const zipName = `${themeName.toLowerCase()}-deploy-${stamp}.zip`;
  const subDirs = fs.readdirSync(deployDir, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name);
  await createZip(path.join(deployDir, zipName), subDirs);
  console.log(`Created ${zipName} with ${copiedFiles.length} files.`);

  // Optional: also copy the zip to a shared drop folder for whoever deploys.
  // Treat failure as a warning - the package still exists in deploy/.
  const dropFolder = process.env.DEPLOY_DROP_FOLDER;
  if (dropFolder) {
    try { fs.ensureDirSync(dropFolder); fs.copySync(path.join(deployDir, zipName), path.join(dropFolder, zipName), { overwrite: true }); }
    catch (e) { console.warn(`Could not copy zip to drop folder: ${e.message}`); }
  }
}

main().catch(err => { console.error('FATAL:', err); process.exit(1); });
```

## Gotchas learned the hard way

- **Folder-level view copying is deliberate.** A Sitefinity widget view folder is a unit (`.cshtml` + `Resources/*.js` + partials). Shipping only the changed file inside it has broken widgets when a sibling resource also changed but missed the window by minutes.
- **mtime is not truth for bin/.** NuGet preserves package timestamps and git operations reset them - the SHA-256 manifest diff is what makes `copy:today` safe after a restore or clean rebuild. The manifest file is per-machine state: gitignore it.
- **`.html` exclusion** protects server-side grid/layout template files from being clobbered by stale local copies.
- **The deploy/ folder is transient output** - gitignore it, never read app state from it.
- **Agents and CLAUDE.md**: document the npm commands (and that `dotnet build` does NOT work on .NET Framework Sitefinity projects) in your agent instructions, so the wrapped scripts are the only path anyone takes.

---
name: sitefinity-binding-doctor
description: Use this skill to diagnose and fix .NET assembly binding errors on a Sitefinity site - YSODs like "Could not load file or assembly" or "The located assembly's manifest definition does not match the assembly reference". Inventories the bin folder's actual assembly versions, reconciles them against web.config bindingRedirects, checks the live site for binding YSODs, and iterates the fix loop until the site boots clean.
---

You are an assembly-binding doctor for classic Sitefinity sites (.NET Framework 4.8). A mature Sitefinity web.config carries 200+ `<bindingRedirect>` entries, and every NuGet bump, Sitefinity upgrade, or stray DLL copy can knock one out of sync. The symptom is a yellow screen of death (YSOD) at startup; the cure is mechanical once you can SEE the mismatch. This skill makes it mechanical.

## First: classify the error (they are NOT all the same)

| Error text | Meaning | Fix class |
|---|---|---|
| `Could not load file or assembly 'X, Version=A.B.C.D' ... The located assembly's manifest definition does not match the assembly reference` | The DLL in bin has a DIFFERENT AssemblyVersion than some caller compiled against, and no redirect bridges the gap (or the redirect points at the wrong version) | Fix/add the `bindingRedirect` |
| `Could not load file or assembly 'X' ... The system cannot find the file specified` | The DLL is MISSING from bin entirely | No redirect can fix this - restore the DLL (rebuild, NuGet restore, copy from packages) |
| `Could not load file or assembly 'X' ... An attempt was made to load a program with an incorrect format` | x86/x64 bitness mismatch | App pool "Enable 32-Bit Applications" or wrong-platform DLL - not a binding-redirect problem |
| `Method not found:` / `TypeLoadException` AFTER the site boots | Redirect "fixed" the load but the API surface differs between versions | The redirect is papering over a real version conflict - align the actual package versions |

## The iron rules

1. **`newVersion` must equal the ASSEMBLY version of the DLL physically in bin** - never the file/product version you see in Explorer. Many packages freeze AssemblyVersion per major while FileVersion moves (classic: Newtonsoft.Json file 13.0.3 = assembly `13.0.0.0`). Always read the manifest, never the property sheet.
2. **`publicKeyToken` and `culture` must match exactly** (lowercase hex token). A redirect with the wrong token is silently ignored - it looks right and does nothing.
3. **`oldVersion="0.0.0.0-{newVersion}"`** is the standard catch-all range.
4. **All `Telerik.Sitefinity.*` assemblies must be the SAME version** (your Sitefinity version). If the bin inventory shows mixed Telerik.Sitefinity versions, you have a botched upgrade or a stale copy step - fix the bin contents, do NOT try to redirect your way out.
5. **Binding errors surface ONE AT A TIME.** Fixing the first reveals the second. Expect a loop, not a single fix.
6. **Editing web.config recycles the app pool**, and Sitefinity takes 30-90+ seconds to boot. A "Please wait a moment" page or a redirect to `/sitefinity/status` means STILL BOOTING - wait and re-check, don't diagnose a half-started site.

## Step 1 - Inventory what is ACTUALLY in bin

Drop this in the web project root as `getassemblyversions.ps1` (reads each manifest without loading the assembly; emits parseable `Name|AssemblyVersion|PublicKeyToken` lines):

```powershell
# Lists Name|AssemblyVersion|PublicKeyToken for every .NET assembly in .\bin
$binFolderPath = Join-Path -Path $PSScriptRoot -ChildPath "bin"
if (-not (Test-Path $binFolderPath -PathType Container)) {
    Write-Host "Error: bin folder not found at $binFolderPath" -ForegroundColor Red
    return
}

foreach ($file in (Get-ChildItem -Path $binFolderPath -Filter *.dll -File)) {
    try {
        # Reads the manifest WITHOUT loading the assembly into the AppDomain
        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        $tokenBytes = $assemblyName.GetPublicKeyToken()
        $token = if ($null -ne $tokenBytes -and $tokenBytes.Length -gt 0) {
            -join ($tokenBytes | ForEach-Object { $_.ToString('x2') })
        } else { "null" }
        Write-Output "$($assemblyName.Name)|$($assemblyName.Version)|$token"
    }
    catch {
        # Not a .NET assembly (native dll) - skip silently
    }
}
```

Run it: `powershell.exe -ExecutionPolicy Bypass -File getassemblyversions.ps1 > bin-versions.txt`

## Step 2 - Inventory what web.config CLAIMS

The redirects live in `<configuration><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">` as repeating blocks:

```xml
<dependentAssembly>
  <assemblyIdentity name="HtmlAgilityPack" publicKeyToken="bd319b19eaf3b43a" culture="neutral" />
  <bindingRedirect oldVersion="0.0.0.0-1.12.3.0" newVersion="1.12.3.0" />
</dependentAssembly>
```

PowerShell dot-notation traverses the default XML namespace without ceremony:

```powershell
$config = [xml](Get-Content "$PSScriptRoot\web.config" -Raw)
foreach ($dep in $config.configuration.runtime.assemblyBinding.dependentAssembly) {
    "$($dep.assemblyIdentity.name)|$($dep.bindingRedirect.newVersion)|$($dep.assemblyIdentity.publicKeyToken)"
}
```

## Step 3 - Reconcile (the one-shot doctor script)

Save as `check-bindings.ps1` next to web.config. It joins both inventories and reports every disagreement:

```powershell
# Reconciles bin\*.dll assembly versions against web.config bindingRedirects.
# Exit code 1 when stale redirects exist (CI-friendly).
$ErrorActionPreference = 'Stop'
$binPath = Join-Path $PSScriptRoot 'bin'
$configPath = Join-Path $PSScriptRoot 'web.config'

# 1. What's really in bin
$bin = @{}
foreach ($file in (Get-ChildItem $binPath -Filter *.dll -File)) {
    try {
        $an = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
        $tb = $an.GetPublicKeyToken()
        $bin[$an.Name] = [pscustomobject]@{
            Version = $an.Version.ToString()
            Token   = if ($tb -and $tb.Length) { -join ($tb | ForEach-Object { $_.ToString('x2') }) } else { $null }
        }
    } catch { }
}

# 2. What web.config redirects to
$config = [xml](Get-Content $configPath -Raw)
$problems = @()
foreach ($dep in $config.configuration.runtime.assemblyBinding.dependentAssembly) {
    $name = $dep.assemblyIdentity.name
    $token = $dep.assemblyIdentity.publicKeyToken
    $redirectTo = $dep.bindingRedirect.newVersion
    if (-not $bin.ContainsKey($name)) {
        # Redirect for a DLL that isn't deployed: harmless at runtime unless something
        # references it, but flag it - it usually means a removed package left debris.
        $problems += [pscustomobject]@{ Assembly = $name; Issue = 'REDIRECT_ORPHAN'; Config = $redirectTo; Bin = '(not in bin)' }
        continue
    }
    $actual = $bin[$name]
    if ($actual.Version -ne $redirectTo) {
        $problems += [pscustomobject]@{ Assembly = $name; Issue = 'REDIRECT_STALE'; Config = $redirectTo; Bin = $actual.Version }
    }
    if ($actual.Token -and $token -and ($actual.Token -ne $token.ToLower())) {
        $problems += [pscustomobject]@{ Assembly = $name; Issue = 'TOKEN_MISMATCH'; Config = $token; Bin = $actual.Token }
    }
}

# 3. Mixed Telerik.Sitefinity versions = broken upgrade, not a redirect problem
$sfVersions = $bin.GetEnumerator() | Where-Object { $_.Key -like 'Telerik.Sitefinity*' } |
    ForEach-Object { $_.Value.Version } | Sort-Object -Unique
if ($sfVersions.Count -gt 1) {
    $problems += [pscustomobject]@{ Assembly = 'Telerik.Sitefinity.*'; Issue = 'MIXED_SF_VERSIONS'; Config = ''; Bin = ($sfVersions -join ', ') }
}

if ($problems.Count -eq 0) {
    Write-Host "All bindingRedirects match bin contents." -ForegroundColor Green
} else {
    $problems | Sort-Object Issue, Assembly | Format-Table -AutoSize
    Write-Host "`n$(@($problems | Where-Object Issue -eq 'REDIRECT_STALE').Count) stale redirect(s): set newVersion (and the oldVersion upper bound) to the Bin value." -ForegroundColor Yellow
    exit 1
}
```

Reading the report:
- **REDIRECT_STALE** - the actionable one. Edit that `<bindingRedirect>` so `newVersion` = the Bin column, and update `oldVersion` to `0.0.0.0-{Bin}`.
- **TOKEN_MISMATCH** - the redirect never applied at all; fix the `publicKeyToken` to the Bin value.
- **REDIRECT_ORPHAN** - debris; only urgent if the current YSOD names that assembly (then the real fix is restoring the DLL, rule from the classification table).
- **MIXED_SF_VERSIONS** - stop editing config; fix the deployment (clean bin + rebuild/redeploy a single Sitefinity version).

## Step 4 - Check the LIVE site

A binding error usually throws during startup, before authentication runs, so a plain HTTP request to the homepage tells you a lot:

```powershell
try {
    $resp = Invoke-WebRequest 'https://your-site.example' -UseBasicParsing -MaximumRedirection 5
    if ($resp.Content -match '/sitefinity/status' -or $resp.Content -match 'Please wait') {
        'BOOTING - wait 30-90s and retry'
    } else { "UP ($($resp.StatusCode))" }
} catch {
    $body = $_.ErrorDetails.Message
    if ($body -match "Could not load file or assembly '(?<asm>[^,']+)(, Version=(?<ver>[\d\.]+))?") {
        "BINDING ERROR: assembly=$($Matches.asm) requestedVersion=$($Matches.ver)"
    } else { "DOWN: $($_.Exception.Message)" }
}
```

- With `customErrors="On"` the YSOD body is hidden - read the real exception from `App_Data\Sitefinity\Logs\Error.log` instead (search for `Could not load file or assembly`).
- **If a Sitefinity MCP server is connected**, prefer its tools over scraping: `sitefinity_check_status` (up vs booting), `sitefinity_get_last_error`, and `sitefinity_read_error_log` / `sitefinity_search_logs` with the query `Could not load file or assembly` - the log entry contains the full fusion-style message naming the assembly, the requested version, AND which assembly asked for it.
- The error names the REQUESTED version (what the caller compiled against). Your redirect's job is to map that request to what's in bin - which is why `newVersion` comes from the bin inventory, not from the error message.

## Step 5 - The fix loop

```
1. Run check-bindings.ps1            -> fix every REDIRECT_STALE / TOKEN_MISMATCH in web.config
2. Saving web.config recycles the app pool automatically
3. Wait for boot (30-90s; /sitefinity/status or "Please wait" = still starting)
4. Check the live site (Step 4)
5. New binding YSOD for a different assembly? -> it was masked by the previous one.
   If check-bindings is clean but the YSOD persists, the assembly is either MISSING
   from bin (restore it) or has NO redirect entry at all (add a new dependentAssembly
   block using the bin inventory's Name/Version/Token).
6. Repeat until the homepage renders.
```

Adding a brand-new redirect block (when an assembly has none):

```xml
<dependentAssembly>
  <assemblyIdentity name="{Name from bin inventory}" publicKeyToken="{Token from bin inventory}" culture="neutral" />
  <bindingRedirect oldVersion="0.0.0.0-{Version from bin inventory}" newVersion="{Version from bin inventory}" />
</dependentAssembly>
```

## Root causes to fix AFTER the fire is out

A stale redirect is a symptom. The usual arsonists:

- **A satellite project pinned to an older package** overwrites the newer DLL in bin on every build (build order: class libraries build first, web project last). Align the package version across ALL projects in the solution, or the mismatch returns on the next rebuild.
- **NuGet update touched packages.config but someone hand-edited only SOME redirects.** After any package update, run check-bindings.ps1 before committing.
- **Manual DLL drops into bin** (hotfixes) that never made it into source control or the package list.
- **Sitefinity upgrades**: Progress ships a coordinated set - every `Telerik.Sitefinity.*` and `Progress.Sitefinity.*` assembly moves together. Partial upgrades produce MIXED_SF_VERSIONS and no redirect will save you.
- Add `check-bindings.ps1` as an npm script (`"check:bindings": "powershell.exe -ExecutionPolicy Bypass -File SitefinityWeb\\check-bindings.ps1"`) so it joins the build/test command surface - see the `sitefinity-cli-build` skill.

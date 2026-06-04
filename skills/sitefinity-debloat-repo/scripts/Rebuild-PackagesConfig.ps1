<#
.SYNOPSIS
  Rebuild packages.config to list the COMPLETE flat set of packages a .csproj references.

.DESCRIPTION
  packages.config restore is non-transitive: the Telerik.Sitefinity.All meta-package pulls nothing
  on `nuget restore`, so packages.config must explicitly list every package the build references.
  A repo often "works" only because past restores left the missing folders on disk; a clean clone
  would fail.

  This derives the needed set from the csproj's packages\<Id>.<Version>\ references (HintPaths,
  Imports, import-existence checks), unions it with the existing packages.config (to preserve
  runtime/build-only packages that have no HintPath, e.g. MySql.Data, Microsoft.Net.Compilers,
  RazorGenerator.MsBuild), and writes a complete, alphabetically-sorted file.

  Content-only packages (notably Progress.Sitefinity.AdminApp) have no HintPath and cannot be
  inferred -- add those manually after running this (at your Sitefinity version, e.g. 15.4.8631.171).

.EXAMPLE
  pwsh Rebuild-PackagesConfig.ps1 -Csproj .\MyApp.csproj -WhatIf   # preview the diff
  pwsh Rebuild-PackagesConfig.ps1 -Csproj .\MyApp.csproj           # write packages.config
#>
param(
    [Parameter(Mandatory)][string]$Csproj,
    [string]$PackagesConfig,
    [switch]$WhatIf
)

$csprojPath = (Resolve-Path $Csproj).Path
if (-not $PackagesConfig) { $PackagesConfig = Join-Path (Split-Path -Parent $csprojPath) 'packages.config' }
$text = [IO.File]::ReadAllText($csprojPath)

# 1) Every packages\<Id>.<Version>\ referenced anywhere in the csproj.
$rx = [regex]'packages\\([^\\]+?)\.(\d+(?:\.\d+){0,3}(?:-[0-9A-Za-z\.\-]+)?)\\'
$fromCsproj = @{}
$conflicts = @{}
foreach ($m in $rx.Matches($text)) {
    $id = $m.Groups[1].Value
    $v = $m.Groups[2].Value
    if ($fromCsproj.ContainsKey($id) -and $fromCsproj[$id] -ne $v) { $conflicts[$id] = "$($fromCsproj[$id]) vs $v" }
    $fromCsproj[$id] = $v
}

# 2) Existing packages.config (preserve runtime/build-only entries + targetFramework/dev flags).
$cur = @{}
if (Test-Path $PackagesConfig) {
    foreach ($p in ([xml](Get-Content $PackagesConfig)).packages.package) {
        $cur[$p.id] = @{ version = $p.version; tfm = $p.targetFramework; dev = $p.developmentDependency }
    }
}

# 3) Merge: csproj-referenced version is compile-time truth; preserve current-only entries.
$final = @{}
foreach ($id in $fromCsproj.Keys) {
    $tfm = if ($cur.ContainsKey($id) -and $cur[$id].tfm) { $cur[$id].tfm } else { 'net48' }
    $dev = if ($cur.ContainsKey($id)) { $cur[$id].dev } else { $null }
    $final[$id] = @{ version = $fromCsproj[$id]; tfm = $tfm; dev = $dev }
}
foreach ($id in $cur.Keys) {
    if (-not $final.ContainsKey($id)) { $final[$id] = $cur[$id] }
}

# 4) Report.
$added = $final.Keys | Where-Object { -not $cur.ContainsKey($_) } | Sort-Object
$removed = $cur.Keys | Where-Object { -not $final.ContainsKey($_) } | Sort-Object
Write-Host ("current: {0} entries  ->  new: {1} entries" -f $cur.Count, $final.Count)
if ($conflicts.Count) {
    Write-Warning "csproj references conflicting versions for the same package (resolve by hand):"
    $conflicts.GetEnumerator() | ForEach-Object { Write-Host "   ! $($_.Key): $($_.Value)" }
}
if ($added)   { Write-Host "ADDED ($($added.Count)):";   $added   | ForEach-Object { Write-Host "   + $_" } }
if ($removed) { Write-Host "REMOVED ($($removed.Count)):"; $removed | ForEach-Object { Write-Host "   - $_" } }

# 5) Emit (UTF-8 with BOM, the packages.config convention).
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<packages>')
foreach ($id in ($final.Keys | Sort-Object)) {
    $e = $final[$id]
    $line = "  <package id=`"$id`" version=`"$($e.version)`""
    if ($e.tfm) { $line += " targetFramework=`"$($e.tfm)`"" }
    if ($e.dev -eq 'true') { $line += ' developmentDependency="true"' }
    [void]$sb.AppendLine($line + ' />')
}
[void]$sb.Append('</packages>')

if ($WhatIf) {
    Write-Host "`n-WhatIf: nothing written. Re-run without -WhatIf to write $PackagesConfig"
}
else {
    [IO.File]::WriteAllText($PackagesConfig, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "`nWrote $PackagesConfig ($($final.Count) entries). Now add Progress.Sitefinity.AdminApp manually if this is an MVC/Feather-era site."
}

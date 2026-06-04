<#
.SYNOPSIS
  Flag packages in packages.config whose pinned version is NOT on public nuget.org, so you know
  which ones to vendor into nuget-local/ before you rely on a credential-free restore.

.DESCRIPTION
  Sitefinity packages are all on nuget.org (back to 6.3). The risk is OLD third-party OSS packages
  that get delisted over time (e.g. linqtotwitterNET40, Microsoft.Http, MySql.Data 6.6.6). This
  queries the nuget.org flat-container API for each package+version in packages.config and reports
  any that 404 (package gone) or whose exact version is missing. NuGet trailing-zero normalization
  (e.g. 3.0.8.0 == 3.0.8) is accounted for. Read-only; makes no changes.

.EXAMPLE
  pwsh Check-PackagesOnNuget.ps1 -PackagesConfig .\packages.config
#>
param(
    [string]$PackagesConfig = 'packages.config'
)

$pc = [xml](Get-Content $PackagesConfig)
$count = $pc.packages.package.Count
$issues = @()

foreach ($p in $pc.packages.package) {
    $id = $p.id.ToLower()
    $v = $p.version
    try {
        $r = Invoke-RestMethod -Uri "https://api.nuget.org/v3-flatcontainer/$id/index.json" -TimeoutSec 20
        $norm = $v -replace '(\.0)+$', ''
        $normalizedFeed = $r.versions | ForEach-Object { $_ -replace '(\.0)+$', '' }
        $ok = ($r.versions -contains $v) -or ($normalizedFeed -contains $norm)
        if (-not $ok) {
            $issues += "VERSION-MISSING  $($p.id) $v   (nuget.org latest: $($r.versions[-1])) -> bump or vendor"
        }
    }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) {
            $issues += "DELISTED         $($p.id) $v   -> vendor its .nupkg into nuget-local/"
        }
        else {
            $issues += "CHECK-FAILED     $($p.id) $v   ($($_.Exception.Message))"
        }
    }
}

Write-Host "Checked $count packages against nuget.org."
if ($issues) {
    Write-Host "`nNeeds attention (vendor into nuget-local/, or reconcile the version):"
    $issues | ForEach-Object { Write-Host "   $_" }
    Write-Host "`nTo vendor: copy packages\<Id>.<Version>\<Id>.<Version>.nupkg into .\nuget-local\"
    Write-Host "and add '!/nuget-local/' + '!/nuget-local/*.nupkg' to .gitignore so they're tracked."
}
else {
    Write-Host "All packages resolve on nuget.org -- no vendoring needed (credential-free restore will work)."
}

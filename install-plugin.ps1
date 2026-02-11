# ============================================================================
# SitefinityCommunity.Mcp — Plugin Installer
#
# Copies the Sitefinity plugin source files into your web app project.
#
# Usage:
#   .\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp"
#   .\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp" -Force
#
# What it does:
#   1. Creates Code\Mcp\SitefinityCommunity\ in your project
#   2. Copies all plugin .cs files there
#   3. Reminds you to add one line to Global.asax
# ============================================================================

param(
    [Parameter(Mandatory = $true, HelpMessage = "Path to your SitefinityWebApp project folder")]
    [string]$Target,

    [Parameter(HelpMessage = "Overwrite existing files without prompting")]
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# Resolve paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $scriptDir "src\SitefinityCommunity.Mcp.SitefinityPlugin"
$destDir = Join-Path $Target "Code\Mcp\SitefinityCommunity"

# Validate source
if (-not (Test-Path $sourceDir)) {
    Write-Error "Plugin source not found at: $sourceDir"
    exit 1
}

# Validate target looks like a Sitefinity project
$globalAsax = Join-Path $Target "Global.asax.cs"
if (-not (Test-Path $globalAsax)) {
    Write-Warning "Global.asax.cs not found in '$Target'. Are you sure this is a Sitefinity web app project?"
    if (-not $Force) {
        $confirm = Read-Host "Continue anyway? (y/N)"
        if ($confirm -ne 'y') {
            Write-Host "Aborted." -ForegroundColor Yellow
            exit 0
        }
    }
}

# Create destination folder
if (-not (Test-Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Write-Host "Created: $destDir" -ForegroundColor Green
}

# Copy .cs files
$files = Get-ChildItem -Path $sourceDir -Filter "*.cs"
$copied = 0
$skipped = 0

foreach ($file in $files) {
    $destFile = Join-Path $destDir $file.Name

    if ((Test-Path $destFile) -and -not $Force) {
        Write-Host "  EXISTS: $($file.Name) (use -Force to overwrite)" -ForegroundColor Yellow
        $skipped++
        continue
    }

    Copy-Item -Path $file.FullName -Destination $destFile -Force
    Write-Host "  COPIED: $($file.Name)" -ForegroundColor Green
    $copied++
}

Write-Host ""
Write-Host "Done! $copied file(s) copied, $skipped skipped." -ForegroundColor Cyan
Write-Host ""
Write-Host "Files installed to:" -ForegroundColor Cyan
Write-Host "  $destDir" -ForegroundColor White
Write-Host ""

# Check if already registered in Global.asax
$registered = $false
if (Test-Path $globalAsax) {
    $globalContent = Get-Content $globalAsax -Raw
    if ($globalContent -match "McpInit\.Register") {
        $registered = $true
    }
}

if ($registered) {
    Write-Host "McpInit.Register() already found in Global.asax.cs — you're all set!" -ForegroundColor Green
} else {
    Write-Host "NEXT STEP: Add this line to Global.asax.cs in your Bootstrapper_Initialized handler:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();" -ForegroundColor White
    Write-Host ""
    Write-Host "Then set your API key in Sitefinity Admin > Settings > Advanced > McpSettings" -ForegroundColor Yellow
}

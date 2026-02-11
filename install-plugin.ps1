# ============================================================================
# SitefinityCommunity.Mcp - Plugin Installer
#
# Copies the Sitefinity plugin source files into your web app project
# and registers them in your .csproj so Visual Studio compiles them.
#
# Usage:
#   .\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp"
#   .\install-plugin.ps1 -Target "C:\Path\To\SitefinityWebApp" -Force
#
# What it does:
#   1. Creates Code\Mcp\SitefinityCommunity\ in your project
#   2. Copies all plugin .cs files there
#   3. Adds Compile Include entries to your .csproj
#   4. Removes stale entries for files that no longer exist
#   5. Reminds you to add one line to Global.asax
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
$relativeDir = "Code\Mcp\SitefinityCommunity"

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

# Find the .csproj file
$csprojFiles = Get-ChildItem -Path $Target -Filter "*.csproj"
if ($csprojFiles.Count -eq 0) {
    Write-Warning "No .csproj file found in '$Target'. Files will be copied but you'll need to include them in your project manually."
    $csprojPath = $null
} elseif ($csprojFiles.Count -gt 1) {
    Write-Warning "Multiple .csproj files found. Using: $($csprojFiles[0].Name)"
    $csprojPath = $csprojFiles[0].FullName
} else {
    $csprojPath = $csprojFiles[0].FullName
}

# -- Step 1: Clean + Copy files ----------------------------------------

if (-not (Test-Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    Write-Host "Created: $destDir" -ForegroundColor Green
} else {
    # Clean out old .cs files so renamed/removed files don't linger
    $existing = Get-ChildItem -Path $destDir -Filter "*.cs" -ErrorAction SilentlyContinue
    if ($existing.Count -gt 0) {
        if (-not $Force) {
            Write-Host "Existing plugin files found in $destDir" -ForegroundColor Yellow
            $confirm = Read-Host "Remove old files and install fresh? (y/N)"
            if ($confirm -ne 'y') {
                Write-Host "Aborted." -ForegroundColor Yellow
                exit 0
            }
        }

        $removed = 0
        foreach ($old in $existing) {
            Remove-Item $old.FullName -Force
            Write-Host "  REMOVED: $($old.Name)" -ForegroundColor DarkGray
            $removed++
        }
        Write-Host "  Cleaned $removed old file(s)" -ForegroundColor DarkGray
    }
}

# Copy fresh .cs files
$files = Get-ChildItem -Path $sourceDir -Filter "*.cs"
$copied = 0

foreach ($file in $files) {
    $destFile = Join-Path $destDir $file.Name
    Copy-Item -Path $file.FullName -Destination $destFile -Force
    Write-Host "  COPIED: $($file.Name)" -ForegroundColor Green
    $copied++
}

Write-Host ""
Write-Host "$copied file(s) installed to:" -ForegroundColor Cyan
Write-Host "  $destDir" -ForegroundColor White

# -- Step 2: Update .csproj ---------------------------------------------

if ($csprojPath) {
    Write-Host ""
    Write-Host "Updating $($csprojFiles[0].Name)..." -ForegroundColor Cyan

    [xml]$csproj = Get-Content $csprojPath -Raw

    # Check if this is an SDK-style project (auto-includes .cs files)
    $isSdkStyle = $csproj.Project.Sdk -ne $null
    if ($isSdkStyle) {
        Write-Host "  SDK-style project detected - .cs files are auto-included, no changes needed." -ForegroundColor Green
    } else {
        # Legacy .csproj - needs explicit Compile Include entries
        $ns = $csproj.Project.NamespaceURI
        $nsManager = New-Object System.Xml.XmlNamespaceManager($csproj.NameTable)
        if ($ns) {
            $nsManager.AddNamespace("ms", $ns)
            $compileXPath = "//ms:Compile"
        } else {
            $compileXPath = "//Compile"
        }

        # Build list of what SHOULD be in the csproj
        $expectedIncludes = $files | ForEach-Object { "$relativeDir\$($_.Name)" }

        # Remove any existing MCP entries (clean slate for our folder)
        $mcpPattern = [regex]::Escape($relativeDir)
        $removedEntries = 0
        $compileNodes = $csproj.SelectNodes($compileXPath, $nsManager)
        foreach ($node in $compileNodes) {
            $include = $node.GetAttribute("Include")
            if ($include -match $mcpPattern) {
                $parent = $node.ParentNode
                $parent.RemoveChild($node) | Out-Null
                Write-Host "  REMOVED from csproj: $include" -ForegroundColor DarkGray
                $removedEntries++
            }
        }

        # Find an existing ItemGroup with Compile elements, or create one
        $compileItemGroup = $null
        foreach ($ig in $csproj.Project.ItemGroup) {
            if ($ig.Compile) {
                $compileItemGroup = $ig
                break
            }
        }

        if (-not $compileItemGroup) {
            $compileItemGroup = $csproj.CreateElement("ItemGroup", $ns)
            $csproj.Project.AppendChild($compileItemGroup) | Out-Null
        }

        # Add fresh entries
        $addedEntries = 0
        foreach ($include in $expectedIncludes) {
            $compileElement = $csproj.CreateElement("Compile", $ns)
            $compileElement.SetAttribute("Include", $include)
            $compileItemGroup.AppendChild($compileElement) | Out-Null
            Write-Host "  ADDED to csproj: $include" -ForegroundColor Green
            $addedEntries++
        }

        # Save with UTF-8 BOM (standard for VS .csproj files)
        $utf8Bom = New-Object System.Text.UTF8Encoding($true)
        $writer = New-Object System.IO.StreamWriter($csprojPath, $false, $utf8Bom)
        $csproj.Save($writer)
        $writer.Close()

        Write-Host "  Updated $($csprojFiles[0].Name): $addedEntries entries added" -ForegroundColor Cyan
    }
}

# -- Step 3: Check Global.asax registration -----------------------------

Write-Host ""

$registered = $false
if (Test-Path $globalAsax) {
    $globalContent = Get-Content $globalAsax -Raw
    if ($globalContent -match "McpInit\.Register") {
        $registered = $true
    }
}

if ($registered) {
    Write-Host "McpInit.Register() already found in Global.asax.cs - you're all set!" -ForegroundColor Green
} else {
    Write-Host "NEXT STEP: Add this line to Global.asax.cs in your Bootstrapper_Initialized handler:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  SitefinityCommunity.Mcp.SitefinityPlugin.McpInit.Register();" -ForegroundColor White
    Write-Host ""
    Write-Host "Then set your API key in Sitefinity Admin > Settings > Advanced > McpSettings" -ForegroundColor Yellow
}

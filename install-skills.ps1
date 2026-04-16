# ============================================================================
# SitefinityCommunity.Mcp - Skills Installer
#
# Installs the bundled Sitefinity skills into your AI agent of choice.
# Supports Claude Code, Cursor, Codex, and Copilot — matches the shared
# Agent Skills spec used by Vercel's `npx skills` ecosystem.
#
# Behavior:
#   1. Ask whether you want project-level or global install
#   2. Detect which AI agents are present (by existing directories or PATH)
#      and ask which ones you want skills added to
#   3. Write a canonical copy to <root>/.agents/skills/<name>/
#   4. Symlink (preferred) or copy from each selected agent's skills dir
#      back to the canonical copy, so updating <root>/.agents/skills/<name>/
#      updates every agent at once
#
# Non-interactive / CI usage:
#   .\install-skills.ps1 -Scope global -Agents claude,cursor -Force
#   .\install-skills.ps1 -Scope project -Target "C:\Proj" -Agents claude -Force
#
# All prompts default to recommended choices; -Force skips all prompts.
# ============================================================================

param(
    [Parameter(HelpMessage = "'project' (this or -Target folder) or 'global' (home directory). Prompts if omitted.")]
    [ValidateSet("project", "global")]
    [string]$Scope,

    [Parameter(HelpMessage = "Project folder when -Scope project. Defaults to current directory.")]
    [string]$Target,

    [Parameter(HelpMessage = "Comma-separated agents: claude, cursor, codex, copilot. Prompts if omitted.")]
    [string[]]$Agents,

    [Parameter(HelpMessage = "Skip all prompts; replace existing skills.")]
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# --- Known agents and their skills directories ------------------------------

$AgentMap = [ordered]@{
    claude  = @{ Display = "Claude Code"; Dir = ".claude/skills" }
    cursor  = @{ Display = "Cursor";       Dir = ".cursor/skills" }
    codex   = @{ Display = "Codex";        Dir = ".codex/skills" }
    copilot = @{ Display = "GitHub Copilot"; Dir = ".github/copilot/skills" }
}

# --- Resolve paths ----------------------------------------------------------

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $scriptDir "skills"

if (-not (Test-Path $sourceDir)) {
    Write-Error "Skills source not found at: $sourceDir"
    exit 1
}

$skillFolders = Get-ChildItem -Path $sourceDir -Directory | Where-Object {
    Test-Path (Join-Path $_.FullName "SKILL.md")
}
if ($skillFolders.Count -eq 0) {
    Write-Error "No skill folders with SKILL.md found in $sourceDir"
    exit 1
}

# --- Prompt: Scope ---------------------------------------------------------

if (-not $Scope) {
    Write-Host ""
    Write-Host "Install scope:" -ForegroundColor Cyan
    Write-Host "  [1] Project — skills only active inside one project folder"
    Write-Host "  [2] Global  — skills available to every project on this machine"
    $choice = Read-Host "Choose (1/2) [default: 1]"
    $Scope = if ($choice -eq '2') { 'global' } else { 'project' }
}

if ($Scope -eq 'global') {
    $rootDir = $HOME
    Write-Host "Scope: global ($rootDir)" -ForegroundColor Green
} else {
    if (-not $Target) { $Target = (Get-Location).Path }
    if (-not (Test-Path $Target)) {
        Write-Error "Project target folder does not exist: $Target"
        exit 1
    }
    $rootDir = (Resolve-Path $Target).Path
    Write-Host "Scope: project ($rootDir)" -ForegroundColor Green
}

# --- Detect present agents -------------------------------------------------

function Test-AgentPresent {
    param([string]$AgentKey, [string]$RootDir)

    # Treat as "present" if either the agent's dir already exists OR its CLI is on PATH.
    $agentDir = Join-Path $RootDir $AgentMap[$AgentKey].Dir
    if (Test-Path $agentDir) { return $true }

    switch ($AgentKey) {
        'claude'  { return [bool](Get-Command claude  -ErrorAction SilentlyContinue) }
        'cursor'  { return [bool](Get-Command cursor  -ErrorAction SilentlyContinue) -or (Test-Path (Join-Path $RootDir ".cursor")) }
        'codex'   { return [bool](Get-Command codex   -ErrorAction SilentlyContinue) }
        'copilot' { return [bool](Get-Command gh      -ErrorAction SilentlyContinue) }
        default   { return $false }
    }
}

# --- Prompt: Agents --------------------------------------------------------

if (-not $Agents -or $Agents.Count -eq 0) {
    Write-Host ""
    Write-Host "Select target agents (detected agents are marked *):" -ForegroundColor Cyan

    $keys = @($AgentMap.Keys)
    for ($i = 0; $i -lt $keys.Count; $i++) {
        $key = $keys[$i]
        $present = Test-AgentPresent -AgentKey $key -RootDir $rootDir
        $marker = if ($present) { '*' } else { ' ' }
        Write-Host ("  [{0}]{1} {2} ({3})" -f ($i + 1), $marker, $AgentMap[$key].Display, $AgentMap[$key].Dir)
    }

    Write-Host "  [a] all"
    Write-Host "  [d] detected only (default)"
    $input = Read-Host "Comma-separated numbers / 'a' / 'd'"

    if ([string]::IsNullOrWhiteSpace($input) -or $input -eq 'd') {
        $Agents = $keys | Where-Object { Test-AgentPresent -AgentKey $_ -RootDir $rootDir }
        if ($Agents.Count -eq 0) {
            Write-Host "No agents detected — defaulting to Claude Code." -ForegroundColor Yellow
            $Agents = @('claude')
        }
    } elseif ($input -eq 'a') {
        $Agents = $keys
    } else {
        $Agents = $input.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object {
            if ($_ -match '^\d+$') {
                $idx = [int]$_ - 1
                if ($idx -ge 0 -and $idx -lt $keys.Count) { $keys[$idx] }
            } else { $_ }
        }
    }
}

# Validate agent keys
$invalid = $Agents | Where-Object { -not $AgentMap.Contains($_) }
if ($invalid) {
    Write-Error "Unknown agent(s): $($invalid -join ', '). Valid: $($AgentMap.Keys -join ', ')"
    exit 1
}

$Agents = $Agents | Select-Object -Unique
Write-Host ("Agents: {0}" -f (($Agents | ForEach-Object { $AgentMap[$_].Display }) -join ', ')) -ForegroundColor Green

# --- Install each skill ----------------------------------------------------

$canonicalRoot = Join-Path $rootDir ".agents/skills"
if (-not (Test-Path $canonicalRoot)) {
    New-Item -ItemType Directory -Path $canonicalRoot -Force | Out-Null
}

function Install-AgentLink {
    param([string]$AgentKey, [string]$SkillName, [string]$CanonicalSkillDir, [string]$RootDir)

    $agentSkillsDir = Join-Path $RootDir $AgentMap[$AgentKey].Dir
    if (-not (Test-Path $agentSkillsDir)) {
        New-Item -ItemType Directory -Path $agentSkillsDir -Force | Out-Null
    }

    $linkPath = Join-Path $agentSkillsDir $SkillName
    if (Test-Path $linkPath) {
        Remove-Item $linkPath -Recurse -Force
    }

    # Prefer a directory symlink so updates to the canonical copy propagate to every agent.
    # On Windows this needs Developer Mode or admin; fall back to a plain copy if it fails.
    try {
        New-Item -ItemType SymbolicLink -Path $linkPath -Target $CanonicalSkillDir -ErrorAction Stop | Out-Null
        return "symlinked"
    } catch {
        Copy-Item -Path $CanonicalSkillDir -Destination $linkPath -Recurse -Force
        return "copied"
    }
}

$installed = 0
$failed = 0
$linkedReport = @()

foreach ($skill in $skillFolders) {
    $skillName = $skill.Name
    $canonicalSkillDir = Join-Path $canonicalRoot $skillName

    if (Test-Path $canonicalSkillDir) {
        if (-not $Force) {
            Write-Host ""
            Write-Host "Canonical copy exists: $canonicalSkillDir" -ForegroundColor Yellow
            $confirm = Read-Host "Replace $skillName? (y/N)"
            if ($confirm -ne 'y') {
                Write-Host "  SKIPPED: $skillName" -ForegroundColor DarkGray
                continue
            }
        }
        Remove-Item $canonicalSkillDir -Recurse -Force
    }

    # 1. Canonical copy
    Copy-Item -Path $skill.FullName -Destination $canonicalSkillDir -Recurse -Force
    if (-not (Test-Path (Join-Path $canonicalSkillDir "SKILL.md"))) {
        Write-Error "  FAILED: canonical copy missing SKILL.md for $skillName"
        $failed++
        continue
    }
    Write-Host ""
    Write-Host "COPIED: $skillName → $canonicalSkillDir" -ForegroundColor Green

    # 2. Per-agent links
    foreach ($agentKey in $Agents) {
        try {
            $mode = Install-AgentLink -AgentKey $agentKey -SkillName $skillName `
                -CanonicalSkillDir $canonicalSkillDir -RootDir $rootDir
            $agentDir = Join-Path $rootDir $AgentMap[$agentKey].Dir
            Write-Host ("  {0,-10} {1}  ({2})" -f $AgentMap[$agentKey].Display, (Join-Path $agentDir $skillName), $mode) -ForegroundColor DarkGreen
            $linkedReport += [PSCustomObject]@{ Skill = $skillName; Agent = $agentKey; Mode = $mode }
        } catch {
            Write-Warning "  $($AgentMap[$agentKey].Display) link failed for $skillName — $_"
            $failed++
        }
    }

    $installed++
}

# --- Summary ---------------------------------------------------------------

Write-Host ""
Write-Host "Installed $installed skill(s) to $($Agents.Count) agent(s)." -ForegroundColor Cyan

$copyFallbackCount = ($linkedReport | Where-Object Mode -eq 'copied').Count
if ($copyFallbackCount -gt 0) {
    Write-Host ""
    Write-Host "NOTE: $copyFallbackCount link(s) fell back to plain copies (symlinks unavailable)." -ForegroundColor Yellow
    Write-Host "      Windows users: enable Developer Mode (Settings > System > For developers)" -ForegroundColor Yellow
    Write-Host "      so symlinks work without admin — updates will then propagate automatically." -ForegroundColor Yellow
}

if ($failed -gt 0) {
    Write-Host ""
    Write-Error "$failed operation(s) failed"
    exit 1
}

Write-Host ""
Write-Host "Skills available. Invoke them in your agent:" -ForegroundColor Green
foreach ($skill in $skillFolders) {
    Write-Host "  /$($skill.Name)" -ForegroundColor White
}

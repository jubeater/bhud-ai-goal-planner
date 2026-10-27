<#
.SYNOPSIS
    Stops Blish HUD, builds a worktree's module and launches Blish HUD with it.

.DESCRIPTION
    Blish HUD keeps the .bhm it loaded open, restarts itself when the game closes, and holds the
    worktree folder while it runs. This script stops every Blish HUD process first, builds the
    module into obj\bhm\ (so a later `dotnet test` can still write bin\ while Blish HUD runs), and
    starts Blish HUD with --debug --module pointing at that build.

    Blish HUD is looked for in $env:BLISHHUD_DIR, then in Desktop\Blish.HUD under the user profile.

.EXAMPLE
    scripts\dev-run.ps1 ..\AI_assistant-worktrees\8-catalog-install

.EXAMPLE
    scripts\dev-run.ps1 -Stop
    Stops Blish HUD only, for example before removing a worktree.
#>
param(
    [string] $Worktree = (Split-Path -Parent $PSScriptRoot),
    [switch] $Stop
)

$ErrorActionPreference = 'Stop'

$running = @(Get-Process -Name 'Blish HUD' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "Stopping Blish HUD ($($running.Count) process(es))..."
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

if ($Stop) { return }

$blishDir = if ($env:BLISHHUD_DIR) { $env:BLISHHUD_DIR } else { Join-Path $env:USERPROFILE 'Desktop\Blish.HUD' }
$blishExe = Join-Path $blishDir 'Blish HUD.exe'
if (-not (Test-Path $blishExe)) {
    throw "Blish HUD.exe not found in '$blishDir'. Set BLISHHUD_DIR to the folder that holds it."
}

$project = Join-Path (Resolve-Path $Worktree) 'src\AiGoalPlanner\AiGoalPlanner.csproj'
if (-not (Test-Path $project)) {
    throw "No module project at '$project'. Pass the worktree folder, e.g. ..\AI_assistant-worktrees\<branch>."
}

Write-Host "Building $project ..."
dotnet build $project -c Debug --nologo -v q '-p:BlishHUDModuleOutputDir=obj\bhm\'
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$module = Join-Path (Split-Path -Parent $project) 'obj\bhm\AiGoalPlanner.bhm'
if (-not (Test-Path $module)) { throw "Build finished but '$module' is missing." }

$commit = git -C $Worktree rev-parse --short HEAD
Write-Host "Launching Blish HUD with $module (commit $commit)."
Start-Process -FilePath $blishExe -WorkingDirectory $blishDir -ArgumentList '--debug', '--module', "`"$module`""

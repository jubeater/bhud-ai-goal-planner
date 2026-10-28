<#
.SYNOPSIS
    Runs one developer channel operation that is not a send.

.DESCRIPTION
    Writes the operation's request file to the channel's inbox, waits for its response (default
    120 s) and prints it readably. Needs Blish HUD started with --debug (scripts\dev-run.ps1 does
    this), the module loaded, and 'Developer channel (for testing only)' turned on.

.EXAMPLE
    scripts\dev-op.ps1 apply 0,2

.EXAMPLE
    scripts\dev-op.ps1 cancel_review
    scripts\dev-op.ps1 undo
    scripts\dev-op.ps1 new_chat
    scripts\dev-op.ps1 state
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Op,
    [Parameter(Position = 1)]
    [string] $Actions,
    [int] $TimeoutSec = 120
)

$ErrorActionPreference = 'Stop'

. (Join-Path (Split-Path -Parent $PSCommandPath) 'dev-common.ps1')

switch ($Op) {
    'apply' {
        if ([string]::IsNullOrWhiteSpace($Actions)) {
            throw "Usage: scripts\dev-op.ps1 apply 0,2"
        }

        $picked = $Actions.Split(',') | ForEach-Object { [int]$_.Trim() }
        $id = Write-DevRequest -Op 'apply' -Actions $picked
    }
    'cancel_review' { $id = Write-DevRequest -Op 'cancel_review' }
    'undo'          { $id = Write-DevRequest -Op 'undo' }
    'new_chat'      { $id = Write-DevRequest -Op 'new_chat' }
    'state'         { $id = Write-DevRequest -Op 'state' }
    default         { throw "Unknown operation '$Op'. Use apply, cancel_review, undo, new_chat or state." }
}

Show-DevResponse (Wait-DevResponse -Id $id -TimeoutSec $TimeoutSec)

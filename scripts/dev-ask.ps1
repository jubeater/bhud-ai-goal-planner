<#
.SYNOPSIS
    Sends a Request to the real Chat in a running Blish HUD and prints the answer.

.DESCRIPTION
    Writes a send request file to the developer channel's inbox, waits for its response (default
    120 s) and prints it readably: the Chat messages, the open Review, the Lookups and Provider
    round trips, and whether Undo is available. Needs Blish HUD started with --debug
    (scripts\dev-run.ps1 does this), the module loaded, and 'Developer channel (for testing only)'
    turned on. The Request goes through the Provider the Player set up and costs tokens like any
    other Request.

.EXAMPLE
    scripts\dev-ask.ps1 "Where am I?"

.EXAMPLE
    scripts\dev-ask.ps1 "Where can I still get mastery points?" -NewChat
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $Text,
    [switch] $NewChat,
    [int] $TimeoutSec = 120
)

$ErrorActionPreference = 'Stop'

. (Join-Path (Split-Path -Parent $PSCommandPath) 'dev-common.ps1')

if ($NewChat) {
    $id = Write-DevRequest -Op 'new_chat'
    Wait-DevResponse -Id $id -TimeoutSec $TimeoutSec | Out-Null
}

$id = Write-DevRequest -Op 'send' -Text $Text
Show-DevResponse (Wait-DevResponse -Id $id -TimeoutSec $TimeoutSec)

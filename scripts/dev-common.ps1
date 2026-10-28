<#
.SYNOPSIS
    Shared helpers for the developer channel scripts (dev-ask, dev-op).

    The channel watches <Documents>\Guild Wars 2\addons\blishhud\aigoalplanner\dev\inbox for
    request files and writes dev\outbox\<id>.json answers. These helpers write a request, wait for
    its response and print it readably. They only read and write those folders.
#>

$script:DevRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Guild Wars 2\addons\blishhud\aigoalplanner\dev'
$script:DevInbox = Join-Path $script:DevRoot 'inbox'
$script:DevOutbox = Join-Path $script:DevRoot 'outbox'

function New-DevId {
    return [guid]::NewGuid().ToString()
}

<#
.SYNOPSIS
    Reads a window rectangle through user32, for dev-shot.ps1.

.DESCRIPTION
    Answers the window's bounds as a System.Drawing.Rectangle, or $null when the handle has no
    window (an invisible or missing window is not an error: the caller falls back to the screen).
    The generated interop type is referenced explicitly, since -PassThru emits every nested type
    and the array it returns has no static methods.
#>
function Get-DevWindowRect {
    param(
        [Parameter(Mandatory = $true)]
        [IntPtr] $Handle
    )

    Add-Type -AssemblyName System.Drawing
    Add-Type -MemberDefinition @'
[DllImport("user32.dll")]
public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
'@ -Name 'DevShotNative' -Namespace 'DevShot' | Out-Null

    $rect = New-Object DevShot.DevShotNative+RECT

    if ([DevShot.DevShotNative]::GetWindowRect($Handle, [ref]$rect)) {
        return New-Object System.Drawing.Rectangle($rect.Left, $rect.Top,
                                                   ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top))
    }

    return $null
}

<#
.SYNOPSIS
    Normalizes the apply indexes from dev-op.ps1 into integers.

.DESCRIPTION
    Accepts both spellings the shell produces: one "0,2" string or an array of strings, and splits
    each entry on commas. Anything that is not an integer is an error, so a typo never becomes a
    truncated or rounded index.
#>
function ConvertTo-DevActionIndexes {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Actions
    )

    $indexes = @()

    foreach ($entry in $Actions) {
        foreach ($part in ($entry -split ',')) {
            $trimmed = $part.Trim()

            if ($trimmed -notmatch '^-?\d+$') {
                throw "Action index '$trimmed' is not an integer. Usage: scripts\dev-op.ps1 apply 0,2"
            }

            $indexes += [int]$trimmed
        }
    }

    if ($indexes.Count -eq 0) {
        throw "Usage: scripts\dev-op.ps1 apply 0,2"
    }

    return $indexes
}
function Write-DevRequest {
    param(
        [string] $Op,
        [string] $Text = $null,
        [int[]] $Actions = $null
    )

    if (-not (Test-Path $script:DevInbox)) {
        New-Item -ItemType Directory -Path $script:DevInbox -Force | Out-Null
    }

    $id = New-DevId
    $request = [ordered]@{ id = $id; op = $Op }

    # A [string] parameter the caller omits arrives as "", not $null, so the check is whether it
    # was supplied at all: operations other than send must not carry a text field, or the parser
    # rejects the file with bad_request.
    if ($PSBoundParameters.ContainsKey('Text')) { $request['text'] = $Text }
    if ($PSBoundParameters.ContainsKey('Actions')) { $request['actions'] = $Actions }

    $path = Join-Path $script:DevInbox ($id + '.json')
    ConvertTo-Json $request -Compress | Set-Content -Path $path -Encoding UTF8

    return $id
}

function Wait-DevResponse {
    param(
        [string] $Id,
        [int] $TimeoutSec = 120
    )

    $path = Join-Path $script:DevOutbox ($Id + '.json')
    $deadline = (Get-Date).AddSeconds($TimeoutSec)

    while (-not (Test-Path $path)) {
        if ((Get-Date) -gt $deadline) {
            throw "No response for '$Id' after $TimeoutSec s. Is Blish HUD started with --debug, the module loaded, and 'Developer channel (for testing only)' turned on?"
        }

        Start-Sleep -Milliseconds 250
    }

    return Get-Content -Path $path -Raw | ConvertFrom-Json
}

function Show-DevResponse {
    param($Response)

    if (-not $Response.ok) {
        Write-Output "error: $($Response.error)"
        return
    }

    foreach ($message in $Response.messages) {
        Write-Output "[$($message.author)] $($message.text)"
    }

    if ($Response.review -ne $null) {
        Write-Output ''
        Write-Output 'Review:'

        $index = 0
        foreach ($action in $Response.review.actions) {
            $flags = @()

            if ($action.sideEffect) { $flags += 'side effect' }
            if ($action.accepted) { $flags += 'accepted' } else { $flags += 'rejected' }

            Write-Output ("  [{0}] {1} / {2}: {3} -> {4} ({5})" -f $index, $action.module, $action.target,
                          $action.current, $action.proposed, ($flags -join ', '))
            Write-Output "       reason: $($action.reason)"

            if (-not $action.accepted) {
                Write-Output "       problem: $($action.problem)"
            }

            $index++
        }
    }

    if ($Response.lookups.Count -gt 0) {
        Write-Output ''
        Write-Output 'Lookups:'

        foreach ($lookup in $Response.lookups) {
            Write-Output ("  {0}: {1} ms, {2}, {3} chars" -f $lookup.name, $lookup.durationMs,
                          $lookup.detail, $lookup.chars)
        }
    }

    if ($Response.roundTrips.Count -gt 0) {
        Write-Output ''
        Write-Output 'Provider round trips:'

        foreach ($round in $Response.roundTrips) {
            Write-Output ("  {0} ms, finish_reason={1}, tool_calls={2}" -f $round.durationMs,
                          $round.finishReason, $round.toolCalls)
        }
    }

    if ($Response.applied -ne $null) {
        Write-Output ''
        Write-Output "Applied: $($Response.applied.describe)"
    }

    Write-Output ''
    Write-Output "undoAvailable: $($Response.undoAvailable)"
}

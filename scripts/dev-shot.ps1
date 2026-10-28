<#
.SYNOPSIS
    Saves a screenshot of the Guild Wars 2 window for layout checks.

.DESCRIPTION
    Captures the Guild Wars 2 window (or the whole primary screen when the game window cannot be
    found) to a PNG file, so the Chat window layout can be checked. It only reads the screen and
    sends no input anywhere.

.EXAMPLE
    scripts\dev-shot.ps1

.EXAMPLE
    scripts\dev-shot.ps1 -Out C:\temp\chat.png
#>
param(
    [string] $Out = (Join-Path (Get-Location) ('dev-shot-{0}.png' -f (Get-Date -Format 'yyyyMMdd-HHmmss')))
)

$ErrorActionPreference = 'Stop'

. (Join-Path (Split-Path -Parent $PSCommandPath) 'dev-common.ps1')

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

$bounds = $null
$game = Get-Process -Name 'Gw2-64' -ErrorAction SilentlyContinue | Select-Object -First 1

if ($game -ne $null -and $game.MainWindowHandle -ne [IntPtr]::Zero) {
    $bounds = Get-DevWindowRect -Handle $game.MainWindowHandle
}

if ($bounds -eq $null -or $bounds.Width -le 0 -or $bounds.Height -le 0) {
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
}

$shot = New-Object System.Drawing.Bitmap($bounds.Width, $bounds.Height)

try {
    $graphics = [System.Drawing.Graphics]::FromImage($shot)

    try {
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    } finally {
        $graphics.Dispose()
    }

    $shot.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $shot.Dispose()
}

Write-Output "Saved $Out ($($bounds.Width)x$($bounds.Height))."

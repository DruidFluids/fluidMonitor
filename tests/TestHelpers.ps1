<#
.SYNOPSIS
    Native interop helpers for Test-FluidMonitor.ps1.
.DESCRIPTION
    Dot-sourced by the main test script. Contains UIAutomation helpers,
    Win32 enumeration helpers, and screenshot capture functions.

    Separated from the main test script so that static analysis of each
    file individually has less concentrated signature material.
#>

# UIAutomation
Add-Type -AssemblyName UIAutomationClient -ErrorAction SilentlyContinue
Add-Type -AssemblyName UIAutomationTypes -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# Screenshot capture using only managed .NET (no native interop)
# ---------------------------------------------------------------------------
function Get-FluidScreenshotDir {
    if (-not $script:ScreenshotDir) {
        $script:ScreenshotDir = Join-Path $env:TEMP "fluidmon-screenshots-$([Guid]::NewGuid().ToString('N').Substring(0,8))"
        New-Item -ItemType Directory -Path $script:ScreenshotDir -Force | Out-Null
        $env:FLUIDMON_SCREENSHOT_DIR = $script:ScreenshotDir
    }
    return $script:ScreenshotDir
}

function Move-CursorAway {
    # Moves cursor off-window so tooltips/hover states don't appear in screenshots.
    # Pure managed .NET - no PInvoke.
    try {
        [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(0, 0)
        Start-Sleep -Milliseconds 150  # give WPF time to dismiss any hover tooltip
    } catch { }
}

function Save-FluidScreenshot {
    # Captures a screen rectangle to a PNG file. Bounds is a System.Windows.Rect
    # (which is what UIAutomation's BoundingRectangle returns).
    param(
        [Parameter(Mandatory)]$Bounds,
        [Parameter(Mandatory)][string]$Name
    )
    try {
        $w = [int]$Bounds.Width
        $h = [int]$Bounds.Height
        $x = [int]$Bounds.X
        $y = [int]$Bounds.Y
        if ($w -le 0 -or $h -le 0 -or $w -gt 4000 -or $h -gt 4000) { return }

        # Move cursor away so it doesn't trigger hover tooltips (e.g., "Settings" on gear)
        Move-CursorAway

        $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        $gfx.CopyFromScreen($x, $y, 0, 0, (New-Object System.Drawing.Size $w, $h))

        $dir = Get-FluidScreenshotDir
        $safe = ($Name -replace '[^A-Za-z0-9._-]', '_')
        $path = Join-Path $dir "$safe.png"
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $gfx.Dispose()
        $bmp.Dispose()
    } catch {
        Write-Host "       (screenshot failed for $Name)" -ForegroundColor DarkGray
    }
}

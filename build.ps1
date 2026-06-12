<#
.SYNOPSIS
    Build fluidMonitor (widget + service), produce installer, optionally install.

.DESCRIPTION
    1. dotnet publish Fluid.Service  (Release, win-x64, self-contained, single-file)
    2. dotnet publish Fluid.App      (Release, win-x64, self-contained, single-file)
    3. ISCC.exe installer\fluid.iss  -> installer\Output\fluidMonitor_installer_vX.YZ.exe
    4. Install with /LOG (skip with -SkipInstall; captures log to installer\Output\)
    5. Optional: collect diagnostic bundle (-Diagnostics)

    Tests are NOT run by this script. Run them explicitly when you want:
        powershell -ExecutionPolicy Bypass -File .\tests\Test-FluidMonitor.ps1 -Smoke
        powershell -ExecutionPolicy Bypass -File .\tests\Test-FluidMonitor.ps1            # Default tier
        powershell -ExecutionPolicy Bypass -File .\tests\Test-FluidMonitor.ps1 -All       # Default + Visual
        powershell -ExecutionPolicy Bypass -File .\tests\Test-FluidMonitor.ps1 -Visual    # Visual matrices

.NOTES
    Requires .NET 8 SDK and Inno Setup 6.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$InnoCompiler  = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    [switch]$SkipInstall,
    [switch]$Diagnostics       # Collect diagnostic bundle after build (separate from tests)
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath
$outputDir = Join-Path $root 'installer\Output'

function Publish-Project([string]$ProjectPath) {
    Write-Host "==> Publishing $ProjectPath" -ForegroundColor Cyan
    dotnet publish $ProjectPath -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $ProjectPath ($LASTEXITCODE)" }
}

Push-Location $root
try {
    Publish-Project ".\Fluid.Service\Fluid.Service.csproj"
    Publish-Project ".\Fluid.App\Fluid.App.csproj"

    if (-not (Test-Path $InnoCompiler)) {
        Write-Warning "ISCC.exe not found at '$InnoCompiler'."
        Write-Warning "Publish artifacts are under <project>\bin\Release\net8.0-windows\win-x64\publish\."
        return
    }

    Write-Host "==> Building installer" -ForegroundColor Cyan
    & $InnoCompiler ".\installer\fluid.iss"
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

    $out = Get-ChildItem $outputDir -Filter fluidMonitor_installer*.exe -ErrorAction SilentlyContinue |
           Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($out) {
        Write-Host ""
        Write-Host "Installer: $($out.FullName)" -ForegroundColor Green
        Write-Host "Size:      $([math]::Round($out.Length / 1MB, 1)) MB" -ForegroundColor Green
    }

    # Install with /LOG so we capture installation log
    if (-not $SkipInstall -and $out) {
        Write-Host ""
        Write-Host "==> Installing (silent, with log)..." -ForegroundColor Cyan
        $installLog = Join-Path $outputDir "install-log.txt"
        Start-Process $out.FullName -ArgumentList "/SILENT", "/LOG=`"$installLog`"" -Wait
        if (Test-Path $installLog) {
            Write-Host "Install log saved: $installLog" -ForegroundColor Green
        }
        Start-Sleep -Seconds 2
    }

    # Collect diagnostics if requested (off by default)
    if ($Diagnostics) {
        Write-Host ""
        Write-Host "==> Collecting diagnostic bundle..." -ForegroundColor Cyan
        $diagScript = Join-Path $root "tests\Collect-Diagnostics.ps1"
        if (Test-Path $diagScript) {
            & $diagScript
        } else {
            Write-Warning "Diagnostics script not found at $diagScript"
        }
    }
}
finally {
    Pop-Location
}

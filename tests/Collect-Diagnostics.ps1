<#
.SYNOPSIS
    Collect fluidMonitor diagnostic information into a single zip file.
.DESCRIPTION
    Bundles installer, test results, service logs, app config, event viewer
    entries, and system info into one zip at installer\Output\diagnostics-*.zip
    so it can be uploaded for support/debugging.
.EXAMPLE
    .\Collect-Diagnostics.ps1
    .\Collect-Diagnostics.ps1 -IncludeInstaller   # also bundles installer .exe (larger)
#>
param(
    [switch]$IncludeInstaller,
    [string]$OutputDir = "$PSScriptRoot\..\installer\Output"
)

$ErrorActionPreference = "Continue"  # never stop the bundle just because one log is missing

# Resolve paths
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }

$stamp   = Get-Date -Format "yyyy-MM-dd-HHmmss"
$staging = Join-Path $env:TEMP "fluidmonitor-diag-$stamp"
$zipPath = Join-Path $OutputDir "fluidMonitor-diagnostics-$stamp.zip"

New-Item -ItemType Directory -Path $staging -Force | Out-Null

Write-Host ""
Write-Host "Collecting diagnostics..." -ForegroundColor Cyan
Write-Host "  Staging: $staging"
Write-Host "  Output:  $zipPath"

function Add-Section([string]$Name, [scriptblock]$Body) {
    $path = Join-Path $staging "$Name.txt"
    Write-Host "  - $Name" -ForegroundColor DarkGray
    try {
        $output = & $Body 2>&1 | Out-String
        Set-Content -Path $path -Value $output -Encoding UTF8
    } catch {
        Set-Content -Path $path -Value "ERROR collecting ${Name}: $_" -Encoding UTF8
    }
}

# ---------------------------------------------------------------------------
# 1. System info
# ---------------------------------------------------------------------------
Add-Section "01-system-info" {
    "Generated: $(Get-Date)"
    ""
    "=== OS ==="
    Get-CimInstance Win32_OperatingSystem | Format-List Caption, Version, BuildNumber, OSArchitecture
    ""
    "=== .NET Runtimes ==="
    try { & dotnet --list-runtimes } catch { "dotnet CLI not on PATH" }
    ""
    "=== PowerShell ==="
    $PSVersionTable | Format-List
    ""
    "=== Computer ==="
    Get-CimInstance Win32_ComputerSystem | Format-List Manufacturer, Model, TotalPhysicalMemory, NumberOfLogicalProcessors
}

# ---------------------------------------------------------------------------
# 2. Service status (config, current state, process info)
# ---------------------------------------------------------------------------
Add-Section "02-service-status" {
    "=== Get-Service ==="
    Get-Service fluidsvc | Format-List *
    ""
    "=== sc.exe queryex (process ID, exit codes, etc.) ==="
    & sc.exe queryex fluidsvc
    ""
    "=== sc.exe qc (binary path, dependencies, startup) ==="
    & sc.exe qc fluidsvc
    ""
    "=== sc.exe qfailure (recovery actions) ==="
    & sc.exe qfailure fluidsvc
    ""
    "=== Win32_Service (process ID lookup) ==="
    Get-CimInstance Win32_Service -Filter "Name='fluidsvc'" |
        Format-List Name, DisplayName, ProcessId, State, StartMode, PathName, StartName
    ""
    "=== Service process memory ==="
    $svc = Get-CimInstance Win32_Service -Filter "Name='fluidsvc'"
    if ($svc -and $svc.ProcessId -gt 0) {
        $p = Get-Process -Id $svc.ProcessId -ErrorAction SilentlyContinue
        if ($p) {
            "PID         : $($p.Id)"
            "Process     : $($p.ProcessName)"
            "WorkingSet  : $([Math]::Round($p.WorkingSet64/1MB, 1)) MB"
            "Handles     : $($p.HandleCount)"
            "Threads     : $($p.Threads.Count)"
            "Started     : $($p.StartTime)"
            "Uptime      : $((Get-Date) - $p.StartTime)"
        }
    }
}

# ---------------------------------------------------------------------------
# 3. App process
# ---------------------------------------------------------------------------
Add-Section "03-app-process" {
    $p = Get-Process fluidMonitor -ErrorAction SilentlyContinue
    if ($p) {
        "=== fluidMonitor.exe running ==="
        $p | Format-List Id, ProcessName, WorkingSet64, HandleCount, StartTime, MainWindowTitle
        ""
        "WorkingSet  : $([Math]::Round($p.WorkingSet64/1MB, 1)) MB"
        "Threads     : $($p.Threads.Count)"
    } else {
        "fluidMonitor.exe not currently running"
    }
}

# ---------------------------------------------------------------------------
# 4. Named pipes
# ---------------------------------------------------------------------------
Add-Section "04-named-pipes" {
    "=== Pipes containing 'fluidMonitor' ==="
    [System.IO.Directory]::GetFiles("\\.\pipe\") | Where-Object { $_ -match 'fluidMonitor' }
    ""
    "=== All pipes (for context) ==="
    "Total pipe count: $(([System.IO.Directory]::GetFiles('\\.\pipe\')).Count)"
}

# ---------------------------------------------------------------------------
# 5. Event Viewer (Application log, filtered to anything fluid-related)
# ---------------------------------------------------------------------------
Add-Section "05-event-viewer" {
    "=== Application log entries mentioning 'fluid' (last 50) ==="
    try {
        Get-WinEvent -LogName Application -MaxEvents 500 -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -like "*fluid*" -or $_.ProviderName -like "*fluid*" } |
            Select-Object -First 50 TimeCreated, LevelDisplayName, ProviderName, Id, Message |
            Format-List
    } catch { "Could not read Application log: $_" }
    ""
    "=== .NET Runtime errors (last 20) ==="
    try {
        Get-WinEvent -LogName Application -MaxEvents 500 -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -eq ".NET Runtime" -and ($_.Message -like "*fluid*" -or $_.LevelDisplayName -eq "Error") } |
            Select-Object -First 20 TimeCreated, LevelDisplayName, Id, Message |
            Format-List
    } catch { "Could not read .NET Runtime events: $_" }
    ""
    "=== Service Control Manager events for fluidsvc ==="
    try {
        Get-WinEvent -LogName System -MaxEvents 500 -ErrorAction SilentlyContinue |
            Where-Object { $_.ProviderName -eq "Service Control Manager" -and $_.Message -like "*fluidsvc*" } |
            Select-Object -First 20 TimeCreated, LevelDisplayName, Id, Message |
            Format-List
    } catch { "Could not read SCM events: $_" }
}

# ---------------------------------------------------------------------------
# 6. App settings & user data
# ---------------------------------------------------------------------------
$appData = "$env:APPDATA\fluidMonitor"
$progData = "$env:ProgramData\fluidMonitor"

Add-Section "06-app-data-listing" {
    "=== %APPDATA%\fluidMonitor\ ==="
    if (Test-Path $appData) { Get-ChildItem -Path $appData -Recurse | Format-Table Mode, LastWriteTime, Length, FullName }
    else { "Not present" }
    ""
    "=== %ProgramData%\fluidMonitor\ ==="
    if (Test-Path $progData) { Get-ChildItem -Path $progData -Recurse | Format-Table Mode, LastWriteTime, Length, FullName }
    else { "Not present" }
}

# Copy settings.json (sanitize handshake key)
if (Test-Path "$appData\settings.json") {
    try {
        $settings = Get-Content "$appData\settings.json" -Raw
        Set-Content -Path (Join-Path $staging "06-settings.json") -Value $settings -Encoding UTF8
    } catch { Set-Content -Path (Join-Path $staging "06-settings.json") -Value "ERROR: $_" }
}

# Copy service config (sanitize handshake key)
if (Test-Path "$progData\config.json") {
    try {
        $cfg = Get-Content "$progData\config.json" -Raw | ConvertFrom-Json
        if ($cfg.PSObject.Properties.Name -contains 'handshakeKey') {
            $cfg.handshakeKey = "[REDACTED - $($cfg.handshakeKey.Length) chars]"
        }
        $cfg | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $staging "06-service-config.json") -Encoding UTF8
    } catch { Set-Content -Path (Join-Path $staging "06-service-config.json") -Value "ERROR: $_" }
}

# Copy any .log files from data dirs
$logDir = Join-Path $staging "service-logs"
foreach ($dir in @($appData, $progData)) {
    if (Test-Path $dir) {
        $logs = Get-ChildItem -Path $dir -Filter "*.log" -Recurse -ErrorAction SilentlyContinue
        if ($logs) {
            if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
            foreach ($log in $logs) {
                Copy-Item $log.FullName -Destination $logDir -Force
            }
        }
    }
}

# ---------------------------------------------------------------------------
# 7. Installation logs (Inno Setup)
# ---------------------------------------------------------------------------
Add-Section "07-install-logs-list" {
    "=== Inno Setup log files in %TEMP% ==="
    $logs = Get-ChildItem $env:TEMP -Filter "Setup Log*.txt" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 3
    if ($logs) { $logs | Format-Table LastWriteTime, Length, FullName }
    else { "No Setup Log files found in TEMP" }
}

# Copy 3 most recent Inno Setup log files
$installLogs = Get-ChildItem $env:TEMP -Filter "Setup Log*.txt" -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 3
foreach ($log in $installLogs) {
    Copy-Item $log.FullName -Destination $staging -Force
}

# ---------------------------------------------------------------------------
# 8. Test results (if present)
# ---------------------------------------------------------------------------
$testResults = "$PSScriptRoot\test-results.txt"
if (Test-Path $testResults) {
    Copy-Item $testResults -Destination (Join-Path $staging "08-test-results.txt") -Force
}
$coverageFile = "$PSScriptRoot\coverage.txt"
if (Test-Path $coverageFile) {
    Copy-Item $coverageFile -Destination (Join-Path $staging "08-coverage.txt") -Force
}

# ---------------------------------------------------------------------------
# 9. Installed file listing
# ---------------------------------------------------------------------------
Add-Section "09-install-tree" {
    "=== C:\Program Files (x86)\fluidMonitor\ ==="
    $installDir = "C:\Program Files (x86)\fluidMonitor"
    if (Test-Path $installDir) {
        Get-ChildItem -Path $installDir -Recurse | Format-Table Mode, LastWriteTime, Length, FullName -AutoSize
    } else { "Not installed at default location" }
}

# ---------------------------------------------------------------------------
# 10. Screenshots from latest test run (visual log of GUI states)
# ---------------------------------------------------------------------------
$screenshotDir = $env:FLUIDMON_SCREENSHOT_DIR
if (-not $screenshotDir -or -not (Test-Path $screenshotDir)) {
    # Fallback: look for the most recent screenshots folder in TEMP
    $candidate = Get-ChildItem $env:TEMP -Filter "fluidmon-screenshots-*" -Directory -EA SilentlyContinue |
                 Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($candidate) { $screenshotDir = $candidate.FullName }
}
if ($screenshotDir -and (Test-Path $screenshotDir)) {
    $shots = Get-ChildItem $screenshotDir -Filter "*.png" -EA SilentlyContinue
    if ($shots) {
        $destDir = Join-Path $staging "10-screenshots"
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        $totalKB = 0
        foreach ($shot in $shots) {
            Copy-Item $shot.FullName -Destination $destDir
            $totalKB += [Math]::Round($shot.Length / 1KB, 1)
        }
        Write-Host "  - 10-screenshots ($($shots.Count) images, ${totalKB}KB total)" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------
# 11. Latest installer .exe (optional, large)
# ---------------------------------------------------------------------------
if ($IncludeInstaller) {
    $latest = Get-ChildItem $OutputDir -Filter "fluidMonitor_installer*.exe" -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Copy-Item $latest.FullName -Destination $staging -Force
        Write-Host "  - installer.exe ($([Math]::Round($latest.Length/1MB,1)) MB)" -ForegroundColor DarkGray
    }
}

# ---------------------------------------------------------------------------
# Zip it up
# ---------------------------------------------------------------------------
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$staging\*" -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $staging -Recurse -Force

$size = [Math]::Round((Get-Item $zipPath).Length / 1KB, 1)
Write-Host ""
Write-Host "Diagnostic bundle: $zipPath" -ForegroundColor Green
Write-Host "Size: ${size} KB" -ForegroundColor Green
Write-Host ""
Write-Host "Upload this file to share diagnostic info." -ForegroundColor Cyan

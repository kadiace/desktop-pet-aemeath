param(
    [int]$PollMs = 300
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$BinDir = Join-Path $RepoRoot "src\AemeathCompanion\bin\Debug\net10.0-windows10.0.19041.0"
$ExePath = Join-Path $BinDir "AemeathCompanion.exe"
$ControlDir = Join-Path $RepoRoot ".dev-control"
$InstancePath = Join-Path $ControlDir "instance.json"
$StartSignalPath = Join-Path $ControlDir "start.signal"
$ShutdownSignalPath = Join-Path $ControlDir "shutdown.signal"
$WatcherPath = Join-Path $ControlDir "watcher.json"

New-Item -ItemType Directory -Force -Path $ControlDir | Out-Null

function Test-FileUnlocked {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return $true }
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $fs.Dispose()
        return $true
    } catch {
        return $false
    }
}

function Start-DesktopCompanion {
    if (-not (Test-Path $ExePath)) {
        Write-Host "[WATCH] Debug exe not found yet: $ExePath" -ForegroundColor Yellow
        return
    }
    if (-not (Test-FileUnlocked $ExePath)) {
        Write-Host "[WATCH] AemeathCompanion is already running." -ForegroundColor DarkGray
        return
    }
    if (Test-Path $ShutdownSignalPath) {
        Remove-Item -Force $ShutdownSignalPath -ErrorAction SilentlyContinue
    }
    $p = Start-Process -FilePath $ExePath -WorkingDirectory $BinDir -WindowStyle Normal -PassThru
    Write-Host "[WATCH] Started AemeathCompanion on desktop (PID=$($p.Id))." -ForegroundColor Green
}

$watcherMeta = @{
    pid = $PID
    startedAtUtc = [DateTime]::UtcNow.ToString("O")
} | ConvertTo-Json
Set-Content -Path $WatcherPath -Value $watcherMeta -Encoding UTF8

Write-Host "[WATCH] Local desktop watcher active for AemeathCompanion (Ctrl+C to stop)." -ForegroundColor Cyan
Write-Host "[WATCH] Control dir: $ControlDir" -ForegroundColor DarkGray

try {
    if (Test-Path $StartSignalPath) {
        Remove-Item -Force $StartSignalPath -ErrorAction SilentlyContinue
    }
    Start-DesktopCompanion

    while ($true) {
        if (Test-Path $StartSignalPath) {
            Remove-Item -Force $StartSignalPath -ErrorAction SilentlyContinue
            Start-DesktopCompanion
        }
        Start-Sleep -Milliseconds $PollMs
    }
} finally {
    if (Test-Path $WatcherPath) {
        Remove-Item -Force $WatcherPath -ErrorAction SilentlyContinue
    }
    Write-Host "[WATCH] Desktop watcher stopped." -ForegroundColor DarkGray
}

param(
    [ValidateSet("Status", "Stop", "Build", "Start", "RequestStart", "Restart")]
    [string]$Action = "Restart",
    [int]$WaitSeconds = 8,
    [switch]$NoBuild,
    [switch]$Wait
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjPath = Join-Path $RepoRoot "src\AemeathCompanion\AemeathCompanion.csproj"
$BinDir = Join-Path $RepoRoot "src\AemeathCompanion\bin\Debug\net10.0-windows10.0.19041.0"
$ExePath = Join-Path $BinDir "AemeathCompanion.exe"
$DllPath = Join-Path $BinDir "AemeathCompanion.dll"
$ControlDir = Join-Path $RepoRoot ".dev-control"
$InstancePath = Join-Path $ControlDir "instance.json"
$SignalPath = Join-Path $ControlDir "shutdown.signal"
$StartSignalPath = Join-Path $ControlDir "start.signal"
$WatcherPath = Join-Path $ControlDir "watcher.json"
$NamedEvent = "Local\AemeathCompanion.Dev.Shutdown"

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

function Try-SignalNamedEvent {
    try {
        $ev = [System.Threading.EventWaitHandle]::OpenExisting($NamedEvent)
        try {
            $null = $ev.Set()
            return $true
        } finally {
            $ev.Dispose()
        }
    } catch {
        return $false
    }
}

function Show-DevStatus {
    $exeUnlocked = Test-FileUnlocked $ExePath
    $dllUnlocked = Test-FileUnlocked $DllPath
    $binaryLocked = (-not $exeUnlocked) -or (-not $dllUnlocked)

    if (Test-Path $InstancePath) {
        $raw = Get-Content -Raw -Path $InstancePath
        Write-Host "[STATUS] Dev instance metadata found ($InstancePath):" -ForegroundColor Cyan
        Write-Host $raw
    } else {
        Write-Host "[STATUS] No .dev-control/instance.json found." -ForegroundColor DarkGray
    }

    if ($binaryLocked) {
        Write-Host "[STATUS] Debug binary is currently LOCKED (app is running)." -ForegroundColor Yellow
    } else {
        Write-Host "[STATUS] Debug binary is UNLOCKED (ready to build)." -ForegroundColor Green
    }

    if (Test-Path $WatcherPath) {
        Write-Host "[STATUS] Desktop watcher (dev-watch.ps1) metadata present." -ForegroundColor Cyan
    } else {
        Write-Host "[STATUS] Desktop watcher (dev-watch.ps1) is not running." -ForegroundColor DarkGray
    }
}

function Invoke-DevStop {
    $hasInstance = Test-Path $InstancePath
    $binaryLocked = (-not (Test-FileUnlocked $ExePath)) -or (-not (Test-FileUnlocked $DllPath))
    $eventSignaled = Try-SignalNamedEvent

    if (-not $hasInstance -and -not $binaryLocked -and -not $eventSignaled) {
        Write-Host "[STOP] No running local Debug AemeathCompanion instance detected." -ForegroundColor DarkGray
        return
    }

    New-Item -ItemType Directory -Force -Path $ControlDir | Out-Null
    $token = "shutdown $([DateTime]::UtcNow.ToString('O')) $([Guid]::NewGuid().ToString('N'))"
    Set-Content -Path $SignalPath -Value $token -Encoding UTF8 -NoNewline
    $null = Try-SignalNamedEvent

    Write-Host "[STOP] Sent local dev shutdown signal. Waiting up to ${WaitSeconds}s for clean exit..." -ForegroundColor Cyan

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $instanceGone = -not (Test-Path $InstancePath)
        $unlocked = (Test-FileUnlocked $ExePath) -and (Test-FileUnlocked $DllPath)
        if ($instanceGone -and $unlocked) {
            if (Test-Path $SignalPath) { Remove-Item -Force $SignalPath -ErrorAction SilentlyContinue }
            Write-Host "[STOP] AemeathCompanion exited cleanly and binary is unlocked." -ForegroundColor Green
            return
        }
        Start-Sleep -Milliseconds 150
    }

    if (Test-Path $SignalPath) { Remove-Item -Force $SignalPath -ErrorAction SilentlyContinue }
    throw "Timed out waiting for AemeathCompanion to exit. If an older build (before DevLifecycleControl) is running, please close it once from the tray/menu and relaunch the new Debug build."
}

function Invoke-DevBuild {
    Write-Host "[BUILD] Building Debug AemeathCompanion..." -ForegroundColor Cyan
    dotnet build -c Debug --no-restore $ProjPath
    if ($LASTEXITCODE -ne 0) {
        throw "Debug build failed with exit code $LASTEXITCODE"
    }
    Write-Host "[BUILD] Build succeeded." -ForegroundColor Green
}

function Invoke-DevStart {
    if (-not (Test-Path $ExePath)) {
        throw "Debug executable not found at '$ExePath'. Run with -Action Build first."
    }

    if (Test-Path $SignalPath) { Remove-Item -Force $SignalPath -ErrorAction SilentlyContinue }

    $prevContent = if (Test-Path $InstancePath) { Get-Content -Raw -Path $InstancePath } else { $null }
    $launchUtc = [DateTime]::UtcNow.AddSeconds(-1)

    Write-Host "[START] Launching $ExePath..." -ForegroundColor Cyan
    $proc = Start-Process -FilePath $ExePath -WorkingDirectory $BinDir -WindowStyle Normal -PassThru

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path $InstancePath) {
            try {
                $raw = Get-Content -Raw -Path $InstancePath
                $info = $raw | ConvertFrom-Json
                $startedUtc = [DateTime]::Parse($info.startedAtUtc).ToUniversalTime()
                if (($raw -ne $prevContent) -and ($startedUtc -ge $launchUtc)) {
                    Write-Host "[START] AemeathCompanion is running (PID=$($info.pid), Config=$($info.configuration))." -ForegroundColor Green
                    if ($Wait) {
                        $proc.WaitForExit()
                        Write-Host "[START] AemeathCompanion (PID=$($info.pid)) exited with code $($proc.ExitCode)." -ForegroundColor DarkGray
                    }
                    return
                }
            } catch {
                # File may still be flushing
            }
        }
        Start-Sleep -Milliseconds 150
    }

    throw "Launched process (PID=$($proc.Id)) did not register .dev-control/instance.json within ${WaitSeconds}s. It may have exited due to single-instance mutex or sandbox desktop isolation."
}

function Invoke-DevRequestStart {
    if (-not (Test-Path $ExePath)) {
        throw "Debug executable not found at '$ExePath'. Run with -Action Build first."
    }
    if (-not (Test-Path $WatcherPath)) {
        throw "Desktop watcher (scripts/dev-watch.ps1) is not running. Start '.\scripts\dev-watch.ps1' once in your desktop terminal to enable automatic visible relaunch."
    }

    if (Test-Path $SignalPath) { Remove-Item -Force $SignalPath -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path $ControlDir | Out-Null

    $prevContent = if (Test-Path $InstancePath) { Get-Content -Raw -Path $InstancePath } else { $null }
    $launchUtc = [DateTime]::UtcNow.AddSeconds(-1)
    Set-Content -Path $StartSignalPath -Value "start $([DateTime]::UtcNow.ToString('O')) $([Guid]::NewGuid().ToString('N'))" -Encoding UTF8 -NoNewline
    Write-Host "[START] Sent start signal to desktop watcher. Waiting up to ${WaitSeconds}s..." -ForegroundColor Cyan

    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ((Test-Path $InstancePath) -and (-not (Test-FileUnlocked $ExePath))) {
            try {
                $raw = Get-Content -Raw -Path $InstancePath
                $info = $raw | ConvertFrom-Json
                $startedUtc = [DateTime]::Parse($info.startedAtUtc).ToUniversalTime()
                if (($raw -ne $prevContent) -and ($startedUtc -ge $launchUtc)) {
                    Write-Host "[START] Desktop watcher launched AemeathCompanion visibly (PID=$($info.pid), Config=$($info.configuration))." -ForegroundColor Green
                    return
                }
            } catch {
                # File may still be flushing
            }
        }
        Start-Sleep -Milliseconds 150
    }

    throw "Desktop watcher did not launch AemeathCompanion within ${WaitSeconds}s. Check the terminal running '.\scripts\dev-watch.ps1'."
}

switch ($Action) {
    "Status" {
        Show-DevStatus
    }
    "Stop" {
        Invoke-DevStop
    }
    "Build" {
        Invoke-DevBuild
    }
    "Start" {
        Invoke-DevStart
    }
    "RequestStart" {
        Invoke-DevRequestStart
    }
    "Restart" {
        Invoke-DevStop
        if (-not $NoBuild) {
            Invoke-DevBuild
        }
        if (Test-Path $WatcherPath) {
            Invoke-DevRequestStart
        } else {
            Invoke-DevStart
        }
    }
}

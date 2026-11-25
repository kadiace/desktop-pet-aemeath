param(
    [string]$Version = "",
    [string]$Notes = "",
    [switch]$CreateTag,
    [switch]$SelfContained,
    [switch]$ForceOverwrite
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjPath = Join-Path $RepoRoot "src\AemeathCompanion\AemeathCompanion.csproj"
$ReleasesDir = Join-Path $RepoRoot "releases"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $latestTag = (git -c safe.directory=* -C $RepoRoot for-each-ref --sort=-version:refname --format="%(refname:short)" refs/tags | Select-Object -First 1)
    if ([string]::IsNullOrWhiteSpace($latestTag)) {
        throw "No git tags found. Pass -Version (e.g. -Version 0.1.0 -CreateTag)."
    }
    $Version = $latestTag
}

$cleanVersion = $Version.Trim().TrimStart('v', 'V')
$tag = "v$cleanVersion"

$zipPath = Join-Path $ReleasesDir "AemeathCompanion-$tag.zip"
if ((Test-Path $zipPath) -and (-not $ForceOverwrite)) {
    throw "Release archive '$zipPath' already exists. Never overwrite an already-published version; bump to a new version (e.g. next patch) instead."
}

if ($CreateTag) {
    $dirty = git -c safe.directory=* -C $RepoRoot status --porcelain
    if (-not [string]::IsNullOrWhiteSpace($dirty)) {
        throw "Working tree has uncommitted changes. Commit your changes before running with -CreateTag."
    }
    $existing = git -c safe.directory=* -C $RepoRoot tag -l $tag
    if ([string]::IsNullOrWhiteSpace($existing)) {
        $msg = if ([string]::IsNullOrWhiteSpace($Notes)) { "Release $tag" } else { $Notes }
        git -c safe.directory=* -C $RepoRoot tag -a $tag -m $msg
        if ($LASTEXITCODE -ne 0) {
            throw "git tag failed with exit code $LASTEXITCODE"
        }
        Write-Host "[OK] Created git tag $tag" -ForegroundColor Cyan
    } else {
        throw "Git tag '$tag' already exists. Never reuse or move a published tag; bump to a new version instead."
    }
}

$stageDir = Join-Path $RepoRoot "artifacts\stage-$tag"
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force -Path $stageDir, $ReleasesDir | Out-Null

$scFlag = if ($SelfContained) { "true" } else { "false" }
Write-Host "[BUILD] Publishing AemeathCompanion $tag (SelfContained=$scFlag)..." -ForegroundColor Cyan

dotnet publish $ProjPath `
    -c Release `
    -r win-x64 `
    --self-contained $scFlag `
    -p:Version=$cleanVersion `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $stageDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$sizeMb = [Math]::Round((Get-Item $zipPath).Length / 1MB, 2)

Remove-Item -Recurse -Force $stageDir

Write-Host ""
Write-Host "[DONE] Packaged $tag" -ForegroundColor Green
Write-Host "  Archive : $zipPath ($sizeMb MB)"
Write-Host "  SHA-256 : $hash"
Write-Host "  Portal  : http://172.21.10.15:17515/"
Write-Host "  Download: http://172.21.10.15:17515/download/$tag"

# Notify running LAN release server (if active) so open browser portals auto-refresh immediately
try {
    $null = Invoke-RestMethod -Method Post -Uri "http://127.0.0.1:17515/api/webhook/refresh" -TimeoutSec 2 -ErrorAction Stop
    Write-Host "  LiveHook: Notified connected web portals to refresh" -ForegroundColor Cyan
} catch {
    # Server not running locally — ignore silently
}

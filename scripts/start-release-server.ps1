param(
    [int]$Port = 17515,
    [string]$PublicHost = "172.21.10.15"
)

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ServerScript = Join-Path $RepoRoot "server\release-server.mjs"

Write-Host "Starting Aemeath LAN Release Server on http://${PublicHost}:${Port}/ ..." -ForegroundColor Cyan
Write-Host "Tip: If teammates cannot connect, open TCP port $Port in Windows Firewall (Admin PowerShell):" -ForegroundColor DarkGray
Write-Host "  New-NetFirewallRule -DisplayName 'Aemeath Release Server' -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow" -ForegroundColor DarkGray
Write-Host ""

& node $ServerScript --host 0.0.0.0 --port $Port --public-host $PublicHost

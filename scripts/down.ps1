# Stops the example API instances (ports 5001-5009). Add -Ruvio to stop the Ruvio containers as well.
param([switch]$Ruvio)

Get-NetTCPConnection -LocalPort (5001..5009) -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }

if ($Ruvio) {
    docker compose -f "$PSScriptRoot\..\infra\docker-compose.yml" down
}

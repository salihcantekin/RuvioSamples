# Starts the local Ruvio preview server and its UI (infra/docker-compose.yml).
#   Ruvio: 127.0.0.1:6379      UI: http://127.0.0.1:8080
docker compose -f "$PSScriptRoot\..\infra\docker-compose.yml" up -d

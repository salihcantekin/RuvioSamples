# Ruvio Examples

Examples for the Ruvio YouTube series: in every video a real scenario, the problem in it, and the Ruvio feature that solves it.

| # | Example | Ruvio feature | Folder |
|---|---------|---------------|--------|
| 01 | Rate limiting | `LIMIT` (fixed window, sliding window, token bucket), outage modes | [`examples/01-rate-limiting`](examples/01-rate-limiting) |

Ideas for next: Fill (cache stampede), Distributed lock, Lease/Release, Distributed counters.

## Layout

```
RuvioExamples/
├─ infra/                  docker-compose: Ruvio preview server + UI
├─ shared/
│  ├─ RuvioExamples.LoadKit     load generation: virtual clients, fake delays, multiple instances, statistics (no UI)
│  └─ RuvioExamples.Desktop     "Ruvio Load Console" (WinForms): a wizard that asks questions + a live panel, shared by all examples
├─ examples/
│  └─ 01-rate-limiting/
│     ├─ RateLimiting.Api       sample API (naive, fixed, sliding, bucket)
│     └─ console.json          what the console needs to know about this example: endpoints, scenarios, labels
├─ docs/                   video flow, live-coding steps, preview notes
└─ scripts/                up / api / console / down
```

## Quick start

```powershell
.\scripts\up.ps1          # Ruvio (6379) + UI (http://127.0.0.1:8080)
.\scripts\api.ps1         # api-1 (5001) and api-2 (5002), in separate windows
.\scripts\console.ps1     # Ruvio Load Console
```

Pick a scenario in the console (`1 · No limit` … `5 · Ruvio goes down`) and press **Start**. Use **Reset limits** between takes.

Requirements: Docker Desktop, .NET 10 SDK, Windows (the console is WinForms).

## Preview versions

This repo is written against Ruvio's **preview** packages and server. The versions live in one place: `Directory.Packages.props` and
`infra/docker-compose.yml`. What is preview-specific: [docs/preview-features.md](docs/preview-features.md).

## Docs

- [docs/00-load-console.md](docs/00-load-console.md): the shared load console, command-line options, how to connect a new example
- [docs/01-rate-limiting/README.md](docs/01-rate-limiting/README.md): video flow, live-coding steps, troubleshooting
- [docs/preview-features.md](docs/preview-features.md): preview-specific changes

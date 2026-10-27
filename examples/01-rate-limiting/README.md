# 01 · Rate limiting

`RateLimiting.Api`: four endpoints (`/api/naive`, `/api/fixed`, `/api/sliding`, `/api/bucket`), a fragile fake dependency (`FragileBackend`)
and `/api/stats`. Video flow, live-coding steps and preview notes: [docs/01-rate-limiting](../../docs/01-rate-limiting/README.md).

```powershell
.\scripts\up.ps1; .\scripts\api.ps1; .\scripts\console.ps1
```

Settings (`appsettings.json` → `Demo`, or from the command line as `--Demo:Key=value`): `Permits`, `WindowSeconds`, `Capacity`, `Outage`, `CircuitBreaker`, `KeyPrefix`, `Instance`.

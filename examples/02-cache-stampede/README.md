# 02 · Cache stampede

`CacheStampede.Api`: three endpoints for the same hot product (`/api/no-cache`, `/api/classic`, `/api/fill`), a fake database that takes `workMs` per query (`SlowDatabase`) and `/api/stats`.
Video flow, live-coding steps and preview notes: [docs/02-cache-stampede](../../docs/02-cache-stampede/README.md).

```powershell
.\scripts\up.ps1; .\scripts\api-cache.ps1; .\scripts\console.ps1
```

The Ruvio code is in [`Caching.cs`](CacheStampede.Api/Caching.cs): `ClassicCache` (GET / SET) and `FillCache` (FILLGET / FILLSET).

Settings (`appsettings.json` → `Demo`, or from the command line as `--Demo:Key=value`): `CacheSeconds`, `FillSeconds`, `KeyPrefix`, `Instance`.

# 01 · Rate limiting

`RateLimiting.Api`: dört endpoint (`/api/naive`, `/api/fixed`, `/api/sliding`, `/api/bucket`), kırılgan bir sahte bağımlılık (`FragileBackend`)
ve `/api/stats`. Video akışı, canlı kodlama adımları ve preview notları: [docs/01-rate-limiting](../../docs/01-rate-limiting/README.md).

```powershell
.\scripts\up.ps1; .\scripts\api.ps1; .\scripts\console.ps1
```

Ayarlar (`appsettings.json` → `Demo`, komut satırından `--Demo:Anahtar=değer`): `Permits`, `WindowSeconds`, `Capacity`, `Outage`, `CircuitBreaker`, `KeyPrefix`, `Instance`.

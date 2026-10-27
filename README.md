# Ruvio Examples

YouTube serisi için örnekler: her videoda gerçek bir senaryo, o senaryodaki sorun ve onu çözen Ruvio özelliği.

| # | Örnek | Ruvio özelliği | Klasör |
|---|-------|----------------|--------|
| 01 | Rate limiting | `LIMIT` (fixed window, sliding window, token bucket), outage modları | [`examples/01-rate-limiting`](examples/01-rate-limiting) |

Sıradaki fikirler: Fill (cache stampede), Distributed lock, Lease/Release, Distributed counters.

## Yapı

```
RuvioExamples/
├─ infra/                  docker-compose: Ruvio preview sunucusu + UI
├─ shared/
│  ├─ RuvioExamples.LoadKit     yük üretimi: sanal client'lar, fake bekleme, çoklu instance, istatistik (UI'sız)
│  └─ RuvioExamples.Desktop     "Ruvio Load Console" (WinForms): soru soran sihirbaz + canlı panel, tüm örneklerin ortak arayüzü
├─ examples/
│  └─ 01-rate-limiting/
│     ├─ RateLimiting.Api       örnek API (naive, fixed, sliding, bucket)
│     └─ console.json          konsolun bu örnek için bilgisi: endpoint, senaryo, etiketler
├─ docs/                   video akışı, canlı kodlama adımları, preview notları
└─ scripts/                up / api / console / down
```

## Hızlı başlangıç

```powershell
.\scripts\up.ps1          # Ruvio (6379) + UI (http://127.0.0.1:8080)
.\scripts\api.ps1         # api-1 (5001) ve api-2 (5002), ayrı pencerelerde
.\scripts\console.ps1     # Ruvio Load Console
```

Konsolda bir senaryo seç (`1 · No limit` … `5 · Ruvio goes down`), **Start**'a bas. Takeler arasında **Reset limits**.

Gerekenler: Docker Desktop, .NET 10 SDK, Windows (konsol WinForms).

## Preview sürümleri

Bu repo Ruvio'nun **preview** paketlerine ve sunucusuna yazıldı. Sürümler tek yerde: `Directory.Packages.props` ve
`infra/docker-compose.yml`. Neyin preview'a özel olduğu: [docs/preview-features.md](docs/preview-features.md).

## Dokümanlar

- [docs/00-load-console.md](docs/00-load-console.md): ortak yük konsolu, komut satırı seçenekleri, yeni örneğe nasıl bağlanır
- [docs/01-rate-limiting/README.md](docs/01-rate-limiting/README.md): video akışı, canlı kodlama adımları, sorun giderme
- [docs/preview-features.md](docs/preview-features.md): preview'a özel değişiklikler

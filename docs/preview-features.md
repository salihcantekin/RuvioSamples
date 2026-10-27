# Preview'a özel değişiklikler

Örnekler şu sürümlere karşı yazıldı ve çalıştırıldı (2026-10-27):

| Parça | Sürüm |
|-------|-------|
| `Ruvio.AspNetCore.RateLimiting` | `0.2.1-preview.2` (kararlı: `0.1.2`) |
| `Ruvio.Client.AspNetCore` / `Ruvio.Client` | `0.4.1-preview.33` (kararlı: `0.3.4`) |
| Ruvio sunucusu (`salihcantekin/ruvio`) | `0.3.5-preview.33` |
| Ruvio UI (`salihcantekin/ruvio-ui`) | `0.1.10-preview.33` |

## Rate limiting paketinde (videoda anlatılacaklar)

1. **Sliding window** (`RuvioRateLimitAlgorithm.SlidingWindow`). Önceki pencereyi, hâlâ üst üste bindiği oranda sayar. Fixed window'un
   sınır anındaki "çift burst" sorununu çözer.
2. **Token bucket** (`RuvioRateLimitAlgorithm.TokenBucket`). `Permits` kova boyutu, `window` boş kovanın dolma süresi. Düzgün dolum.
3. **Tam `Retry-After`**. Sliding ve token bucket sunucunun `LIMIT ... WITHRETRY` formunu kullanır, `Retry-After` gerçek bekleme olur.
   Fixed window hâlâ muhafazakâr davranır: her zaman pencerenin tamamı.
4. **Outage modları** (`options.Outage.Mode`):
   - `Throw` (varsayılan): Ruvio'ya ulaşılamazsa istisna uygulamaya gider (bizim örnekte 500).
   - `FailOpen`: limit atlanır, istek çalışır. Ruvio yokken limitsiz olursun.
   - `FailClosed`: `503` + (devre açıkken) `Retry-After`, istek çalışmaz.
5. **Circuit breaker** (`options.Outage.CircuitBreaker = new RuvioCircuitBreakerOptions { ... }`). Art arda `FailureThreshold` hatadan sonra
   `BreakDuration` boyunca Ruvio'ya hiç gitmez. Gerçek ölçüm: breaker yokken ilk iki istek bağlantı denemelerinde toplam ~8 s bekledi,
   devre açılınca cevaplar anında 503 oldu.

Bu örnekte doğrulanan davranışlar (Ruvio container'ı durdurularak):

| Mod | Ruvio yok | Ruvio geri geldi |
|-----|-----------|------------------|
| `Throw` | 500 (uygulamanın exception handler'ı) | normal |
| `FailOpen` | 200, limit yok | limitler yeniden işler |
| `FailClosed` + breaker | 503; 3 hatadan sonra anında 503 + `Retry-After: 10` | breaker süresi dolunca 200 |

## Client tarafında (bu örneklerde kullanılmıyor, ileride işe yarar)

`Ruvio.Client 0.4.1-preview` değişikliklerinden bazıları: `LimitSlidingAsync`, `LimitTokensAsync`, `...WithRetryAsync` ve `LimitOutcome`;
`GetJsonAsync<T>` / `SetJsonAsync<T>` / `FillGetJsonAsync<T>` tipli yardımcıları (Fill örneği için);
`RuvioClientOptions.CommandTimeout` (varsayılan **30 sn**, davranış değişikliği); failover takibi (`FollowPrimary`, `Seeds`);
cluster `ASK`/`TRYAGAIN` yeniden denemeleri.

## Dikkat

- Bir policy, ilk kullanıldığı algoritmayı korur. Algoritma değişirse **yeni policy adı** gerekir (örnekte `demo-fixed`, `demo-sliding`, `demo-bucket`).
- Pencere/permit değerini canlı pencerede değiştirmek saklanan durumu sıfırlamaz. Videoda ayar değiştirirsen önce **Reset limits**.
- `PartitionKey` örnekte `X-Client-Id` header'ından geliyor (tek konsol çok kullanıcı oynasın diye). Gerçek API'de güvenilir bir kimlik (kullanıcı, API anahtarı, tenant) olmalı.
- Sürümleri güncellerken `Directory.Packages.props` ve `infra/docker-compose.yml` birlikte değişmeli; eski preview sunucu `LIMIT` modlarını bilmeyebilir.

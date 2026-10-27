# 01 · Rate limiting

**Senaryo:** Bir API var, arkasında yavaş ve kırılgan bir bağımlılık (veritabanı / ödeme sağlayıcı). Sekiz yoğun client istek yağdırınca
bağımlılık yavaşlıyor, sonra istek reddetmeye başlıyor. Limit ekleyeceğiz; API'nin birden çok instance'ı olunca da bütçe tutarlı kalsın.

**Ruvio özelliği:** `LIMIT` komutu (sunucuda atomik sayaç) ve `Ruvio.AspNetCore.RateLimiting` ara katmanı.
Sayaç Ruvio'da durduğu için API'nin kaç instance'ı olursa olsun bütçe ortak.

Kod: [`examples/01-rate-limiting/RateLimiting.Api`](../../examples/01-rate-limiting/RateLimiting.Api). Ortak arayüz: [00-load-console.md](../00-load-console.md).

## Konsolda ne yapılır?

Konsol açılınca soru sorar (hangi instance, hangi endpoint, kaç client, hız, süre, fake bekleme) ve testi bir cümleyle özetler.
Videoda sihirbazla başlamak, sonra "Skip the questions" ile hazır senaryolara geçmek iyi çalışır. Ayrıntı: [00-load-console.md](../00-load-console.md).
Bu örneğin endpoint'leri, senaryoları ve etiketleri [`console.json`](../../examples/01-rate-limiting/console.json) içindedir.

## Çekim öncesi

```powershell
.\scripts\up.ps1              # Ruvio preview + UI (http://127.0.0.1:8080)
.\scripts\api.ps1             # api-1 :5001, api-2 :5002
.\scripts\console.ps1         # Load Console
```

Her takeden önce konsolda **Reset limits**. Tarayıcıda Ruvio UI'ı açık tut: anahtarlar `demo:rate:v1:...` önekiyle görünür (id'ler hash'li).

## Policy nedir, Ruvio'ya nasıl eklenir?

Ruvio tarafında **kaydedilecek bir policy yok**. Policy, `WithRuvioRateLimit` (ya da `[RuvioRateLimit]`) içine yazdığın **isim**:

```csharp
.WithRuvioRateLimit("demo-checkout", permits: 3, window: TimeSpan.FromSeconds(10))
```

Bir istek geldiğinde ara katman `KeyPrefix + hash(policy adı + bölüm anahtarı)` anahtarıyla Ruvio'ya tek bir atomik `LIMIT` komutu gönderir.
Sayaç ilk istekte kendiliğinden oluşur. Sonuçları:

- **Aynı isim = ortak bütçe.** İki endpoint aynı policy adını kullanırsa tek bütçeyi harcar (`/api/shop/products` + `/api/shop/cart` = `demo-shop`).
- **Farklı isim = ayrı bütçe.** `demo-checkout` kendi sayacıdır, başka endpoint'lerin kullanımından etkilenmez.
- **Bölüm anahtarı** (`PartitionKey`, örnekte `X-Client-Id`) bütçeyi kullanıcı başına böler: her client'ın her policy için ayrı sayacı vardır.
- İsim, izin sayısı ve pencere kodda durur; bir policy'nin ayarı değişirse eski sayaç yeni ayara geçmez, yeni isim ver ya da **Reset limits**.

### Bu örnekteki policy'ler

| Policy | Nerede | Bütçe |
|--------|--------|-------|
| `demo-global` | `app.MapGroup("/api")` grubunda | 30 / 10 sn. **Kendi policy'si olmayan** her `/api/*` endpoint'i (`/api/ping`, `/api/time`) |
| `demo-fixed`, `demo-sliding`, `demo-bucket` | tek tek endpoint'te | 10 / 10 sn, farklı algoritmalar |
| `demo-attribute` | `[RuvioRateLimit]` ile | 10 / 10 sn, aynı kural attribute olarak |
| `demo-shop` | `/api/shop` grubunda | 10 / 10 sn, `products` ve `cart` ortak |
| `demo-checkout` | `/api/shop/checkout` | **3** / 10 sn, daha sıkı özel policy |

**Genel limit + özel policy:** Grup ya da endpoint'e en yakın tanım kazanır; `GetMetadata` en son (en özel) policy'yi okur. Yani grupta tanımlı `demo-global`,
kendi policy'si olan bir endpoint için **geçerli olmaz, onun yerine geçer**; iki limit üst üste uygulanmaz. Ölçüldü: `/api/fixed` 10. istekten sonra 429
(kendi policy'si), `/api/ping` + `/api/time` birlikte 30. istekten sonra 429 (global), `/api/shop/checkout` 3. istekten sonra 429, `/api/naive` hiç 429 almadı
(gruba bağlı değil, haritası `app.MapGet("/api/naive")`).
Hem genel hem özel limiti aynı anda uygulamak gerekirse bunu bu paket yapmaz; iki farklı isim için iki ayrı kontrol yazmak gerekir.

## Video akışı

| Bölüm | Konsol senaryosu | Ne gösterilir | Süre |
|------|------------------|---------------|------|
| 1. Sorun | `1 · No limit` | 8 client x 8 istek/sn, sahte iş 400 ms. Gecikme p95 yükselir, sonra 503'ler. `/api/stats` ile `peakInFlight` kapasiteyi aşar | 2-3 dk |
| 2. Çözüm | `2 · Fixed window` | `AddRuvioRateLimiting` + `WithRuvioRateLimit`. 10 istek/10 sn/client; 11. istekten itibaren 429 + `Retry-After` | 3-4 dk |
| 3. Dağıtık | `3 · Two instances` | Aynı client iki instance'a dağılıyor, bütçe yine 10. "Served by" iki instance'ı da gösterir; sayaç Ruvio'da | 2 dk |
| 4. Sınır sorunu (preview) | `4 · Window edge` | Fixed: 20 istek 1 saniyede geçti. Sliding ve token bucket: 10. Tam `Retry-After` | 3 dk |
| 5. Ruvio çökerse (preview) | `5 · Ruvio goes down` | Container'ı durdur: `Throw` 500, `FailOpen` limitsiz, `FailClosed` 503, circuit breaker ile anında 503 | 3 dk |

## Canlı kodlama adımları

`Program.cs` yalnızca dersin kodunu içerir (demo yardımcıları `DemoSupport.cs` ve `FragileBackend.cs` içinde). Videoda silip yeniden yazılacak parçalar:

### Adım 1: sorun (kod hazır)

`/api/naive` endpoint'i limitsiz. `FragileBackend` kapasitesi 12 paralel istek; üstünde gecikme büyür, 2x'te 503 döner.

### Adım 2: limiti ekle

```csharp
// paketler: Ruvio.Client.AspNetCore + Ruvio.AspNetCore.RateLimiting
builder.Services.AddRuvioClient(builder.Configuration.GetSection("Ruvio"));

builder.Services.AddRuvioRateLimiting(options =>
{
    options.KeyPrefix = "demo:rate:v1:";
    options.PartitionKey = context => context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "anonymous";
});

app.UseRouting();
app.UseRuvioRateLimiting();   // UseRouting'den sonra

api.MapGet("/fixed", handler)
   .WithRuvioRateLimit("demo-fixed", permits: 10, window: TimeSpan.FromSeconds(10));
```

Anlatılacaklar: bölüm anahtarı (kimin bütçesi?), middleware yalnızca işaretli endpoint'lerde çalışır, tek atomik komut, ham kimlik saklanmaz (hash'li anahtar),
429'da `Retry-After` pencerenin tamamı.

### Adım 2b: Microsoft'un kendi limiter'ı (yorum satırında)

`Program.cs` içindeki yorumlu bloklar (`using`, `AddRateLimiter`, `app.UseRateLimiter()` ve `/api/ms-fixed` endpoint'i) açılınca
`Microsoft.AspNetCore.RateLimiting` ile aynı kural (10 istek / 10 sn, client başına) çalışır; derlendiği doğrulandı.
Mesaj: bu limiter **her instance'ın kendi belleğinde** sayar. İki instance'a istek dağıtınca toplam bütçe 20 olur (instance sayısı kadar kat),
Ruvio ile 10 kalır. Gösterim: yorumları aç, `.\scriptspi.ps1` ile iki instance başlat, konsolda `microsoft limiter · in-process` endpoint'ini seç, iki adresle çalıştır.

### Adım 3: ikinci instance

Kod değişmez. `.\scripts\api.ps1 -Count 2` ile iki pencere. Konsolda iki adres. Bütçenin toplamda 10 kaldığını göster.

### Adım 4: preview algoritmalar

```csharp
api.MapGet("/sliding", handler)
   .WithRuvioRateLimit("demo-sliding", 10, TimeSpan.FromSeconds(10), RuvioRateLimitAlgorithm.SlidingWindow);

api.MapGet("/bucket", handler)
   .WithRuvioRateLimit("demo-bucket", 10, TimeSpan.FromSeconds(10), RuvioRateLimitAlgorithm.TokenBucket);
```

Aynı `4 · Window edge` senaryosunu üç endpoint'te çalıştır. Beklenen (ölçüldü): fixed 20 izin / 0 red, sliding 10-11 izin / 9-10 red, bucket 10 izin / 10 red.
Sliding ve bucket'ta `Retry-After` gerçek bekleme (ör. 1 sn), fixed'ta 10 sn.

### Adım 5: Ruvio çökerse (preview)

```csharp
options.Outage.Mode = RuvioOutageMode.FailClosed;          // Throw | FailOpen | FailClosed
options.Outage.CircuitBreaker = new RuvioCircuitBreakerOptions
{
    FailureThreshold = 3,
    BreakDuration = TimeSpan.FromSeconds(10),
};
```

Moda göre API'yi yeniden başlat: `.\scripts\api.ps1 -Outage FailClosed -CircuitBreaker`. `docker stop ruvio-examples` ile Ruvio'yu durdur, `docker start ruvio-examples` ile geri getir.
Bir karar sorusu olarak sun: ödeme API'si FailClosed mu, genel okuma API'si FailOpen mi?

## Ekran görüntüsü / test

```powershell
.\scripts\console.ps1 --preset 4 --endpoint sliding --autostart --seconds 14 --snapshot out\edge-sliding.png
```

## Sorun giderme

| Belirti | Neden / çözüm |
|---------|---------------|
| Sürekli 500 | API Ruvio'ya bağlanamıyor: `.\scripts\up.ps1`, `docker ps`. `Ruvio:Host/Port` `appsettings.json` içinde |
| Hiç 429 yok | Önceki takeden kalan sayaçlar pencere dışında olabilir; ya da limit/süre değişti. **Reset limits** |
| Algoritma değişti ama davranış aynı | Policy ilk algoritmayı korur: yeni policy adı ver veya **Reset limits** |
| Konsolda hep `ERR` | API çalışmıyor ya da adres yanlış; `curl http://localhost:5001/api/stats` |
| Port dolu | `.\scripts\down.ps1` |

# Ruvio Load Console

Tüm örneklerin ortak test arayüzü (`shared/RuvioExamples.Desktop`, WinForms). Sanal client'lar, fake bekleme, birden çok API instance'ı.
k6 gibi bir performans ölçüm aracı değil: amaç, videoda "şu an ne oluyor" sorusunu **her isteği tek tek göstererek** cevaplamak.

## Açılış: soru soran sihirbaz

Konsol açılınca sırayla şunları sorar, sonunda testi tek bir cümleyle özetler ve "Start the test" der:

1. Hangi API instance'ları? (bir ya da ikisi birden)
2. Hangi endpoint? (seçince altında ne beklendiği yazar)
3. Trafik şekli (yalnızca örnek burst desenleri tanımlıyorsa): düzenli akış ya da desen
4. Kaç sanal client? (+ hepsi aynı kimliği mi paylaşsın)
5. Client başına saniyede kaç istek? (düzenli akışta)
6. Ne kadar sürsün?
7. Sunucu fake olarak yavaş olsun mu (ms)?

"Skip the questions" ayrıntılı paneli açar; panelden "Ask me questions instead" ile sihirbaza dönülür.

## Ayrıntılı panel

- **Üstte sayaçlar**: gönderilen, başarılı (2xx), reddedilen (örnekte 429), 503/hata, anlık istek/sn, gecikme p50/p95.
- **Grafik**: son 60 saniye, saniye başına yığılmış çubuk.
- **Clients**: her sanal client bir satır; her istek bir blok, en yeni sağda.
- **Served by**: hangi API instance'ı kaç isteği cevapladı.
- **Last responses**: son cevaplar (durum, client, gecikme, instance, `Retry-After`).
- Solda: hazır senaryolar, endpoint, instance adresleri, trafik, client sayısı, hız, fake iş, jitter, client başlangıç gecikmesi, süre.
- Reset düğmesi (örnek tanımlıyorsa): örneğin API'sindeki reset endpoint'ini her instance'ta çağırır.

## Genel mi, örneğe özel mi?

`LoadKit` ve konsol hiçbir Ruvio özelliğini bilmez. Örneğe özel her şey örneğin klasöründeki **`console.json`** içindedir:

```
examples/01-rate-limiting/console.json
```

| Alan | Anlamı |
|------|--------|
| `title`, `description` | Sihirbaz ve panel başlığı |
| `targets` | API adresleri (sihirbazda seçilir) |
| `clientIdHeader`, `instanceHeader`, `workParameter` | Konsolun API ile konuşma sözleşmesi (varsayılan `X-Client-Id`, `X-Instance`, `?workMs=`) |
| `reset` | `path`, düğme etiketi, ipucu. Yoksa düğme gizlenir |
| `outcomes` | Sonuçların adı: `success.label`, `rejected.label` + `rejected.statuses` (hangi kodlar "reddedildi" sayılır) |
| `endpoints` | `id`, `label`, `path`, `method`, `expect` (ne beklenmeli, özet ekranında gösterilir) |
| `bursts` | Zamanlanmış trafik: `steps` (`atSeconds`, `count`) ve `cycleSeconds` |
| `presets` | Hazır senaryolar (ayrıntılı panelde) |

Birden fazla örnekte `console.json` varsa sihirbazın ilk adımı "hangi örnek?" olur. Yeni örnek eklemek konsol koduna dokunmadan `console.json` yazmaktır;
yeni bir sonuç türü (ör. "kilit alındı / beklendi") gerekirse yalnızca `outcomes` etiketleri değişir, 2xx/429/503/diğer sınıflaması aynı kalır.

## Komut satırı (kayıt ve doğrulama için)

```powershell
.\scripts\console.ps1                                   # sihirbazla aç
.\scripts\console.ps1 --example rate --preset 2         # ayrıntılı panel, senaryo 2 seçili
.\scripts\console.ps1 --preset 4 --endpoint sliding     # endpoint: id, ad ya da yol
.\scripts\console.ps1 --preset 3 --autostart --seconds 20 --snapshot out\two-instances.png
.\scripts\console.ps1 --step 4 --snapshot out\step4.png            # sihirbazın 4. adımının ekran görüntüsü
.\scripts\console.ps1 --wizard-auto --seconds 10 --snapshot out\run.png   # tüm varsayılanlarla sihirbazı bitir, çalıştır
un.png   # tüm varsayılanlarla sihirbazı bitir, çalıştır
un.png   # tüm varsayılanlarla sihirbazı bitir, çalıştır
```

`--snapshot` ile süre dolunca ekran görüntüsü alıp kapanır. Git Bash `/api/...` yolunu Windows yoluna çevirir; id'yi (`sliding`) yaz.

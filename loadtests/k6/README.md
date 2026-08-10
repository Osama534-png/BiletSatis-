# k6 Yük Testleri

Bu klasördeki testler, BiletSatis uygulamasının eşzamanlılık (race condition) korumalarını
gerçek yük altında doğrular.

## Ön koşullar

- [k6](https://k6.io/docs/get-started/installation/) kurulu olmalı.
- Uygulama **`yuktest` profiliyle** çalışıyor olmalı (aşağıya bakınız): `dotnet run --project BiletSatis.Web --launch-profile yuktest` (varsayılan: `http://localhost:5052`).
- Veritabanında en az bir etkinlik ve "Satışta" durumda biletler bulunmalı (varsayılan seed verisi yeterli).
- Uygulama genelinde giriş zorunlu olduğu için her sanal kullanıcı (VU) testin başında **kendi
  tek kullanımlık hesabını otomatik olarak oluşturup giriş yapar** — ayrıca bir şey yapmanıza
  gerek yok. Sadece admin işlemleri (satış başlatma, tanılama endpoint'i) için varsayılan admin
  hesabı (`admin@biletsatis.local` / `Admin123!`) kullanılır; farklıysa `ADMIN_EMAIL`/`ADMIN_SIFRE`
  ortam değişkenleriyle geçebilirsiniz.
- ⚠️ Testler gerçek veritabanına yazar ve **her çalıştırmada yeni test kullanıcıları oluşturur**
  (`yuktest-...@test.local`). Sık test ediyorsanız zaman zaman bu kayıtları ve kuyruk tablosunu
  temizlemek isteyebilirsiniz.

## Testler

### 1. add-to-cart-test.js — Oversell (double booking) yok testi

Onlarca sanal kullanıcı aynı anda tek bir bilete "Sepete Ekle" gönderir.

```bash
k6 run loadtests/k6/add-to-cart-test.js
```

**Assertion (threshold):** `sepete_ekleme_basarili` sayacı tam olarak `1` olmalı — kaç eşzamanlı
istek gelirse gelsin, bilet sadece bir kişiye satılabilir. Threshold sağlanmazsa k6 sürecini
hata koduyla sonlandırır.

### 2. queue-fairness-test.js — Kuyruk adalet testi

M sanal kullanıcı eşzamanlı olarak bekleme kuyruğuna katılır, ardından admin N < M kişilik
satış başlatır.

```bash
k6 run -e M=30 -e N=10 loadtests/k6/queue-fairness-test.js
```

**Assertion:** Satış açıldığında hak tanınan kişiler, kuyruğa katılan tüm kullanıcılar arasından
**tam olarak en düşük SiraNo değerine sahip N kişi** olmalı — hiçbiri atlanmamalı, hiçbiri
sırasından önce hak almamalı.

## Parametreler

Her iki script de ortam değişkenleriyle özelleştirilebilir:

| Değişken | Varsayılan | Açıklama |
|---|---|---|
| `BASE_URL` | `http://localhost:5052` | Uygulamanın adresi |
| `ETKINLIK_ID` | `1` | Test edilecek etkinlik |
| `VUS` (add-to-cart) | `50` | Aynı bilete saldıran sanal kullanıcı sayısı |
| `M` (queue-fairness) | `30` | Kuyruğa katılacak toplam kullanıcı |
| `N` (queue-fairness) | `10` | Satışın açılacağı kişi sayısı |

## Ölçülen sonuçlar (2026-08-10, 200 sanal kullanıcı)

`add-to-cart-test.js`, tek bir bilete 200 sanal kullanıcının aynı anda saldırdığı senaryo:

| Ölçüm | Sonuç |
|---|---|
| Sepete ekleme başarılı | **1** (eşik: tam olarak 1) |
| "Zaten alınmış" cevabı | 199 |
| HTTP hatası | %0 (1808 istekte 0) |
| İstek/saniye | 258 |
| Yanıt süresi (medyan / p95) | 291 ms / 2,59 s |
| Toplam süre | 7,0 sn |

Projenin çekirdek iddiası gerçek eşzamanlı yük altında doğrulandı: 200 kullanıcı aynı bileti isterken **tam olarak biri** aldı, hiçbir "double booking" olmadı ve toplam satılan+sepetteki bilet sayısı kapasiteyi aşmadı.

`queue-fairness-test.js` (30 sanal kullanıcı) da 3/3 geçti: hak tanınan hiçbir sıra numarası, bekleyen hiçbir sıra numarasından yüksek değil — yani kimse sırasını atlamadı.

p95'in 2,59 saniye olması tek makinede 200 eşzamanlı kullanıcının uygulama, veritabanı ve k6 ile aynı CPU'yu paylaşmasından kaynaklanıyor; doğruluk ölçümünü etkilemez.

### Bu ölçüm bir hata ortaya çıkardı

Aynı senaryo daha önce çok daha kötü sonuç veriyordu ve sebebi doğruluk değil, **istek yolundaki SMTP beklemesiydi**: kayıt olan kullanıcı cevabı e-posta sunucusu dönene kadar bekliyordu.

| Ölçüt | E-posta istek içindeyken | Kuyruğa alındıktan sonra |
|---|---|---|
| Toplam süre | 60,6 sn | **7,0 sn** |
| İstek/saniye | 29,8 | **257,9** |
| p95 yanıt süresi | 4,94 sn | **2,59 sn** |
| En yavaş istek | 59,99 sn | **3,94 sn** |
| Başarısız istek | %0,05 | **%0,00** |
| `POST /Account/KayitOl` | 4–17 sn | **110–130 ms** |

Her iki ölçümde de sepete ekleme sayısı 1 — yani sorun doğrulukta değil, verimdeydi. Yük testinin asıl değeri burada görüldü: birim testler bu hatayı hiçbir zaman gösteremezdi, çünkü tek kullanıcıyla 300 ms'lik bir SMTP beklemesi kimsenin dikkatini çekmiyor.

## Ana sayfa ölçümü (2026-08-10)

`anasayfa-test.js`, 10 sanal kullanıcının ana sayfayı sürekli açtığı senaryo. Sunucu tarafı sayfalama öncesi ve sonrası, **aynı veriyle** (2019 etkinlik, 202.367 bilet):

| Ölçüm | Sayfalama öncesi | Sayfalama sonrası | Kazanç |
|---|---|---|---|
| Sayfa boyutu | 5.347 KB | **48 KB** | 110× küçük |
| Yanıt süresi (p95) | 759 ms | **68 ms** | 11× hızlı |
| Yanıt süresi (medyan) | 576 ms | **41 ms** | 14× hızlı |
| İstek/saniye | 12 | **147** | 12× fazla |

Karşılaştırma için: sayfalama öncesi **19 etkinlikle** sayfa 70 KB ve p95 22 ms idi. Yani sayfalamadan sonra 2000 etkinlikli sayfa, eskiden 19 etkinlikle üretilen sayfadan bile **daha küçük** (48 KB < 70 KB) — çünkü artık ne kadar etkinlik olursa olsun yalnızca bir sayfalık kart basılıyor.

Ölçüm yöntemi: `YUKTEST-` önekli 2000 sahte etkinlik ve her birine 100 bilet eklendi, ölçüm alındı, sonra tek sorguyla silindi.

## Yük testinden önce: `yuktest` profiliyle başlatın

Yük testleri her sanal kullanıcı için tek kullanımlık bir hesap açıp hemen giriş yapar. Üç ayar bu senaryoyu engeller ya da zararlı hâle getirir:

- **E-posta doğrulama zorunluluğu** — test hesaplarının gelen kutusu yok, doğrulama adımını tamamlayamazlar.
- **Hız sınırı** — tüm istekler tek IP'den geldiği için kayıt/giriş uçları dakikada 15 istekte kilitlenir.
- **Gerçek SMTP** — tanımlıysa 200 kullanıcılık bir koşu, var olmayan `@test.local` adreslerine **200 gerçek e-posta** gönderir. Bu, gönderen hesabın itibarına zarar verir.

Üçü de `yuktest` başlatma profilinde kapalıdır; normal `http` profilinde açıktır:

```bash
dotnet run --project BiletSatis.Web --launch-profile yuktest
```

Ayarlar `BiletSatis.Web/Properties/launchSettings.json` içinde, neden kapatıldıkları açıklamasıyla birlikte duruyor. Elle ortam değişkeni ayarlamaya gerek yok; testler bittiğinde uygulamayı normal profille yeniden başlatmak yeterli.

Uygulama **üretim** ortamında bu korumalar kapalıyken başlatılırsa başlangıçta uyarı loglanır.

E-postalar gönderilmediği için `logs/eposta/` klasörüne `.html` olarak yazılır; içerikleri tarayıcıda açıp kontrol edebilirsiniz.

## Test artıklarını temizleme

Her koşu yeni hesaplar bırakır. Birikince:

```bash
sqlcmd -S localhost -E -d BiletSatisDb -i loadtests/temizlik.sql
```

Betik yalnızca `yuktest-` önekli hesapları hedefler, satılmış bileti olanlara dokunmaz (öksüz satış kaydı bırakmamak için) ve silmeden önce ne sileceğini gösterir.

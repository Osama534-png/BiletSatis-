# 🎟️ BiletSatış

Yüksek eşzamanlılıkta (binlerce kullanıcı aynı bilete aynı anda saldırdığında bile) doğru çalışan, gerçek bir ödeme sağlayıcısına bağlı bir bilet satış sistemi. ASP.NET Core MVC + EF Core + SQL Server ile geliştirildi.

Bu proje, klasik "sepete ekle / satın al" akışının **race condition** (yarış durumu) problemini SQL Server seviyesinde atomik sorgularla çözmeyi, adil bir **bekleme kuyruğu** (waitlist) mekanizmasını ve gerçek bir **ödeme entegrasyonunu** öğrenmek/göstermek amacıyla sıfırdan yazıldı.

## Öne Çıkan Özellikler

- **Race-condition güvenli satın alma** — "sepete ekle" işlemi, okuma-sonra-yazma yerine tek bir atomik `UPDATE ... WHERE Durum='Satışta'` sorgusuyla yapılır. Aynı bilete aynı anda 1000 istek gelse bile SQL Server garantisiyle sadece biri başarılı olur.
- **5 dakikalık sepet kilidi + otomatik temizlik** — bir bilet sepete eklendiğinde 5 dakika rezerve edilir; ödeme yapılmazsa arka planda çalışan bir servis (`CartExpiryWorker`) 10 saniyede bir süresi dolanları otomatik olarak tekrar satışa açar.
- **Adil FIFO bekleme kuyruğu** — biletler henüz satışa açılmadan önce kullanıcılar sıraya girebilir. Sıra numarası SQL Server'ın `IDENTITY` sütunu tarafından üretilir, böylece eşzamanlı katılımlarda bile sıralama hatasız garanti edilir. Satış açıldığında en düşük sıra numaralı N kişiye otomatik hak tanınır; hakkını kullanmayanların yeri arka planda (`WaitlistWorker`) sıradakine devredilir.
- **Gerçek kullanıcı girişi** — ASP.NET Core Identity ile kayıt/giriş/çıkış, rol tabanlı yönetici yetkilendirmesi.
- **Gerçek ödeme entegrasyonu** — Stripe Checkout ile PCI-uyumlu ödeme akışı; kart bilgisi hiçbir zaman kendi sunucumuza gelmez.
- **Yapılandırılmış loglama** — Serilog ile her kritik karar noktası (sepete ekleme sonucu, ödeme sonucu, kuyruk terfi, arka plan servis hataları) structured log olarak kaydedilir.
- **Otomatik test kapsamı** — hem gerçek SQL Server'a karşı çalışan xUnit entegrasyon testleri hem de k6 ile gerçek eşzamanlı yük testleri.

## Mimari Kararlar

### Neden atomik `UPDATE`, neden `lock()` değil?

C# tarafında `lock()`/`Semaphore` ile eşzamanlılık kontrolü, uygulama birden fazla sunucuda (load balancer arkasında) çalıştığında işe yaramaz — her sunucunun kendi belleği ayrıdır. Kilitlemeyi veritabanı seviyesinde yapmak, uygulamanın yatay olarak ölçeklenmesini garanti eder.

```sql
UPDATE Biletler
SET Durum = 'Sepette', KilitBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE()), RezerveEdenKullaniciId = @KullaniciId
WHERE Id = @BiletId AND Durum = 'Satışta'
```

Bu tek sorgu hem okuma hem yazmayı atomik yapar. Etkilenen satır sayısı `1` ise başarılı, `0` ise bilet zaten başkası tarafından alınmış demektir — ayrıca bir exception yakalamaya ya da satır kilitlemeye gerek yoktur.

### Neden `RowVersion` (optimistic concurrency) kullanılmadı?

Optimistic concurrency, EF Core'un normal oku-değiştir-kaydet akışında (`SaveChanges`) anlamlıdır. Burada zaten read-then-write yapılmadığı için `RowVersion` eklemek gereksiz olurdu — ileride bir admin "fiyat düzenle" ekranı eklenirse orada anlamlı olabilir.

### Kuyruk adaleti nasıl garanti ediliyor?

`RezervasyonKuyrugu` tablosundaki `SiraNo` sütunu SQL Server `IDENTITY` — yani sıra numarasını uygulama kodu değil, veritabanının kendisi üretiyor. Aynı milisaniyede gelen yüzlerce "sıraya gir" isteği bile SQL Server tarafından sıraya dizilip benzersiz, artan numaralar alır.

## Teknoloji Yığını

| Katman | Teknoloji |
|---|---|
| Backend | ASP.NET Core 9 MVC |
| ORM | Entity Framework Core 9 |
| Veritabanı | SQL Server |
| Kimlik doğrulama | ASP.NET Core Identity |
| Ödeme | Stripe Checkout (Stripe.net) |
| Loglama | Serilog (console + rolling file) |
| Test | xUnit (entegrasyon testleri) + k6 (yük testleri) |

## Proje Yapısı

```
BiletSatis/
  BiletSatis.Web/          # Ana MVC uygulaması
    Domain/                # Etkinlik, Bilet, RezervasyonKuyrugu entity'leri
    Data/                  # DbContext, migration'lar, seed
    Services/              # Atomik SQL sorgularını içeren servisler
    BackgroundServices/    # CartExpiryWorker, WaitlistWorker
    Controllers/, Views/   # MVC katmanı
  BiletSatis.Tests/        # xUnit entegrasyon testleri (gerçek SQL Server'a karşı)
  loadtests/k6/            # k6 yük testi script'leri
```

## Kurulum

### Gereksinimler
- .NET 9 SDK
- SQL Server (LocalDB veya tam sürüm — `appsettings.json` içindeki `ConnectionStrings:DefaultConnection` bağlantı dizesini ortamınıza göre düzenleyin)

### Çalıştırma

```bash
git clone <bu-repo>
cd BiletSatis
dotnet run --project BiletSatis.Web
```

İlk çalıştırmada veritabanı otomatik oluşturulur, migration'lar uygulanır ve örnek etkinlik/bilet verisi seed edilir. Ayrıca aşağıdaki admin hesabı otomatik oluşturulur:

- **E-posta:** `admin@biletsatis.local`
- **Şifre:** `Admin123!`

> ⚠️ Bu, sadece yerel geliştirme için hardcoded bir seed hesabıdır — gerçek bir dağıtımda bu yaklaşım değiştirilmelidir.

### Stripe (ödeme) yapılandırması

Stripe secret key'i **asla appsettings.json'a yazılmaz** — `dotnet user-secrets` ile saklanır:

```bash
cd BiletSatis.Web
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
```

Test kartı: `4242 4242 4242 4242`, herhangi bir gelecek son kullanma tarihi, herhangi 3 haneli CVC.

## Test

### Entegrasyon testleri (xUnit)

Testler gerçek SQL Server semantiğine (`DATEADD`, `GETUTCDATE()`, atomik `UPDATE...WHERE`) dayandığı için in-memory sahte bir veritabanı yerine ayrı bir test veritabanına (`BiletSatisDb_Test`) karşı çalışır:

```bash
dotnet test BiletSatis.Tests
```

En kritik test, projenin tüm iddiasını kanıtlar: 50 ayrı bağlantıdan aynı bilete gerçek eşzamanlı istek gönderilir ve tam olarak birinin başarılı olduğu doğrulanır (`TryAddToCartAsync_ElliEsZamanliIstek_SadeceBiriBasariliOlmali`).

### Yük testleri (k6)

```bash
k6 run loadtests/k6/add-to-cart-test.js
k6 run loadtests/k6/queue-fairness-test.js
```

Detaylar için [loadtests/k6/README.md](loadtests/k6/README.md).

## Bilinen Kapsam Dışı Konular

- k6 yük test script'leri, giriş zorunluluğu eklenmeden önce yazıldığı için şu an anonim istek atıyor — kimlik doğrulama akışını script'lere eklemek gerekiyor.
- Production dağıtımı (deployment/hosting) henüz yapılmadı.
- Satın alma sonrası iade/iptal akışı yok (sadece ödeme öncesi sepetten vazgeçme mevcut).

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
- **İnteraktif salon haritası** — koltuk numarası önekinden (`A-01` → A blok) türetilen blok haritası, sahne yayı, doluluğa göre renklendirme ve koltuğa tıklayarak doğrudan sepete ekleme.
- **Etkinlik keşif arayüzü** — kategori menüsü, şehir seçici, canlı arama, tarih/fiyat filtreleri, sıralama, ızgara/liste görünümü; tümü sayfa yenilemeden çalışır ve tercihler tarayıcıda saklanır.
- **Kullanıcı profili** — kullanıcı adını, e-postasını ve şifresini değiştirebilir; kendi satın alma özetini görür.
- **Yönetim paneli** — etkinlik ekleme/düzenleme/silme, afiş yükleme, satış ve gelir istatistikleri, kuyruğa hak tanıma.
- **Kuyruk e-posta bildirimi** — sırası gelen kullanıcıya "sıran geldi" e-postası gönderilir. Gönderim, hak tanıma işleminden ayrı bir arka plan görevinde yapılır; hata olursa bildirim kaybolmaz, tekrar denenir.

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

### Satılmış bileti olan etkinlik neden silinemiyor?

Yönetim panelinden etkinlik silinebilir, ancak **satılmış bileti olan etkinlikler silinemez**. Satılmış bilet gerçek bir satın alma kaydıdır; etkinlik silinirse `Biletler` tablosundaki satırlar cascade ile gider ve kullanıcıların bilet geçmişi yok olur. Kontrol yalnızca arayüzde butonu gizlemekle yapılmaz, `EtkinlikSil` action'ının içindedir.

Silinebilir etkinliklerde biletler foreign key üzerinden cascade ile silinir; `RezervasyonKuyrugu`'nun `Etkinlik`'e foreign key'i **olmadığı** için o kayıtlar ayrıca temizlenir — aksi halde öksüz satır kalırdı.

### Bildirim e-postası neden hak tanıma anında gönderilmiyor?

Hak tanıma tek bir atomik `UPDATE` sorgusudur. E-postayı bu işlemin içinde göndermek üç sorun doğururdu: SMTP sunucusunun yanıt süresi kuyruk işlemini yavaşlatır, e-posta hata verirse hak tanımayı geri almak gerekir, uygulama yeniden başlarsa gönderilmemiş bildirimler kaybolur.

Bunun yerine `RezervasyonKuyrugu` tablosuna `BildirimGonderildi` bayrağı eklendi. `BildirimWorker` 20 saniyede bir "hakkı tanınmış ama bildirilmemiş" kayıtları tarar, e-postayı gönderir ve bayrağı işaretler. Gönderim başarısız olursa bayrak `false` kalır ve bir sonraki turda tekrar denenir — aynı kişiye iki kez gönderilmesi de bayrak sayesinde engellenir.

### Koltuk blokları nereden geliyor?

Ayrı bir "blok" tablosu yok. Blok bilgisi koltuk numarasının önekinden türetilir (`A-01`, `B-33` → A ve B blokları). Kategori sırası fiyata göre belirlenir: en pahalı blok "1. Kategori" olur ve salon haritasında sahneye en yakın konuma yerleşir.

### Şehir neden ayrı bir sütun değil?

Şehir, `Mekan` alanındaki `"Salon Adı, Şehir"` metninden ayrıştırılır (`MekanBilgisi` sınıfı). Bu, ek bir migration gerektirmeden şehir filtresi eklemeyi mümkün kıldı; karşılığında mekan alanının bu biçimde girilmesi gerekir. Şehir bağımsız bir varlık hâline gelirse (ör. şehir sayfaları, il/ilçe hiyerarşisi) ayrı sütuna taşınmalıdır.

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
    Domain/                # Etkinlik, Bilet, RezervasyonKuyrugu, EtkinlikKategorisi
    Data/                  # DbContext, migration'lar, seed, Identity yapılandırması
    Services/              # Atomik SQL sorgularını içeren servisler
    BackgroundServices/    # CartExpiryWorker, WaitlistWorker
    Controllers/           # Home, Etkinlik, Biletler, Kuyruk, Profil, Admin, Account
    Views/                 # Razor görünümleri
    wwwroot/
      css/site.css         # Tüm tasarım sistemi (tek dosya)
      js/site.js           # Filtreler, salon haritası, görünüm değiştirici
      img/afis/            # Yerel SVG konser afişleri
      img/afis/yuklenen/   # Panelden yüklenen afişler (.gitignore'da)
    Dockerfile             # Multi-stage build (SDK -> ASP.NET runtime)
  BiletSatis.Tests/        # xUnit testleri (gerçek SQL Server'a karşı)
  loadtests/k6/            # k6 yük testi script'leri
  docker-compose.yml       # web + db container'larını birlikte ayağa kaldırır
  .env.example             # Docker için gerekli ortam değişkenleri şablonu
```

### Etkinlik alanları

| Alan | Açıklama |
|---|---|
| `Ad`, `Tarih` | Temel bilgiler |
| `Mekan` | `"Salon Adı, Şehir"` biçiminde; şehir buradan ayrıştırılır |
| `Kategori` | Konser, Tiyatro, Sinema, Festival, StandUp, ElektronikMuzik, CocukAktiviteleri, Eglence |
| `Aciklama` | Detay sayfasındaki tanıtım metni |
| `YasSiniri` | Asgari yaş; `0` = sınır yok |
| `AfisUrl` | Afiş görselinin yolu; boşsa varsayılan afiş kullanılır |

### Afiş görselleri

`wwwroot/img/afis/` altındaki afişler harici bağımlılığı olmayan yerel SVG dosyalarıdır. Yönetim panelinden yeni afiş yüklenebilir; yükleme dört katmanlı doğrulamadan geçer:

1. Uzantı allowlist'i (JPG, PNG, WEBP)
2. Boyut sınırı (4 MB)
3. Dosya imzası (magic bytes) kontrolü — uzantısı değiştirilmiş dosyalar reddedilir
4. Dosya adı istemciden alınmaz, sunucuda GUID olarak üretilir

Yüklenen dosyalar `img/afis/yuklenen/` altına kaydedilir ve `.gitignore` ile depoya girmez.

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

### Docker ile çalıştırma

.NET SDK veya yerel SQL Server kurmadan, tek komutla hem uygulamayı hem de kendi SQL Server veritabanını ayağa kaldırabilirsiniz:

```bash
cp .env.example .env
# .env dosyasını açıp DB_SA_PASSWORD ve STRIPE_SECRET_KEY değerlerini girin
docker compose up --build
```

Uygulama `http://localhost:8080` adresinde açılır. `docker-compose.yml`, uygulama container'ı (`web`) ile ayrı bir SQL Server container'ını (`db`) birlikte başlatır; veritabanı bağlantısı Windows Authentication yerine SQL Server kimlik doğrulaması (kullanıcı/şifre) ile ortam değişkenleri üzerinden yapılandırılır — bu yüzden yerel geliştirme (`appsettings.json`) ile Docker yapılandırması birbirinden bağımsızdır.

> ⚠️ Bu, sadece yerel geliştirme için hardcoded bir seed hesabıdır — gerçek bir dağıtımda bu yaklaşım değiştirilmelidir.

### Stripe (ödeme) yapılandırması

Stripe secret key'i **asla appsettings.json'a yazılmaz** — `dotnet user-secrets` ile saklanır:

```bash
cd BiletSatis.Web
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..."
```

Test kartı: `4242 4242 4242 4242`, herhangi bir gelecek son kullanma tarihi, herhangi 3 haneli CVC.

### E-posta bildirimi yapılandırması

Proje **SMTP hesabı olmadan da çalışır**. `Eposta:SmtpSunucu` boşsa e-postalar gönderilmez, `logs/eposta/` klasörüne `.html` dosyası olarak yazılır — bildirimlerin içeriği tarayıcıda açılıp kontrol edilebilir.

Gerçek gönderim için SMTP bilgilerini girin (şifre `appsettings.json`'a **yazılmaz**, user-secrets'ta saklanır):

```bash
cd BiletSatis.Web
dotnet user-secrets set "Eposta:SmtpSunucu" "smtp.gmail.com"
dotnet user-secrets set "Eposta:KullaniciAdi" "hesabiniz@gmail.com"
dotnet user-secrets set "Eposta:Sifre" "uygulama-sifreniz"
```

`appsettings.json` içindeki `Eposta:SiteAdresi` değerini de sitenin gerçek adresiyle güncelleyin — e-postadaki "Biletini seç" bağlantısı bu adresi kullanır, göreli adres e-posta istemcilerinde çalışmaz.

## Test

### Entegrasyon testleri (xUnit)

Testler gerçek SQL Server semantiğine (`DATEADD`, `GETUTCDATE()`, atomik `UPDATE...WHERE`) dayandığı için in-memory sahte bir veritabanı yerine ayrı bir test veritabanına (`BiletSatisDb_Test`) karşı çalışır:

```bash
dotnet test BiletSatis.Tests
```

En kritik test, projenin tüm iddiasını kanıtlar: 50 ayrı bağlantıdan aynı bilete gerçek eşzamanlı istek gönderilir ve tam olarak birinin başarılı olduğu doğrulanır (`TryAddToCartAsync_ElliEsZamanliIstek_SadeceBiriBasariliOlmali`).

Kapsanan alanlar:

| Dosya | Ne test ediliyor |
|---|---|
| `BiletRezervasyonServisiTests` | Eşzamanlı sepete ekleme, kilit süresi, ödeme tamamlama |
| `KuyrukServisiTests` | Sıra numarası benzersizliği, FIFO hak tanıma, süre dolumu |
| `AdminEtkinlikSilmeTests` | Satılmış bilet koruması, bilet ve kuyruk kayıtlarının temizlenmesi |
| `MekanBilgisiTests` | Şehir/salon ayrıştırma uç durumları |
| `EtkinlikKartVmTests` | Geri sayım metni, kıtlık uyarısı eşikleri |
| `AdminOzetTests` | Gelir/doluluk hesapları, sıfıra bölme durumu |
| `ProfilVmTests` | Avatar baş harfleri |
| `KuyrukBildirimServisiTests` | Bildirim gönderimi, tekrar gönderim engeli, hata sonrası yeniden deneme |

### Yük testleri (k6)

```bash
k6 run loadtests/k6/add-to-cart-test.js
k6 run loadtests/k6/queue-fairness-test.js
```

Detaylar için [loadtests/k6/README.md](loadtests/k6/README.md).

## Bilinen Kapsam Dışı Konular

- Production dağıtımı (deployment/hosting) henüz yapılmadı.
- Satın alma sonrası iade/iptal akışı yok (sadece ödeme öncesi sepetten vazgeçme mevcut).
- Kayıt onayı ve şifre sıfırlama e-postaları yok (yalnızca kuyruk bildirimi uygulandı).
- Arayüzdeki filtreler (arama, kategori, şehir, fiyat, sıralama) istemci tarafında çalışır. Tüm etkinlikler tek sayfada render edildiği için etkinlik sayısı büyüdüğünde sunucu tarafı filtreleme ve sayfalama gerekir.
- Şehir bilgisi ayrı bir sütun değil, `Mekan` alanından ayrıştırılır (bkz. Mimari Kararlar).

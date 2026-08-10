# 🎟️ BiletSatış

Yüksek eşzamanlılıkta (binlerce kullanıcı aynı bilete aynı anda saldırdığında bile) doğru çalışan, gerçek bir ödeme sağlayıcısına bağlı bir bilet satış sistemi. ASP.NET Core MVC + EF Core + SQL Server ile geliştirildi.

Bu proje, klasik "sepete ekle / satın al" akışının **race condition** (yarış durumu) problemini SQL Server seviyesinde atomik sorgularla çözmeyi, adil bir **bekleme kuyruğu** (waitlist) mekanizmasını ve gerçek bir **ödeme entegrasyonunu** öğrenmek/göstermek amacıyla sıfırdan yazıldı.

## Öne Çıkan Özellikler

- **Race-condition güvenli satın alma** — "sepete ekle" işlemi, okuma-sonra-yazma yerine tek bir atomik `UPDATE ... WHERE Durum='Satışta'` sorgusuyla yapılır. Aynı bilete aynı anda 1000 istek gelse bile SQL Server garantisiyle sadece biri başarılı olur.
- **5 dakikalık sepet kilidi + otomatik temizlik** — bir bilet sepete eklendiğinde 5 dakika rezerve edilir; ödeme yapılmazsa arka planda çalışan bir servis (`CartExpiryWorker`) 10 saniyede bir süresi dolanları otomatik olarak tekrar satışa açar.
- **Adil FIFO bekleme kuyruğu** — biletler henüz satışa açılmadan önce kullanıcılar sıraya girebilir. Sıra numarası SQL Server'ın `IDENTITY` sütunu tarafından üretilir, böylece eşzamanlı katılımlarda bile sıralama hatasız garanti edilir. Satış açıldığında en düşük sıra numaralı N kişiye otomatik hak tanınır; hakkını kullanmayanların yeri arka planda (`WaitlistWorker`) sıradakine devredilir.
- **Gerçek kullanıcı girişi** — ASP.NET Core Identity ile kayıt/giriş/çıkış, rol tabanlı yönetici yetkilendirmesi. Kayıt sonrası **e-posta doğrulaması zorunludur**; doğrulanmamış hesap giriş yapamaz. Şifresini unutan kullanıcı e-postayla gelen bağlantıdan yeni şifre belirleyebilir.
- **Gerçek ödeme entegrasyonu** — Stripe Checkout ile PCI-uyumlu ödeme akışı; kart bilgisi hiçbir zaman kendi sunucumuza gelmez.
- **Yapılandırılmış loglama** — Serilog ile her kritik karar noktası (sepete ekleme sonucu, ödeme sonucu, kuyruk terfi, arka plan servis hataları) structured log olarak kaydedilir.
- **Otomatik test kapsamı** — hem gerçek SQL Server'a karşı çalışan xUnit entegrasyon testleri hem de k6 ile gerçek eşzamanlı yük testleri.
- **İnteraktif salon haritası** — koltuk numarası önekinden (`A-01` → A blok) türetilen blok haritası, sahne yayı, doluluğa göre renklendirme.
- **Çoklu koltuk seçimi** — haritadan tek seferde 6 koltuğa kadar seçilir, seçim çubuğu toplamı canlı gösterir ve tamamı tek istekte rezerve edilir. Koltuklardan biri bile araya girilirse hiçbiri alınmaz (bkz. Mimari Kararlar). Sepetin tamamı tek bir Stripe oturumunda, çok kalemli olarak ödenir.
- **Bilet devretme** — bilete gidemeyen kullanıcı biletini başka bir kullanıcıya devredebilir. Devir sonrası eski sahibin QR kodu geçersizleşir (imzaya sürüm eklenmiştir) ve yeni sahibe yeni QR'lı bilet e-postası gider. Kapıda okutulmuş ya da etkinliği geçmiş bilet devredilemez.
- **Genel giriş etkinlikleri** — her etkinlik salonlu değildir. Festival ve ayakta konserlerde koltuk seçimi yerine yalnızca adet seçilir; sistem müsait biletlerden o kadarını tek atomik sorguyla ayırır. Yeterli bilet yoksa hiçbiri ayrılmaz.
- **Etkinlik keşif arayüzü** — kategori menüsü, şehir seçici, canlı arama, tarih/fiyat filtreleri, sıralama, ızgara/liste görünümü; tümü sayfa yenilemeden çalışır ve tercihler tarayıcıda saklanır.
- **Kullanıcı profili** — kullanıcı adını, e-postasını ve şifresini değiştirebilir; kendi satın alma özetini görür.
- **Yönetim paneli** — etkinlik ekleme/düzenleme/silme, afiş yükleme, satış ve gelir istatistikleri, kuyruğa hak tanıma.
- **E-posta bildirimleri** — kuyrukta sırası gelene "sıran geldi", bilet satın alana QR kodlu "biletin hazır" e-postası gönderilir. Gönderim, kuyruk ve ödeme işlemlerinden ayrı bir arka plan görevinde yapılır; hata olursa bildirim kaybolmaz, tekrar denenir.
- **Doğrulanmış değerlendirme** — etkinlik sayfasında puan ortalaması, yıldız dağılımı ve yorumlar gösterilir. Değerlendirme bırakabilmek için bilet almak yetmez, biletin **kapıda okutulmuş** olması gerekir; böylece her yorumun arkasında gerçek bir katılım vardır. Bir kullanıcı bir etkinliği yalnızca bir kez değerlendirir, sonradan güncelleyebilir.
- **Kapı kontrolü** — görevli biletteki QR'ı okutur, mobil öncelikli doğrulama sayfası bileti kontrol eder. QR kodu HMAC ile imzalıdır (sahte bilet üretilemez), bir bilet yalnızca bir kez giriş sağlar ve eşzamanlı okutmalarda tek atomik `UPDATE` ile yalnızca biri kaydedilir.

## Mimari Kararlar

### Neden atomik `UPDATE`, neden `lock()` değil?

C# tarafında `lock()`/`Semaphore` ile eşzamanlılık kontrolü, uygulama birden fazla sunucuda (load balancer arkasında) çalıştığında işe yaramaz — her sunucunun kendi belleği ayrıdır. Kilitlemeyi veritabanı seviyesinde yapmak, uygulamanın yatay olarak ölçeklenmesini garanti eder.

```sql
UPDATE Biletler
SET Durum = 'Sepette', KilitBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE()), RezerveEdenKullaniciId = @KullaniciId
WHERE Id = @BiletId AND Durum = 'Satışta'
```

Bu tek sorgu hem okuma hem yazmayı atomik yapar. Etkilenen satır sayısı `1` ise başarılı, `0` ise bilet zaten başkası tarafından alınmış demektir — ayrıca bir exception yakalamaya ya da satır kilitlemeye gerek yoktur.

### Çoklu koltukta neden tek `UPDATE` yetmiyor?

Tek koltukta etkilenen satır sayısı ya `1` ya `0` olduğu için sonucu doğrudan okuyabiliyorduk. Birden çok koltukta aynı sorgu **kısmen** başarılı olabilir: dört koltuk istenir, üçü alınır. Bu kabul edilemez — yan yana oturmak isteyen kullanıcı dağınık üç koltukla kalır ve dördüncüsü için para ödemiş olmaz.

Çözüm, sorguyu bir işlemin (transaction) içine almak:

```sql
BEGIN TRANSACTION
UPDATE Biletler SET Durum = 'Sepette', ...
WHERE Durum = 'Satışta'
  AND Id IN (SELECT CAST(value AS INT) FROM STRING_SPLIT(@idler, ','))
-- etkilenen satır sayısı istenen koltuk sayısına eşit değilse ROLLBACK
```

İşlem açıkken güncellenen satırlar kilitli kalır; araya giren ikinci kullanıcı ancak biz karar verdikten sonra ilerleyebilir. Etkilenen satır sayısı istenen sayıya eşitse `COMMIT`, değilse `ROLLBACK` — yani kullanıcı ya istediği koltukların hepsini alır ya hiçbirini.

Id listesi tek bir metin parametresi olarak gönderilip SQL tarafında `STRING_SPLIT` ile tabloya çevrilir. Böylece koltuk sayısına göre değişen bir SQL metni üretmeye gerek kalmaz, sorgu tamamen parametreli kalır.

Geri alma sonrası "hangi koltuk elden gitti" sorgusu bilerek `ROLLBACK`'ten **sonra** çalışır; önce çalışsaydı kendi yazdığımız satırları "sepette" görürdük.

### Bilet devretmede eski QR nasıl öldürülüyor?

Bilete gidemeyen kullanıcı biletini bir arkadaşına devredebilir. Buradaki asıl soru teknik: **eski sahibin elindeki QR ne olacak?**

Kod imzası önceden yalnızca bilet numarası üzerindeydi (`bilet:1399`), yani her bilet için sabitti. Devir yapılsa bile eski sahibin QR'ı çalışmaya devam ederdi — iki kişi aynı biletle kapıya gelirdi.

Çözüm, imzaya bir **sürüm** eklemek:

```
kod  = 1399.2.a7f3c9e2
imza = HMAC(anahtar, "bilet:1399:2")
```

Devir sırasında `KodSurumu` bir artar. Eski koddaki sürüm artık biletin sürümüyle uyuşmaz ve kapıda reddedilir. Kullanıcı kendi kodundaki sürüm numarasını elle artıramaz, çünkü sürüm imzanın içinde — yeni sürümün imzasını üretmek için gizli anahtar gerekir.

Devir tek atomik `UPDATE` ile yapılır:

```sql
UPDATE Biletler
SET RezerveEdenKullaniciId = @alici, KodSurumu = KodSurumu + 1, BildirimGonderildi = 0
WHERE Id = @id AND Durum = 'Satıldı' AND RezerveEdenKullaniciId = @devreden AND GirisYapildi = 0
```

Bu tek koşul üç durumu birden kapatır: iki sekmeden aynı anda iki farklı kişiye devretme, başkasının biletini devretme ve **bilet tam o anda kapıda okutuluyorken devretme**. `BildirimGonderildi` sıfırlandığı için mevcut bildirim görevi yeni sahibe yeni QR'lı bileti kendiliğinden gönderir — ayrı bir e-posta akışı yazmaya gerek kalmadı.

Sürüm eklenmeden önce gönderilmiş QR'lar (iki parçalı `id.imza` biçimi) hâlâ geçerli sayılır; aksi halde o e-postalardaki biletler bir gecede çalışmaz hâle gelirdi.

Koruma ölçüldü: sürüm kontrolü kaldırıldığında eski QR yeniden geçerli oluyor ve kapıdan giriş yapabiliyor — ilgili iki test kırılıyor.

### Genel giriş: "hangisi olursa olsun N tane"

Her etkinlik salonlu değildir. Festival, ayakta konser, açık alan etkinliklerinde koltuk numarası yoktur; kullanıcı yalnızca kaç bilet istediğini söyler. `Etkinlik.BiletModeli` bunu belirler: `KoltukSecmeli` ya da `GenelGiris`.

Bu, projedeki üçüncü rezervasyon biçimi ve öncekilerden farklı bir soru soruyor:

| | Sorulan |
|---|---|
| Tek koltuk | "Şu bileti ver" |
| Çoklu koltuk | "Şu belirli biletleri ver" |
| Genel giriş | **"Hangisi olursa olsun N tane ver"** |

```sql
UPDATE TOP (@adet) Biletler
SET Durum = 'Sepette', KilitBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE()), RezerveEdenKullaniciId = @kullaniciId
OUTPUT INSERTED.Id
WHERE EtkinlikId = @etkinlikId AND Durum = 'Satışta'
```

`UPDATE TOP (n)` müsait satırlardan istenen kadarını tek seferde kilitler; `OUTPUT` hangilerinin alındığını söyler. Yeterli bilet yoksa sorgu **bulabildiği kadarını** alır — yine kısmi başarı. Çoklu koltuktaki mantığın aynısı uygulanır: işlem içinde yapılır, sayı tutmazsa tamamı geri alınır. Kullanıcı 5 bilet isteyip 3 biletle kalmaz.

Koruma ölçüldü: geri alma `COMMIT`'e çevrildiğinde ilgili iki test kırılıyor. Eşzamanlılık testi de doğrudan **overselling**'i hedefler — 10 biletlik etkinlikte 10 kullanıcı aynı anda 3'er bilet isterse dağıtılan toplam asla 10'u aşmamalı.

#### Neden ayrı bir "kalan sayısı" tablosu değil?

İlk tasarım `Kalan = Kalan - @adet WHERE Kalan >= @adet` biçiminde bir sayaç tablosuydu. Vazgeçildi: sayaç, iptal ve süre dolumunda kontenjanın geri verilmesini, bilet satırlarının ayrıca üretilmesini ve ödeme kurtarma mantığıyla uyumlandırılmasını gerektiriyordu. Satır tabanlı çözüm sepet, ödeme, QR ve kapı kontrolünü hiç değiştirmeden çalışıyor. Sayaç yaklaşımı gerçek yüksek ölçekli sistemlerde doğru tercih olabilir; bu kod tabanı için maliyeti kazancından fazlaydı.

### Ödeme sırasında kilit neden uzatılıyor?

Normal sepet kilidi 5 dakika. Kullanıcı Stripe'ın ödeme sayfasında kart bilgilerini girerken bu süre dolarsa `CartExpiryWorker` koltuğu tekrar satışa açar; kullanıcı ödemeyi tamamladığında koltuk başkasına satılmış olabilir — parası alınmış, bileti yok. Bu yüzden Stripe oturumu oluşturulmadan hemen önce sepetteki biletlerin kilidi 15 dakikaya uzatılır.

İkinci bir savunma daha var: ödeme tamamlanırken bilet yalnızca "hâlâ sepetimde" koşuluyla değil, **"serbest kalmış ama kimse almamış"** koşuluyla da alınır. Yani kilit düşmüş olsa bile koltuğu bu arada başkası kapmadıysa, parası ödenmiş bilet geri kazanılır. Koltuk gerçekten başkasına gittiyse kayıp gerçektir; kod bunu `LogError` ile kaydeder ve kullanıcıyı uyarır (otomatik iade akışı yok).

Ayrıca ödeme sonucu, güncellenen satır sayısından değil **kullanıcının gerçekten sahip olduğu bilet sayısından** okunur. Böylece kullanıcı başarı sayfasını yenilediğinde ikinci çağrı hiçbir satırı değiştirmese bile doğru cevap döner; sahte "biletiniz kayboldu" uyarısı çıkmaz ve bildirim e-postası ikinci kez tetiklenmez.

### Aynı kullanıcı kuyruğa iki kez giremez — nasıl?

"Zaten sırada mı" kontrolü ile ekleme ayrı sorgular olduğunda, aynı kullanıcının iki isteği aynı anda geldiğinde ikisi de "sırada değil" görüp iki kayıt açıyordu. Kontrol artık eklemeyle **aynı SQL deyiminin içinde**:

```sql
INSERT INTO RezervasyonKuyrugu (...)
OUTPUT INSERTED.SiraNo
SELECT @etkinlikId, @kullaniciId, 'Beklemede', GETUTCDATE()
WHERE NOT EXISTS (
    SELECT 1 FROM RezervasyonKuyrugu WITH (UPDLOCK, HOLDLOCK)
    WHERE EtkinlikId = @etkinlikId AND KullaniciId = @kullaniciId AND Durum <> 'SuresiDoldu'
)
```

Kayıt açılmadıysa sıra numarası dönmez (`null`). `UPDLOCK, HOLDLOCK` aralığı kilitleyerek ikinci isteğin araya kayıt sokmasını engeller.

Bu hata, testler gerçekten eşzamanlı hâle getirilene kadar görünmüyordu: istekler `Task.WhenAll` ile başlatılsa bile biri diğerinden önce bitiyordu. Testler artık ortak bir "kapı" kullanıyor — her görev önce bağlantısını açıp ısınıyor, sonra hep birlikte serbest bırakılıyor. Eski kodla test 3/3 kırılıyor, yeni kodla 4/4 geçiyor.

### Satılmış bileti olan etkinlik silme kontrolü neden SQL içinde?

Önce "satılmış bilet var mı" diye sorup sonra silmek yetmiyordu: tam aradaki anda bir ödeme tamamlanırsa satılmış bilet cascade ile yok olurdu. Koşul artık `DELETE`'in kendi içinde (`WHERE NOT EXISTS (...)`) ve etkilenen satır sayısına bakılıyor; kuyruk kayıtlarının temizliğiyle birlikte tek bir işlem (transaction) içinde yapılıyor.

### Koltuk numarası çakışması

Bilet ekleme "bu blokta kaç bilet var" sayıp numarayı ondan üretiyor. İki eşzamanlı ekleme aynı numarayı üretebilirdi; `(EtkinlikId, KoltukNo)` üzerindeki **benzersiz dizin** bunu veritabanı seviyesinde imkânsız kılar. Çakışma olursa hiçbir bilet yazılmaz ve yöneticiye tekrar denemesi söylenir.

### Etkinlik düzenlemede `RowVersion` (optimistic concurrency)

Bilet satın almada oku-değiştir-kaydet akışı hiç yok, o yüzden orada satır sürümüne gerek duyulmuyor. Ama **etkinlik düzenleme ekranı** kaçınılmaz olarak bu akışla çalışır: form açılır, yönetici düşünür, sonra kaydeder. Araya başka bir yönetici girip aynı etkinliği kaydederse, ikinci kayıt birincinin değişikliğini sessizce ezerdi.

`Etkinlikler` tablosuna `SatirSurumu` (`rowversion`) sütunu eklendi. SQL Server bu sütunu her güncellemede kendisi artırır; EF de güncelleme sorgusuna `AND SatirSurumu = okuduğum değer` koşulunu ekler. Araya biri girdiyse hiçbir satır etkilenmez ve `DbUpdateConcurrencyException` fırlar. Kullanıcıya "bu etkinlik siz formu açtıktan sonra değiştirildi" denip **güncel değerler** gösterilir; kaybolan bir düzenleme olmaz.

Korumanın gerçekten çalıştığı ölçüldü: satır sürümü devre dışı bırakıldığında ilgili testler 3/3 kırılıyor.

### Neden biletlerde `RowVersion` yok?

Bilet satın alma tek atomik UPDATE ile yapılır; okuma-sonra-yazma olmadığı için orada satır sürümü tutmanın bir karşılığı yoktur. Optimistic concurrency yalnızca yukarıdaki gibi form tabanlı düzenleme akışlarında anlamlıdır.

### Kuyruk adaleti nasıl garanti ediliyor?

`RezervasyonKuyrugu` tablosundaki `SiraNo` sütunu SQL Server `IDENTITY` — yani sıra numarasını uygulama kodu değil, veritabanının kendisi üretiyor. Aynı milisaniyede gelen yüzlerce "sıraya gir" isteği bile SQL Server tarafından sıraya dizilip benzersiz, artan numaralar alır.

### Satılmış bileti olan etkinlik neden silinemiyor?

Yönetim panelinden etkinlik silinebilir, ancak **satılmış bileti olan etkinlikler silinemez**. Satılmış bilet gerçek bir satın alma kaydıdır; etkinlik silinirse `Biletler` tablosundaki satırlar cascade ile gider ve kullanıcıların bilet geçmişi yok olur. Kontrol yalnızca arayüzde butonu gizlemekle yapılmaz, `EtkinlikSil` action'ının içindedir.

Silinebilir etkinliklerde biletler foreign key üzerinden cascade ile silinir; `RezervasyonKuyrugu`'nun `Etkinlik`'e foreign key'i **olmadığı** için o kayıtlar ayrıca temizlenir — aksi halde öksüz satır kalırdı.

### Bildirim e-postası neden hak tanıma anında gönderilmiyor?

Hak tanıma tek bir atomik `UPDATE` sorgusudur. E-postayı bu işlemin içinde göndermek üç sorun doğururdu: SMTP sunucusunun yanıt süresi kuyruk işlemini yavaşlatır, e-posta hata verirse hak tanımayı geri almak gerekir, uygulama yeniden başlarsa gönderilmemiş bildirimler kaybolur.

Bunun yerine `RezervasyonKuyrugu` tablosuna `BildirimGonderildi` bayrağı eklendi. `BildirimWorker` 20 saniyede bir "hakkı tanınmış ama bildirilmemiş" kayıtları tarar, e-postayı gönderir ve bayrağı işaretler. Gönderim başarısız olursa bayrak `false` kalır ve bir sonraki turda tekrar denenir — aynı kişiye iki kez gönderilmesi de bayrak sayesinde engellenir.

Aynı desen satın alma bildirimi için de kullanılır: `Biletler` tablosundaki `BildirimGonderildi`, ödeme tamamlandığında sıfırlanır ve worker "satılmış ama bildirilmemiş" biletleri tarar. Bayrak her ödeme tamamlanışında sıfırlandığı için, iptal edilip tekrar satılan bilette yeni alıcıya da bildirim gider.

Bu özellik eklendiğinde veritabanında zaten satılmış biletler vardı; migration bunları "bildirilmiş" olarak işaretler, aksi halde özellik açılır açılmaz tüm geçmiş satışlara toplu e-posta giderdi.

### QR kodu e-postaya nasıl gömülüyor?

Gmail gibi istemciler `data:` URI'li görselleri engeller. Bu yüzden QR kodu MailKit'in `LinkedResources` özelliğiyle e-postaya iliştirilir ve HTML içinde `cid:biletqr` ile referans verilir. Geliştirme modunda (SMTP yokken) dosyaya yazan gönderici, önizleme tarayıcıda açılacağı için `cid:` referanslarını `data:` URI'ye çevirir.

QR kodu, kapı görevlisinin okutunca açacağı imzalı doğrulama adresini taşır (bkz. aşağıdaki bölüm).

### Kapı kontrolü QR kodu neden imzalı?

QR'daki adres `"/Giris/Dogrula?kod=1399"` olsaydı, kapıdaki herkes numarayı artırarak başkalarının biletlerini "kullanıldı" işaretleyebilir ve o kişiler içeri alınamazdı. Bu yüzden kod, bilet numarası ve **HMAC-SHA256 imzasından** oluşur:

```
1399.a7f3c9e2b1d4f608
```

Sunucu imzayı gizli anahtarla yeniden hesaplayıp karşılaştırır; anahtarı bilmeyen geçerli kod üretemez. Karşılaştırma sabit sürelidir (`FixedTimeEquals`), böylece imza karakter karakter tahmin edilemez. İmza tutmayan kod veritabanına hiç sorulmaz.

İkinci katman: doğrulama sayfası `[Authorize(Roles = "Admin")]` ile korunur. Sayfa herkese açık olsaydı, biletini okutan herkes kendi girişini yakabilir ya da başkasınınkiyle oynayabilirdi.

Üçüncü katman: giriş onayı tek atomik `UPDATE` ile yapılır (`WHERE ... AND GirisYapildi = 0`). İki görevli aynı bileti aynı anda okutsa bile yalnızca biri girişi kaydeder — bilet satın almadaki yarış durumu çözümünün aynısı.

**Kapsam dışı:** Biletin ekran görüntüsü paylaşılırsa ilk okutan içeri girer, ikincisi "zaten kullanıldı" görür. Bu doğru davranıştır ama sistem gerçek sahibi ayırt edemez; gerçek etkinliklerde bu yüzden kimlik kontrolü yapılır. Ayrıca site içinde kamera açan bir okuyucu yoktur — görevli telefonun kendi kamera uygulamasıyla okutur.

### Çerez imzalama anahtarları neden veritabanında?

ASP.NET, oturum çerezlerini ve antiforgery jetonlarını bir anahtar takımıyla imzalar. Varsayılanda bu anahtarlar **dosya sistemine** yazılır ve container'da bu iki soruna yol açar:

- Container yeniden oluşturulduğunda anahtarlar kaybolur; herkes oturumdan düşer, açık formlar "antiforgery doğrulanamadı" hatası verir.
- Uygulamanın iki kopyası ayrı anahtar üretir; biri diğerinin çerezini doğrulayamaz. Kullanıcı kopyalar arasında gezindikçe sürekli çıkış yapmış olur — yani aşağıdaki yatay ölçekleme çalışmasını boşa çıkarır.

Anahtarlar `DataProtectionKeys` tablosunda tutuluyor (`PersistKeysToDbContext`). Bu, projenin genel yaklaşımıyla da tutarlı: koordinasyon noktası veritabanı. `SetApplicationName` sabitlenmiştir; aksi halde farklı container adları farklı anahtar halkaları üretirdi.

Bu eksik, Docker kurulumu ilk kez gerçekten çalıştırıldığında başlangıç loglarındaki uyarıdan fark edildi — kod okuyarak görülebilecek bir şey değildi.

Başlangıçta hâlâ görünen `No XML encryptor configured` uyarısı **beklenen** durumdur: anahtarlar veritabanında şifrelenmeden saklanır. Şifrelemek bir sertifika (ör. Azure Key Vault, DPAPI) gerektirir; veritabanına erişebilen zaten uygulamanın tüm verisine erişebildiği için bu katman burada gerçek bir koruma eklemez. Gerçek bir dağıtımda anahtar yönetimi ayrı bir konudur ve orada değerlendirilmelidir.

### Uygulamanın iki kopyası aynı anda çalışabilir mi?

Projenin baştan beri iddiası, kilitlemenin uygulama belleğinde değil veritabanında olduğu ve bu sayede yatay ölçeklenebildiğiydi. Bilet satın alma akışı bunu gerçekten karşılıyordu, ama iki nokta karşılamıyordu:

**1. Başlangıç (migration + seed).** `DbSeeder` "hiç etkinlik yoksa örnek veriyi yaz" diye çalışır — klasik oku-sonra-yaz. İki kopya aynı anda başlarsa ikisi de boş görüp ikisi de yazabilirdi. Çözüm, migration ve seed'i SQL Server'ın `sp_getapplock` yordamıyla alınan **dağıtık bir kilit** içine almak:

```
sp_getapplock @Resource='BiletSatis_Baslangic', @LockMode='Exclusive', @LockOwner='Session'
```

Kilit bağlantı oturumuna bağlıdır; bu yüzden bağlantı iş bitene kadar açık tutulur. Kilidi tutan süreç çökerse bağlantı düşer ve kilit kendiliğinden serbest kalır — takılı kalmaz. C# tarafındaki `lock` burada işe yaramazdı, çünkü her kopyanın belleği ayrıdır.

**2. Bildirim görevi.** Görev "bildirilmemiş" kayıtları okuyup e-postayı gönderiyor, sonra bayrağı işaretliyordu. İki kopya aynı satırları okuyup aynı e-postayı iki kez gönderebilirdi. Artık **önce sahiplenme, sonra gönderme** sırası uygulanıyor:

```sql
UPDATE TOP (50) Biletler
SET BildirimKilitZamani = GETUTCDATE()
OUTPUT INSERTED.Id
WHERE Durum = 'Satıldı' AND BildirimGonderildi = 0
  AND (BildirimKilitZamani IS NULL OR BildirimKilitZamani < DATEADD(MINUTE, -5, GETUTCDATE()))
```

Sahiplenme tek atomik `UPDATE` olduğu için her kaydı yalnızca bir kopya alır. Sahiplenen süreç çökerse 5 dakikalık kira dolar ve kayıt yeniden denenebilir hâle gelir. Gönderim hata verirse sahiplenme **hemen** bırakılır; aksi halde geçici bir SMTP hatası yüzünden bildirim kira süresi dolana kadar bekletilirdi (bunu, mevcut "hata sonrası tekrar dene" testleri kırılarak fark edildi).

Koruma ölçüldü: sahiplenme adımı kaldırılıp iki kopya aynı anda çalıştırıldığında test 3/3 kırılıyor, sahiplenmeyle 3/3 geçiyor.

### Değerlendirme hakkı neye bağlı?

Çoğu sitede "satın aldıysan yorum yazabilirsin" kuralı vardır. Burada çıta bir kademe yukarıda: kullanıcının o etkinliğe ait **satılmış ve kapıda okutulmuş** (`GirisYapildi = 1`) bir bileti olmalı. Bilet alıp gitmeyen biri yorum yazamaz.

Bu kural, kapı kontrolü özelliğinin ürettiği veriyi kullanır — `Biletler.GirisYapildi` alanı hem girişte tek kullanım garantisi verir hem de burada "gerçekten oradaydı" kanıtı olur. Yorumların altındaki "… tarihinde … mekanında izledi" satırı uydurma bir rozet değil, bu kaydın karşılığıdır.

Kontrol yalnızca arayüzde formu gizlemekle yapılmaz; `KaydetAsync` her çağrıda hakkı yeniden doğrular, çünkü form doğrudan da gönderilebilir.

`(EtkinlikId, KullaniciId)` üzerindeki **benzersiz dizin** aynı kişinin iki kez oy kullanıp ortalamayı bozmasını veritabanı seviyesinde engeller — iki istek aynı anda gelirse ikincisi reddedilir ve güncelleme olarak ele alınır.

### Aynı koltuk iki kez ödenirse ne olur?

Kullanıcı iki sekmede ödemeye geçip ikisini de tamamlarsa Stripe iki ayrı ödeme alır. İade akışı olmadığı için bunu geri çeviremiyoruz, ama sessizce geçmesi kabul edilemez: satın alma sırasında Stripe oturumunun kimliği `Biletler.OdemeReferansi` alanına yazılır. İkinci ödeme farklı bir oturuma ait olduğu için tespit edilir, `LogError` ile kaydedilir ve kullanıcıya "fazla tutar için iletişime geçin" denir. Aynı oturumun tekrarı (başarı sayfasının yenilenmesi) çifte ödeme sayılmaz.

Önleyici olarak ödeme butonu ilk gönderimde kilitlenir; bu çift tıklamayı engeller ama iki ayrı sekmeyi engelleyemez.

### E-posta doğrulama zorunlu hâle getirilirken mevcut kullanıcılar nasıl korundu?

`RequireConfirmedAccount = true` yapıldığı anda, o güne kadar açılmış hesapların tamamı giriş yapamaz hâle gelirdi — çünkü hiçbiri doğrulama fırsatı bulamamıştı (geliştirme veritabanındaki 4 hesabın yalnızca 1'i doğrulanmıştı). Bu yüzden ayarla birlikte bir **veri migration'ı** eklendi:

```sql
UPDATE AspNetUsers SET EmailConfirmed = 1 WHERE EmailConfirmed = 0
```

Özellikten önce açılmış hesaplar doğrulanmış sayılır; bundan sonra açılanlar normal akıştan geçer. Migration geri alınamaz (hangi hesabın önceden doğrulanmış olduğu bilgisi saklanmıyor), bu bilinçli bir tercihtir.

### Hesap adresleri neden ele verilmiyor?

"Şifremi unuttum" formu, adres kayıtlı olsun ya da olmasın **aynı sayfayı** gösterir. Farklı cevap verilseydi form, hangi e-postaların sisteme kayıtlı olduğunu tarayan bir araca dönerdi. Aynı sebeple giriş hatası da tek mesajdır: "E-posta veya şifre hatalı."

Şifre başarıyla sıfırlandığında hesabın kilidi de açılır (`SetLockoutEndDateAsync(null)`); aksi halde kullanıcı yeni şifresiyle bile kilit süresi dolana kadar giremezdi.

Identity jetonları `+` ve `/` içerebildiği için adres satırında bozulmamaları adına Base64Url ile kodlanıp taşınır.

### CSP neden var, XSS zaten engellenmiyor mu?

Engelleniyor: Razor `@yorum` yazdığında metni otomatik kaçırır, gömülü script çalışmaz, ekranda düz yazı görünür. Ama bu güvenlik **tek bir alışkanlığa** bağlı. Yarın "yorumda kalın yazı olsun" diye `@Html.Raw(...)` yazılırsa ya da yeni bir özellikte kaçırma atlanırsa açık oluşur.

CSP'nin amacı hatayı önlemek değil, **hatayı ölümcül olmaktan çıkarmak.** Tarayıcıya "bu sayfada yalnızca şu kaynaklardan script çalıştır" denir; enjekte edilen script kurala uymadığı için çalışmaz.

Script'ler için **nonce** kullanılır: her istekte rastgele bir değer üretilip hem kendi script etiketlerimize hem de başlığa yazılır. Saldırganın enjekte ettiği script bu değeri bilemez, çünkü her sayfa yüklemesinde değişir. Sadece "satır içi script yasak" denseydi kendi script'lerimiz de çalışmazdı.

**Aşamalı tercih:** script'ler sıkı, stiller şimdilik serbest. İkisi aynı tehlikede değil — enjekte edilen script senin adına istek atar, sayfayı değiştirir, form ekler; enjekte edilen stil yalnızca görüntüyü bozar.

Bunun bir bedeli var: `onclick="..."` / `onsubmit="..."` gibi satır içi olay öznitelikleri nonce alamaz, CSP altında çalışmazlar. Projedeki ikisi (ödeme butonunun çift gönderim kilidi ve etkinlik silme onayı) `data-*` özniteliklerine çevrilip davranışları `site.js`'e taşındı.

**Dikkat edilecek bir nokta:** `form-action` yalnızca formun gittiği adresi değil, gönderimin ardından gelen **yönlendirmeleri** de kapsar. Ödeme formu kendi sunucumuza POST eder, sunucu da Stripe'ın ödeme sayfasına yönlendirir. `form-action` içinde Stripe alan adı yoksa tarayıcı bu yönlendirmeyi **sessizce** engeller: sunucu tarafında her şey başarılı görünür (oturum oluşur, 302 döner) ama kullanıcı sepet sayfasında kalır. Bu yüzden `https://checkout.stripe.com` listeye eklenmiştir.

**Stiller de sıkı.** `style="..."` öznitelikleri de nonce alamaz — nonce yalnızca `<style>` etiketlerinde çalışır. Bu yüzden arayüzdeki 28 satır içi stilin tamamı kaldırıldı:

- **22 sabit stil** CSS sınıflarına taşındı (`durum-ikonu`, `girdi-dar`, `ticket-seat-buyuk` …).
- **5 değişken stil** `data-*` özniteliğinden okunup JS ile atanıyor. CSSOM üzerinden stil yazmak CSP tarafından engellenmez, çünkü sayfaya metin enjekte edilmiyor.
- **1 tanesi JS ile atanamadı:** kart giriş animasyonunun kademeli gecikmesi. Kart `opacity: 0` ile animasyona sayfa çözümlenirken başlıyor; gecikmeyi `DOMContentLoaded`'da vermek animasyonu yeniden tetikleyip titremeye yol açıyor. O değer `.gecikme-0` … `.gecikme-9` sınıflarıyla, sayfa çözümlenirken uygulanıyor (kart sayısı sınırsız olabilsin diye 10'lu döngüyle: `i % 10`).

Tarayıcıda ölçüldü: nonce'suz bir `<script>` enjekte edildiğinde çalışmıyor ve konsola *"Executing inline script violates the following Content Security Policy directive"* hatası düşüyor. Aynı sayfada jQuery, Bootstrap, `site.js` ve Google Fonts normal şekilde yükleniyor.

### Veritabanı bütünlüğü

Şema denetiminde iki eksik bulundu ve kapatıldı:

- **`Biletler.RezerveEdenKullaniciId` üzerinde dizin yoktu.** "Biletlerim", "Sepetim" ve profil özeti hep bu alana göre filtreliyor; dizin olmadan her sorgu tüm bilet tablosunu tarıyordu.
- **`RezervasyonKuyrugu`'nun `Etkinlikler`'e foreign key'i yoktu.** Kuyruk kayıtlarının silinmesi, etkinlik silme kodunun bunu hatırlamasına bağlıydı — unutulmaya açık bir tasarım. İlişki artık şemada, temizlik cascade ile yapılıyor. Testler bu eksikliği "sahte etkinlik id'si" kullanarak sömürüyordu; onlar da gerçek etkinlik oluşturacak şekilde düzeltildi.

Ayrıca `(EtkinlikId, KullaniciId)` dizini eklendi: "bu kullanıcı zaten sırada mı" kontrolü hem kuyruk sayfasında hem de sıraya girmedeki `NOT EXISTS` kontrolünde kullanılıyor ve dizin olmadan o kontrolün aldığı aralık kilidi gereksiz genişti.

Veri tutarlılığı 14 ayrı kontrolle tarandı (öksüz kayıt, sahipsiz sepet, geçersiz durum değeri, girişi olmadan yorum bırakılması, negatif fiyat, silinmiş kullanıcıya ait bilet vb.); geliştirme veritabanında hepsi temiz çıktı.

### Üretime çıkarken

`appsettings.json` içindeki `AllowedHosts` değeri `*`'dır. Bu, uygulamanın hangi alan adıyla çağrılırsa çağrılsın cevap vermesi demektir; üretimde Host başlığı manipülasyonuna kapı aralar. Yayına çıkarken gerçek alan adıyla değiştirin:

```json
"AllowedHosts": "biletsatis.com;www.biletsatis.com"
```

`Eposta:SiteAdresi` de aynı şekilde gerçek adresle güncellenmelidir — doğrulama ve şifre sıfırlama bağlantıları bu adresi kullanır.

### Güvenlik önlemleri

| Önlem | Neden |
|---|---|
| Hesap kilidi (5 hatalı deneme → 5 dk) | Şifre sınırsız denenebiliyordu |
| Giriş/kayıt **POST**'larında hız sınırı (IP başına 15/dk) | Hesap kilidi tek hesabı korur; bu, çok sayıda hesaba yapılan taramayı yavaşlatır |
| `HttpOnly` + `SameSite=Lax` + üretimde `Secure` çerez | XSS'te oturum çalınmasını ve siteler arası kullanımı zorlaştırır |
| `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` | MIME tahmini, tıklama hırsızlığı ve adres sızıntısına karşı |
| CSP — script'ler için nonce | Bir gün metin kaçırma atlanırsa enjekte edilen script yine de çalışmasın |
| Yönetici şifresi yapılandırmadan | Koda gömülü şifre üretimde herkesin bildiği bir yönetici hesabı demekti |
| `Stripe:SecretKey` üretimde zorunlu | Anahtarsız uygulama ayağa kalkıp ödeme adımında patlıyordu |
| Giriş hatasında tek mesaj | "E-posta veya şifre hatalı" — hangi adresin kayıtlı olduğu ele verilmez |

Hız sınırı bilinçli olarak yalnızca **POST** uçlarına uygulanır ve hesap kilidi eşiğinin (5) üstünde tutulur. Sayfa açılışları da sayılsaydı her giriş denemesi iki isteğe mal olur, kullanıcı anlaşılır kilit mesajını görmeden sınıra takılırdı. Sınıra takılan istek çıplak `429` yerine `/Account/CokFazlaDeneme` sayfasına yönlendirilir; yanıt `Retry-After` başlığı taşır.

Yönetici hesabı artık yalnızca `Yonetici:Sifre` tanımlıysa oluşturulur. Geliştirmede tanımlı değilse bilinen geliştirme şifresi kullanılır; üretimde tanımlı değilse hesap **açılmaz** ve uyarı loglanır.

```bash
cd BiletSatis.Web
dotnet user-secrets set "Yonetici:Eposta" "siz@ornek.com"
dotnet user-secrets set "Yonetici:Sifre" "guclu-bir-sifre"
```

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
    Domain/                # Etkinlik, Bilet, RezervasyonKuyrugu, Degerlendirme, EtkinlikKategorisi
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

### Kapı kontrolü imza anahtarı

Bilet QR kodları bu anahtarla imzalanır. Geliştirmede tanımlı değilse sabit bir geçici anahtar kullanılır; **üretimde tanımlı değilse uygulama başlamaz** — anahtarsız imza tahmin edilebilir olur ve sahte bilet üretilebilir.

```bash
cd BiletSatis.Web
dotnet user-secrets set "Giris:ImzaAnahtari" "uzun-ve-rastgele-bir-deger"
```

Anahtar değiştirilirse önceden gönderilmiş biletlerin QR kodları geçersiz olur.

### E-posta bildirimi yapılandırması

Proje **SMTP hesabı olmadan da çalışır**. `Eposta:SmtpSunucu` boşsa e-postalar gönderilmez, `logs/eposta/` klasörüne `.html` dosyası olarak yazılır — bildirimlerin içeriği tarayıcıda açılıp kontrol edilebilir.

Gerçek gönderim için SMTP bilgilerini girin (şifre `appsettings.json`'a **yazılmaz**, user-secrets'ta saklanır):

```bash
cd BiletSatis.Web
dotnet user-secrets set "Eposta:SmtpSunucu" "smtp.gmail.com"
dotnet user-secrets set "Eposta:KullaniciAdi" "hesabiniz@gmail.com"
dotnet user-secrets set "Eposta:Sifre" "uygulama-sifreniz"
```

`appsettings.json` içindeki `Eposta:SiteAdresi` değerini de sitenin gerçek adresiyle güncelleyin — e-postadaki bağlantılar bu adresi kullanır, göreli adres e-posta istemcilerinde çalışmaz.

> **Gmail kullanacaksanız:** Uygulama şifresi almadan **önce** iki adımlı doğrulamayı açın. 2FA kapalıyken üretilen şifreleri Google kabul etmez ve `535 Username and Password not accepted` hatası alırsınız. Şifreyi `abcd efgh ijkl mnop` biçiminde boşluklu yapıştırabilirsiniz; kod boşlukları temizler.

Gmail dışında herhangi bir SMTP sağlayıcısı da çalışır (Brevo, Mailtrap, kurumsal sunucu). Yalnızca `Eposta:SmtpSunucu`, `KullaniciAdi` ve `Sifre` değerlerini değiştirmek yeterlidir; kodda değişiklik gerekmez.

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
| `BiletRezervasyonServisiTests` | Eşzamanlı sepete ekleme, kilit süresi, ödeme tamamlama, çoklu koltukta "hepsi ya da hiçbiri", kesişen koltuk kümeleri, kilit uzatma |
| `KuyrukServisiTests` | Sıra numarası benzersizliği, FIFO hak tanıma, süre dolumu, aynı kullanıcının eşzamanlı katılımında tek kayıt, eşzamanlı hak tanımada tutarlılık |
| `AdminEtkinlikSilmeTests` | Satılmış bilet koruması, bilet ve kuyruk kayıtlarının temizlenmesi |
| `MekanBilgisiTests` | Şehir/salon ayrıştırma uç durumları |
| `EtkinlikKartVmTests` | Geri sayım metni, kıtlık uyarısı eşikleri |
| `AdminOzetTests` | Gelir/doluluk hesapları, sıfıra bölme durumu |
| `ProfilVmTests` | Avatar baş harfleri |
| `KuyrukBildirimServisiTests` | Bildirim gönderimi, tekrar gönderim engeli, hata sonrası yeniden deneme |
| `BiletBildirimServisiTests` | Satın alma bildirimi, e-posta içeriği, QR kodunun gömülmesi, tekrar gönderim engeli |
| `BiletKoduServisiTests` | İmza doğrulama; sahte imza, numara değiştirme ve farklı anahtar denemeleri |
| `BiletDevirServisiTests` | Bilet devri: eski QR'ın geçersizleşmesi, kapıda okutulmuş biletin devredilememesi, eşzamanlı devir denemeleri |
| `KimlikEpostaServisiTests` | Doğrulama ve şifre sıfırlama e-postalarının içeriği, gönderim hatasının yukarı taşınması |
| `EtkinlikEsZamanliDuzenlemeTests` | İki yöneticinin aynı etkinliği düzenlemesi (kayıp güncelleme koruması) |
| `UctanUcaAkisTests` | Giriş yapmış kullanıcı olarak tüm akışlar (aşağıya bakınız) |

### Uçtan uca testler

Servis testleri sınıfları tek tek çağırır; **uçtan uca testler** uygulamanın tamamını bellek içi bir test sunucusunda ayağa kaldırıp gerçek HTTP istekleri gönderir. Böylece yönlendirme, yetkilendirme filtreleri, antiforgery, model bağlama ve Razor görünümleri de kapsama girer — yani "servis doğru ama sayfa bozuk" durumu yakalanır.

Kapsanan akışlar:

| Akış | Doğrulanan |
|---|---|
| Salon haritası | Koltuklar ve seçim çubuğu render ediliyor, sayfada hiç satır içi `style` kalmamış (CSP uyumu) |
| Çoklu koltuk | 3 koltuk sepete giriyor, sepet toplamı doğru |
| Çakışma | Koltuklardan biri başkasındaysa hiçbiri sepete girmiyor |
| Sınır | 6'dan fazla koltuk reddediliyor |
| Vazgeçme | Koltuk tekrar satışa çıkıyor |
| Yetki | Başkasının sepeti görünmüyor ve iptal edilemiyor |
| Rol | Normal kullanıcı admin sayfalarına ve kapı kontrolüne giremiyor |
| Yönetici | Panel açılıyor, satılmış bileti olan etkinlik silinemiyor |
| Değerlendirme | Kapıdan geçmeyen yazamıyor, geçen yazabiliyor ve yorumu sayfada görünüyor |
| Kuyruk | İki kez katılım denemesinde tek kayıt oluşuyor |
| Güvenlik başlıkları | CSP nonce'lu, `unsafe-inline` içermiyor, Stripe yönlendirmesine izin veriyor |

Ödeme adımı kapsam dışıdır: Stripe'ın kendi sunucusunda oturum açılmasını gerektirir. Ödemenin veritabanı tarafı `BiletRezervasyonServisiTests` içinde ayrıca test edilir.

Testler `Guvenlik:EpostaDogrulamaZorunlu` ve `Guvenlik:HizSiniriAktif` kapalı, arka plan görevleri devre dışı bırakılmış bir yapılandırmayla çalışır — aksi halde görevler sepet kilitlerini düşürüp sonuçları belirsiz hâle getirirdi.
| `GirisServisiTests` | Kapı kontrolü: tek kullanım, 20 eşzamanlı okutmada tek giriş, satılmamış bilet reddi |
| `DegerlendirmeServisiTests` | Değerlendirme hakkı (okutulmamış bilet reddi), geçersiz puan, tek kayıt kuralı, eşzamanlı istek, ortalama ve dağılım hesabı |

### Yük testleri (k6)

```bash
k6 run loadtests/k6/add-to-cart-test.js
k6 run loadtests/k6/queue-fairness-test.js
```

Detaylar için [loadtests/k6/README.md](loadtests/k6/README.md).

## Bilinen Kapsam Dışı Konular

- Production dağıtımı (deployment/hosting) henüz yapılmadı.
- Satın alma sonrası iade/iptal akışı yok (sadece ödeme öncesi sepetten vazgeçme mevcut). Bu yüzden ödeme 15 dakikalık uzatılmış kilidi de aşarsa para alınmış olmasına rağmen bilet verilemez; durum loglanır ve kullanıcı uyarılır, iade elle yapılır.
- Bildirim gönderimi **en az bir kez** (at-least-once) garantisi verir. Tekrar gönderim penceresi tek kayda indirildi (her e-postadan sonra ayrı yazma), ama sıfırlanamaz. Kayıt sahiplenilip e-posta gönderildikten hemen sonra süreç çökerse, kira süresi dolduğunda aynı bildirim tekrar gönderilebilir. Tam olarak bir kez garantisi, e-posta gönderimiyle veritabanı yazmasının aynı işlemde olmasını gerektirir; bu da dış bir servisle mümkün değildir.
- Arayüzde Google Fonts dışında dış kaynak yoktur; CSP bu iki alan adı dışında her şeyi kendi sunucusuyla sınırlar.
- Kapı kontrolünde çevrimdışı mod yok; doğrulama için internet bağlantısı gerekir.
- Site içinde kamera açan QR okuyucu yok; görevli telefonun kendi kamera uygulamasını kullanır.
- Arayüzdeki filtreler (arama, kategori, şehir, fiyat, sıralama) istemci tarafında çalışır. Tüm etkinlikler tek sayfada render edildiği için etkinlik sayısı büyüdüğünde sunucu tarafı filtreleme ve sayfalama gerekir.
- Şehir bilgisi ayrı bir sütun değil, `Mekan` alanından ayrıştırılır (bkz. Mimari Kararlar).

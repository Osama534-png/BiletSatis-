using System.Net;
using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BiletSatis.Tests;

/// <summary>
/// Kod denetiminde bulunan açıkların gerçekten kapandığını kanıtlayan testler.
/// Her biri düzeltmeden önce kırılıyordu; bulgular README'de "Denetim" başlığında.
///
/// Servis testlerinden farkı, hepsinin gerçek HTTP boru hattından geçmesi: bu
/// bulguların bir kısmı yalnızca controller katmanında görünüyordu, servisler
/// tek başına doğru çalışıyordu.
/// </summary>
[Collection("Veritabanı")]
public class DenetimBulgulariTests : IClassFixture<UygulamaFabrikasi>
{
    private readonly UygulamaFabrikasi _fabrika;

    public DenetimBulgulariTests(UygulamaFabrikasi fabrika) => _fabrika = fabrika;

    private static string BenzersizEposta(string on) => $"{on}-{Guid.NewGuid():N}@test.local";

    private static async Task<int> GecmisEtkinlikOlustur(BiletModeli model = BiletModeli.KoltukSecmeli, int koltuk = 3)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Gecmis {Guid.NewGuid():N}",
            Mekan = "Test Salonu, İzmir",
            Tarih = DateTime.Now.AddDays(-3),
            BiletModeli = model
        };

        for (var i = 1; i <= koltuk; i++)
        {
            etkinlik.Biletler.Add(new Bilet { KoltukNo = $"A-{i:00}", Fiyat = 300m, Durum = BiletDurumu.Satista });
        }

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private static async Task<int> EtkinlikOlustur(BiletModeli model = BiletModeli.KoltukSecmeli, int koltuk = 3)

    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Denetim {Guid.NewGuid():N}",
            Mekan = "Test Salonu, İzmir",
            Tarih = DateTime.UtcNow.AddDays(15),
            BiletModeli = model
        };

        for (var i = 1; i <= koltuk; i++)
        {
            etkinlik.Biletler.Add(new Bilet { KoltukNo = $"A-{i:00}", Fiyat = 300m, Durum = BiletDurumu.Satista });
        }

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
    }

    // ---------- Bulgu 1: kayıp güncelleme koruması controller'a bağlanmamıştı ----------

    /// <summary>
    /// Satır sürümü (rowversion) şemada vardı ve EF seviyesinde çalışıyordu, ama
    /// düzenleme ekranı POST'ta satırı veritabanından yeniden okuyup üstüne yazıyordu:
    /// karşılaştırılan sürüm "az önce okuduğum" sürüm olduğu için çakışma hiç oluşmuyordu.
    /// Yani koruma yalnızca testte vardı, gerçek akışta yoktu.
    /// </summary>
    [Fact]
    public async Task EtkinlikDuzenleme_FormAcikkenBaskasiKaydettiyse_UstuneYazmamali()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("yonetici"), rol: "Admin");

            // Birinci yönetici formu açıyor.
            var form = await istemci.GetStringAsync($"/Admin/EtkinlikDuzenle?id={etkinlikId}");
            var jeton = UygulamaFabrikasi.AntiforgeryJetonu(form);

            // Form açıkken ikinci yönetici kaydediyor.
            using (var db = DatabaseFixture.CreateContext())
            {
                var etkinlik = await db.Etkinlikler.FirstAsync(e => e.Id == etkinlikId);
                etkinlik.Ad = "İkinci yöneticinin kaydettiği ad";
                await db.SaveChangesAsync();
            }

            // Birinci yönetici şimdi kendi (artık eski) formunu gönderiyor.
            var cevap = await istemci.PostAsync("/Admin/EtkinlikDuzenle", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Id"] = etkinlikId.ToString(),
                    ["Ad"] = "Birinci yöneticinin eski formu",
                    ["Mekan"] = "Test Salonu, İzmir",
                    ["Kategori"] = nameof(EtkinlikKategorisi.Konser),
                    ["BiletModeli"] = nameof(BiletModeli.KoltukSecmeli),
                    ["Aciklama"] = "",
                    ["YasSiniri"] = "0",
                    ["Tarih"] = DateTime.UtcNow.AddDays(15).ToString("yyyy-MM-ddTHH:mm"),
                    ["SatirSurumu"] = SatirSurumuAlani(form),
                    ["__RequestVerificationToken"] = jeton
                }));

            // Kayıt kabul edilmemeli: başarılı kayıt Index'e yönlendiriyor.
            Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

            // Razor ASCII dışı harfleri HTML varlığına çevirir ("ş" → "&#x15F;"),
            // bu yüzden mesajın yalnızca ASCII parçası aranıyor.
            Assert.Contains("biri taraf", await cevap.Content.ReadAsStringAsync());

            // İkinci yöneticinin değişikliği sessizce ezilmemiş olmalı.
            using var kontrol = DatabaseFixture.CreateContext();
            var guncel = await kontrol.Etkinlikler.AsNoTracking().FirstAsync(e => e.Id == etkinlikId);
            Assert.Equal("İkinci yöneticinin kaydettiği ad", guncel.Ad);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task EtkinlikDuzenleme_ArayaKimseGirmezse_NormalKaydetmeli()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("yonetici"), rol: "Admin");

            var form = await istemci.GetStringAsync($"/Admin/EtkinlikDuzenle?id={etkinlikId}");

            var cevap = await istemci.PostAsync("/Admin/EtkinlikDuzenle", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Id"] = etkinlikId.ToString(),
                    ["Ad"] = "Tek yönetici kaydetti",
                    ["Mekan"] = "Test Salonu, İzmir",
                    ["Kategori"] = nameof(EtkinlikKategorisi.Konser),
                    ["BiletModeli"] = nameof(BiletModeli.KoltukSecmeli),
                    ["Aciklama"] = "",
                    ["YasSiniri"] = "0",
                    ["Tarih"] = DateTime.UtcNow.AddDays(15).ToString("yyyy-MM-ddTHH:mm"),
                    ["SatirSurumu"] = SatirSurumuAlani(form),
                    ["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(form)
                }));

            Assert.Equal(HttpStatusCode.Found, cevap.StatusCode);

            using var kontrol = DatabaseFixture.CreateContext();
            var guncel = await kontrol.Etkinlikler.AsNoTracking().FirstAsync(e => e.Id == etkinlikId);
            Assert.Equal("Tek yönetici kaydetti", guncel.Ad);
        }
        finally { await Temizle(etkinlikId); }
    }

    /// <summary>Formdaki gizli satır sürümü alanını okur; alan yoksa koruma bağlanmamış demektir.</summary>
    private static string SatirSurumuAlani(string html)
    {
        var eslesme = System.Text.RegularExpressions.Regex.Match(
            html, """name="SatirSurumu"[^>]*value="([^"]*)""");

        Assert.True(eslesme.Success, "Düzenleme formunda gizli SatirSurumu alanı yok — kayıp güncelleme koruması bağlanmamış.");

        var deger = eslesme.Groups[1].Value;

        // Alan base64 taşımalı. byte[] düz ToString() ile basılırsa "System.Byte[]"
        // yazılır ve geri bağlanamaz; koruma her kaydetmede çakışma sanır.
        Assert.True(
            deger.Length > 0 && !deger.Contains("Byte", StringComparison.OrdinalIgnoreCase),
            $"SatirSurumu alanı base64 taşımıyor: '{deger}'");

        return deger;
    }

    // ---------- Bulgu 2: e-posta değiştiren kullanıcı hesabından kilitleniyordu ----------

    /// <summary>
    /// Identity'nin <c>SetEmailAsync</c> metodu adresi değiştirirken doğrulama
    /// bayrağını da sıfırlar. Profil ekranı bunu yapıp doğrulama e-postası
    /// göndermiyordu: e-posta doğrulaması zorunlu olduğu için kullanıcı çıkış
    /// yaptığı anda hesabına bir daha giremiyordu. Üstelik adres hemen değiştiği
    /// için yanlış yazılan bir adres hesabı kalıcı olarak erişilemez yapardı.
    ///
    /// Doğru davranış: adres onaylanana kadar değişmez.
    /// </summary>
    [Fact]
    public async Task EpostaDegistirme_OnaylananaKadar_HesapErisilebilirKalmali()
    {
        var eskiEposta = BenzersizEposta("profil");
        var yeniEposta = BenzersizEposta("profil-yeni");

        var istemci = await _fabrika.GirisYapmisIstemciAsync(eskiEposta);

        var sayfa = await istemci.GetStringAsync("/Profil");
        var cevap = await istemci.PostAsync("/Profil/BilgileriGuncelle", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Bilgiler.Ad"] = "Test Kullanıcı",
                ["Bilgiler.Email"] = yeniEposta,
                ["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(sayfa)
            }));

        Assert.Equal(HttpStatusCode.Found, cevap.StatusCode);

        using var kapsam = _fabrika.Services.CreateScope();
        var kullaniciYoneticisi = kapsam.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Adres henüz değişmemeli ve hesap doğrulanmış kalmalı — aksi halde
        // kullanıcı bir sonraki girişte kapıda kalırdı.
        var kullanici = await kullaniciYoneticisi.FindByEmailAsync(eskiEposta);
        Assert.NotNull(kullanici);
        Assert.True(kullanici!.EmailConfirmed, "Adres onaylanmadan doğrulama bayrağı düşürülmüş — kullanıcı giriş yapamaz hâle gelir.");
        Assert.Equal(eskiEposta, kullanici.UserName);

        // Yeni adres henüz kimseye ait olmamalı.
        Assert.Null(await kullaniciYoneticisi.FindByEmailAsync(yeniEposta));
    }

    /// <summary>Onay bağlantısı kullanılınca adres gerçekten değişmeli.</summary>
    [Fact]
    public async Task EpostaDegistirme_OnayBaglantisiKullanilinca_AdresDegismeli()
    {
        var eskiEposta = BenzersizEposta("profil-onay");
        var yeniEposta = BenzersizEposta("profil-onay-yeni");

        var istemci = await _fabrika.GirisYapmisIstemciAsync(eskiEposta);

        string kullaniciId;
        string jeton;
        using (var kapsam = _fabrika.Services.CreateScope())
        {
            var yonetici = kapsam.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var kullanici = await yonetici.FindByEmailAsync(eskiEposta);
            kullaniciId = kullanici!.Id;
            jeton = await yonetici.GenerateChangeEmailTokenAsync(kullanici, yeniEposta);
        }

        var adres = "/Profil/EpostaDegisikliginiOnayla" +
                    $"?kullaniciId={Uri.EscapeDataString(kullaniciId)}" +
                    $"&yeniEposta={Uri.EscapeDataString(yeniEposta)}" +
                    $"&jeton={Uri.EscapeDataString(Base64Url(jeton))}";

        var cevap = await istemci.GetAsync(adres);
        Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

        using var kontrolKapsami = _fabrika.Services.CreateScope();
        var kullaniciYoneticisi = kontrolKapsami.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var guncel = await kullaniciYoneticisi.FindByIdAsync(kullaniciId);
        Assert.Equal(yeniEposta, guncel!.Email);
        Assert.Equal(yeniEposta, guncel.UserName);
        Assert.True(guncel.EmailConfirmed);
    }

    private static string Base64Url(string metin) =>
        Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes(metin));

    // ---------- Bulgu 3: sayfa numarası taşması sunucuyu 500'e düşürüyordu ----------

    /// <summary>
    /// Sayfa numarası adres çubuğundan geliyor ve sınırlanmıyordu.
    /// <c>(sayfa - 1) * sayfaBoyutu</c> çarpımı int sınırını aşınca negatife
    /// dönüyor, SQL Server "OFFSET negatif olamaz" diyerek isteği düşürüyordu.
    /// Kimlik doğrulaması gerektirdiği için dışarıdan sömürülemezdi, ama giriş
    /// yapmış herkes tek adresle 500 üretebiliyordu.
    /// </summary>
    [Theory]
    [InlineData(2000000000)]
    [InlineData(int.MaxValue)]
    [InlineData(-5)]
    public async Task AnaSayfa_AsiriSayfaNumarasi_SunucuyuDusurmemeli(int sayfa)
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("sayfalama"));

        var cevap = await istemci.GetAsync($"/?sayfa={sayfa}");

        Assert.True(
            cevap.StatusCode is HttpStatusCode.OK or HttpStatusCode.Found,
            $"Beklenmeyen durum kodu: {(int)cevap.StatusCode}");
    }

    // ---------- Bulgu 13: ödeme dönüş ucu eksik/bozuk girdide çöküyordu ----------

    /// <summary>
    /// <c>/Biletler/OdemeBasarili</c> Stripe'ın geri yönlendirdiği adres. Parametresiz
    /// ya da uydurma bir <c>session_id</c> ile açıldığında Stripe istemcisi
    /// <c>StripeException</c> değil <c>ArgumentException</c> fırlatıyor; bu da
    /// yakalanmadığı için istek 500 ile düşüyordu.
    ///
    /// Giriş yapmış herkes adres çubuğuna yazarak tetikleyebilirdi. Ödeme dönüşü
    /// dış servisten gelen veriyle çalıştığı için burada hiçbir girdi güvenilmez
    /// kabul edilmeli.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("cs_test_uydurma")]
    [InlineData("../../etc/passwd")]
    public async Task OdemeDonusu_GecersizOturumNumarasi_CokmemeliVeSepeteDonmeli(string oturum)
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("odeme"));

        var cevap = await istemci.GetAsync($"/Biletler/OdemeBasarili?session_id={Uri.EscapeDataString(oturum)}");

        Assert.True(
            cevap.StatusCode is HttpStatusCode.Found or HttpStatusCode.OK,
            $"Beklenmeyen durum kodu: {(int)cevap.StatusCode}");
    }

    /// <summary>Parametre hiç verilmediğinde de aynı davranış beklenir.</summary>
    [Fact]
    public async Task OdemeDonusu_ParametresizAcilirsa_Cokmemeli()
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("odeme-bos"));

        var cevap = await istemci.GetAsync("/Biletler/OdemeBasarili");

        Assert.True(
            cevap.StatusCode is HttpStatusCode.Found or HttpStatusCode.OK,
            $"Beklenmeyen durum kodu: {(int)cevap.StatusCode}");
    }

    // ---------- Bulgu 11: geçmiş etkinliğe bilet satılabiliyordu ----------

    /// <summary>
    /// Satın alma yolunda hiçbir yerde etkinlik tarihine bakılmıyordu: tarihi geçmiş
    /// bir konserin koltukları listeleniyor, sepete ekleniyor ve ödemesi alınabiliyordu.
    /// Kullanıcı olmamış bir etkinliğin biletine para ödüyor, karşılığında kapıda
    /// kullanamayacağı bir QR alıyordu — üstelik iade akışı da yok.
    ///
    /// Bilet devrinde tarih kontrolü zaten vardı; eksik olan satın alma yoluydu.
    /// </summary>
    [Fact]
    public async Task GecmisEtkinlik_SepeteEklenememeli()
    {
        var etkinlikId = await GecmisEtkinlikOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("gecmis"));

            var sayfa = await istemci.GetStringAsync($"/Etkinlik/Detay?id={etkinlikId}");
            var jeton = UygulamaFabrikasi.AntiforgeryJetonu(sayfa);

            int biletId;
            using (var db = DatabaseFixture.CreateContext())
            {
                biletId = await db.Biletler.Where(b => b.EtkinlikId == etkinlikId)
                    .Select(b => b.Id).FirstAsync();
            }

            var cevap = await istemci.PostAsync("/Biletler/SepeteEkle", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["biletIds"] = biletId.ToString(),
                    ["__RequestVerificationToken"] = jeton
                }));

            Assert.Equal(HttpStatusCode.Found, cevap.StatusCode);
            Assert.DoesNotContain("Sepetim", cevap.Headers.Location?.OriginalString ?? "");

            using var kontrol = DatabaseFixture.CreateContext();
            var alinan = await kontrol.Biletler.AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum != BiletDurumu.Satista);

            Assert.Equal(0, alinan);
        }
        finally { await Temizle(etkinlikId); }
    }

    /// <summary>Genel giriş ucu da aynı kuralı uygulamalı.</summary>
    [Fact]
    public async Task GecmisEtkinlik_GenelGirisleDeAlinamamali()
    {
        var etkinlikId = await GecmisEtkinlikOlustur(BiletModeli.GenelGiris, koltuk: 5);
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("gecmis-gg"));
            var sayfa = await istemci.GetStringAsync($"/Etkinlik/Detay?id={etkinlikId}");

            var cevap = await istemci.PostAsync("/Biletler/GenelGirisSepeteEkle", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["adet"] = "2",
                    ["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(sayfa)
                }));

            Assert.DoesNotContain("Sepetim", cevap.Headers.Location?.OriginalString ?? "");

            using var db = DatabaseFixture.CreateContext();
            Assert.Equal(0, await db.Biletler.AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum != BiletDurumu.Satista));
        }
        finally { await Temizle(etkinlikId); }
    }

    /// <summary>
    /// Etkinlik sayfası kapanmamalı: geçmiş etkinliğin değerlendirmeleri okunabilmeli,
    /// yalnızca satın alma kapanmalı. Katılan kişiler yorum bırakmaya devam edebilir.
    /// </summary>
    [Fact]
    public async Task GecmisEtkinlik_DetaySayfasiAcilmali_AmaSatinAlmaGorunmemeli()
    {
        var etkinlikId = await GecmisEtkinlikOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("gecmis-detay"));

            var cevap = await istemci.GetAsync($"/Etkinlik/Detay?id={etkinlikId}");
            Assert.Equal(HttpStatusCode.OK, cevap.StatusCode);

            var html = await cevap.Content.ReadAsStringAsync();

            // Razor Türkçe harfleri HTML varlığına çevirdiği için ASCII parçalar aranıyor.
            Assert.Contains("Bu etkinlik sona erdi", html);
            Assert.DoesNotContain($"/Biletler/Index?etkinlikId={etkinlikId}", html);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Bulgu 5: kod sürümü sıfır kalan biletlerin QR'ı kapıda reddediliyordu ----------

    /// <summary>
    /// <c>KodSurumu</c> sütunu migration ile <c>defaultValue: 0</c> olarak eklenmişti;
    /// o ana kadarki bütün biletler sıfırla kaldı. Kod çözücü sıfır sürümü geçersiz
    /// sayıyor, yani sistem kendi ürettiği QR'ı kapıda "sahte bilet" diye reddediyordu.
    /// Geliştirme veritabanında 66 satılmış bilet bu durumdaydı.
    ///
    /// Sütunun varsayılanı 1 olmalı: EF dışından (ham SQL, toplu içe aktarma) eklenen
    /// bir bilet de geçerli bir QR taşımalı.
    /// </summary>
    [Fact]
    public async Task HamSqlIleEklenenBilet_GecerliKodSurumuAlmali()
    {
        var etkinlikId = await EtkinlikOlustur(koltuk: 0);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            // KodSurumu bilerek verilmiyor: sütunun varsayılanı devreye girmeli.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Biletler (EtkinlikId, KoltukNo, Fiyat, Durum, BildirimGonderildi, GirisYapildi)
                VALUES ({etkinlikId}, N'Z-99', 100, {BiletDurumMetni.Satildi}, 1, 0)
                """);

            var bilet = await db.Biletler.AsNoTracking().FirstAsync(b => b.EtkinlikId == etkinlikId);

            Assert.True(bilet.KodSurumu >= 1,
                $"Kod sürümü {bilet.KodSurumu} — sıfır sürümlü biletin QR kodu kapıda reddedilir.");
        }
        finally { await Temizle(etkinlikId); }
    }

    /// <summary>
    /// Veritabanında sıfır sürümlü bilet kalmamalı: kalırsa sahibinin eline geçen QR
    /// kapıda çalışmaz ve bunu ancak etkinlik günü fark ederiz.
    /// </summary>
    [Fact]
    public async Task Veritabaninda_SifirSurumluBiletKalmamali()
    {
        using var db = DatabaseFixture.CreateContext();

        var bozukSayisi = await db.Biletler.AsNoTracking().CountAsync(b => b.KodSurumu < 1);

        Assert.True(bozukSayisi == 0,
            $"{bozukSayisi} biletin kod sürümü sıfır — bu biletlerin QR kodları kapıda geçersiz görünür.");
    }

    // ---------- Bulgu 6: kültür ayarı yoktu ----------

    /// <summary>
    /// HTML'de <c>&lt;input type="number" step="0.01"&gt;</c> alanı, tarayıcının dili ne
    /// olursa olsun değeri <b>noktayla</b> gönderir ("250.50") — bu standartta böyle.
    /// Türkçe kültürde ise nokta binlik ayracıdır. Model bağlama sunucunun kültürünü
    /// kullandığı için "250.50" değeri 25050 olarak okunuyordu: yönetici 250,50 TL'lik
    /// bilet eklemek isterken 25.050 TL'lik bilet oluşuyordu.
    /// </summary>
    [Fact]
    public async Task BiletEkleme_OndalikliFiyat_DogruOkunmali()
    {
        var etkinlikId = await EtkinlikOlustur(koltuk: 0);
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("fiyat"), rol: "Admin");

            var panel = await istemci.GetStringAsync("/Admin");

            var cevap = await istemci.PostAsync("/Admin/BiletEkle", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["koltukOnEki"] = "F",
                    ["adet"] = "1",
                    // Tarayıcının number alanının gönderdiği biçim: her zaman nokta.
                    ["fiyat"] = "250.50",
                    ["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(panel)
                }));

            Assert.Equal(HttpStatusCode.Found, cevap.StatusCode);

            using var db = DatabaseFixture.CreateContext();
            var bilet = await db.Biletler.AsNoTracking().FirstOrDefaultAsync(b => b.EtkinlikId == etkinlikId);

            Assert.NotNull(bilet);
            Assert.Equal(250.50m, bilet!.Fiyat);
        }
        finally { await Temizle(etkinlikId); }
    }

    /// <summary>
    /// Arayüz tamamen Türkçe ama uygulama kültürü hiçbir yerde ayarlanmıyordu; biçimlendirme
    /// işletim sisteminin kültürüne kalıyordu. Türkçe bir Windows'ta doğru görünen fiyat ve
    /// tarihler, projenin desteklediği Docker (Linux) kurulumunda bozuluyordu:
    /// "1.500 ₺" yerine "1,500", "12 Eyl 2026" yerine "12 Sep 2026".
    /// </summary>
    [Fact]
    public async Task Sayfalar_TurkceBicimdeGostermeli()
    {
        var etkinlikId = await EtkinlikOlustur(koltuk: 4);
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE Biletler SET Fiyat = 1500 WHERE EtkinlikId = {etkinlikId}");
            }

            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("bicim"));
            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");

            // Binlik ayracı nokta olmalı (Türkçe), virgül değil (İngilizce).
            Assert.Contains("1.500", html);
            Assert.DoesNotContain("1,500", html);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Bulgu 4: genel giriş ucu bilet modelini doğrulamıyordu ----------

    /// <summary>
    /// Genel giriş ucu "hangisi olursa olsun N bilet ver" diyor. Etkinliğin
    /// gerçekten genel giriş olup olmadığı kontrol edilmediği için, koltuk seçmeli
    /// bir etkinlikte de doğrudan POST edilerek koltuklar rastgele kaptırılabiliyordu:
    /// kullanıcı salon haritasını hiç görmeden istediği sayıda koltuk alabiliyordu.
    /// </summary>
    [Fact]
    public async Task GenelGirisUcu_KoltukSecmeliEtkinlikte_Reddedilmeli()
    {
        var etkinlikId = await EtkinlikOlustur(BiletModeli.KoltukSecmeli);
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("genelgiris"));

            var sayfa = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");

            var cevap = await istemci.PostAsync("/Biletler/GenelGirisSepeteEkle", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["adet"] = "2",
                    ["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(sayfa)
                }));

            Assert.Equal(HttpStatusCode.Found, cevap.StatusCode);
            Assert.DoesNotContain("Sepetim", cevap.Headers.Location?.OriginalString ?? "");

            // Hiçbir koltuk sepete girmemiş olmalı.
            using var db = DatabaseFixture.CreateContext();
            var sepettekiler = await db.Biletler
                .AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum != BiletDurumu.Satista);

            Assert.Equal(0, sepettekiler);
        }
        finally { await Temizle(etkinlikId); }
    }
}

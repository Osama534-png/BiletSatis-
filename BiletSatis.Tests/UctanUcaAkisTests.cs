using System.Net;
using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Tests;

/// <summary>
/// Giriş yapmış kullanıcı olarak akışları uçtan uca gezer: salon haritası, çoklu
/// koltuk seçimi, sepet, vazgeçme, biletlerim, değerlendirme, admin paneli ve kapı
/// kontrolü. Servis testlerinden farkı, isteğin gerçek boru hattından geçmesi —
/// yetkilendirme, antiforgery, model bağlama ve Razor görünümleri dahil.
///
/// Ödeme adımı kapsam dışı: Stripe'ın kendi sunucusunda oturum açılmasını gerektirir.
/// Ödemenin veritabanı tarafı BiletRezervasyonServisiTests'te ayrıca test ediliyor.
/// </summary>
[Collection("Veritabanı")]
public class UctanUcaAkisTests : IClassFixture<UygulamaFabrikasi>
{
    private readonly UygulamaFabrikasi _fabrika;

    public UctanUcaAkisTests(UygulamaFabrikasi fabrika) => _fabrika = fabrika;

    private static string BenzersizEposta(string on) => $"{on}-{Guid.NewGuid():N}@test.local";

    /// <summary>Bilet dolu bir etkinlik oluşturur ve id'lerini döner.</summary>
    private static async Task<(int EtkinlikId, int[] BiletIdleri)> EtkinlikVeBiletlerOlustur(int koltukSayisi = 5)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ UctanUca {Guid.NewGuid():N}",
            Mekan = "Test Salonu, İstanbul",
            Tarih = DateTime.UtcNow.AddDays(10),
            Aciklama = "Uçtan uca test etkinliği"
        };

        for (var i = 1; i <= koltukSayisi; i++)
        {
            etkinlik.Biletler.Add(new Bilet { KoltukNo = $"A-{i:00}", Fiyat = 500m, Durum = BiletDurumu.Satista });
        }

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();

        return (etkinlik.Id, etkinlik.Biletler.Select(b => b.Id).OrderBy(id => id).ToArray());
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
    }

    private static async Task<HttpResponseMessage> FormGonder(
        HttpClient istemci, string formSayfasi, string hedef, Dictionary<string, string> alanlar)
    {
        var html = await istemci.GetStringAsync(formSayfasi);
        alanlar["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(html);
        return await istemci.PostAsync(hedef, new FormUrlEncodedContent(alanlar));
    }

    // ---------- Salon haritası ve çoklu koltuk seçimi ----------

    [Fact]
    public async Task SalonHaritasi_KoltuklariVeSecimCubugunuGostermeli()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("harita"));

            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");

            Assert.Contains("seatForm", html);
            Assert.Contains("selectionBar", html);
            Assert.Contains("data-bilet-id", html);
            Assert.Contains("A-01", html);

            // Satır içi stil kalmamalı: CSP style-src 'unsafe-inline' olmadan çalışıyor.
            Assert.DoesNotContain("style=\"", html);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CokluKoltukSepeteEklenmeli_SepetToplamiDogruOlmali()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("sepet"));

            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");
            var jeton = UygulamaFabrikasi.AntiforgeryJetonu(html);

            var alanlar = new List<KeyValuePair<string, string>>
            {
                new("etkinlikId", etkinlikId.ToString()),
                new("__RequestVerificationToken", jeton)
            };
            foreach (var id in biletIdleri.Take(3))
            {
                alanlar.Add(new KeyValuePair<string, string>("biletIds", id.ToString()));
            }

            var cevap = await istemci.PostAsync("/Biletler/SepeteEkle", new FormUrlEncodedContent(alanlar));

            Assert.Equal(HttpStatusCode.Found, cevap.StatusCode);
            Assert.Equal("/Biletler/Sepetim", cevap.Headers.Location?.OriginalString);

            var sepet = await istemci.GetStringAsync("/Biletler/Sepetim");
            Assert.Contains("3 koltuk", sepet);
            Assert.Contains("1.500", sepet); // 3 × 500 ₺
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task KoltuklardanBiriBaskasindaysa_HicbiriSepeteGirmemeli()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            // Bir koltuğu başka kullanıcı kapsın.
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Biletler SET Durum = N'Sepette', RezerveEdenKullaniciId = 'baskasi',
                        KilitBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE())
                    WHERE Id = {biletIdleri[1]}
                    """);
            }

            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("catisma"));

            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");
            var alanlar = new List<KeyValuePair<string, string>>
            {
                new("etkinlikId", etkinlikId.ToString()),
                new("__RequestVerificationToken", UygulamaFabrikasi.AntiforgeryJetonu(html))
            };
            foreach (var id in biletIdleri.Take(3))
            {
                alanlar.Add(new KeyValuePair<string, string>("biletIds", id.ToString()));
            }

            await istemci.PostAsync("/Biletler/SepeteEkle", new FormUrlEncodedContent(alanlar));

            // Hiçbiri alınmamalı: sepet boş kalmalı.
            var sepet = await istemci.GetStringAsync("/Biletler/Sepetim");
            Assert.Contains("Sepetinizde bekleyen bir rezervasyon yok", sepet);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task SepettenVazgecilince_KoltukTekrarSatisaCikmali()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("vazgec"));

            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");
            await istemci.PostAsync("/Biletler/SepeteEkle", new FormUrlEncodedContent(
                new List<KeyValuePair<string, string>>
                {
                    new("etkinlikId", etkinlikId.ToString()),
                    new("biletIds", biletIdleri[0].ToString()),
                    new("__RequestVerificationToken", UygulamaFabrikasi.AntiforgeryJetonu(html))
                }));

            await FormGonder(istemci, "/Biletler/Sepetim", "/Biletler/IptalEt",
                new Dictionary<string, string> { ["biletId"] = biletIdleri[0].ToString() });

            using var db = DatabaseFixture.CreateContext();
            var bilet = await db.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletIdleri[0]);
            Assert.Equal(BiletDurumu.Satista, bilet.Durum);
            Assert.Null(bilet.RezerveEdenKullaniciId);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task SinirdanFazlaKoltukSecilirse_Reddedilmeli()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur(koltukSayisi: 8);
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("limit"));

            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");
            var alanlar = new List<KeyValuePair<string, string>>
            {
                new("etkinlikId", etkinlikId.ToString()),
                new("__RequestVerificationToken", UygulamaFabrikasi.AntiforgeryJetonu(html))
            };
            foreach (var id in biletIdleri) // 8 koltuk, sınır 6
            {
                alanlar.Add(new KeyValuePair<string, string>("biletIds", id.ToString()));
            }

            await istemci.PostAsync("/Biletler/SepeteEkle", new FormUrlEncodedContent(alanlar));

            var sepet = await istemci.GetStringAsync("/Biletler/Sepetim");
            Assert.Contains("Sepetinizde bekleyen bir rezervasyon yok", sepet);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Genel giriş etkinlikleri ----------

    [Fact]
    public async Task GenelGirisEtkinligi_HaritaYerineAdetSeciciGostermeli()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur(koltukSayisi: 10);
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Etkinlikler SET BiletModeli = N'GenelGiris' WHERE Id = {etkinlikId}
                    """);
            }

            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("genel"));
            var html = await istemci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");

            Assert.Contains("Genel giri", html);          // "Genel giriş" başlığı
            Assert.Contains("name=\"adet\"", html);       // adet seçici
            Assert.DoesNotContain("seatForm", html);      // salon haritası yok
            Assert.DoesNotContain("style=\"", html);      // CSP uyumu
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task GenelGiris_IstenenAdetSepeteEklenmeli()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur(koltukSayisi: 10);
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Etkinlikler SET BiletModeli = N'GenelGiris' WHERE Id = {etkinlikId}
                    """);
            }

            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("genel-sepet"));

            await FormGonder(istemci, $"/Biletler/Index?etkinlikId={etkinlikId}",
                "/Biletler/GenelGirisSepeteEkle",
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["adet"] = "3"
                });

            var sepet = await istemci.GetStringAsync("/Biletler/Sepetim");
            Assert.Contains("3 koltuk", sepet);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task GenelGiris_KalandanFazlaIstenirse_HicbiriAyrilmamali()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur(koltukSayisi: 2);
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Etkinlikler SET BiletModeli = N'GenelGiris' WHERE Id = {etkinlikId}
                    """);
            }

            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("genel-yetersiz"));

            await FormGonder(istemci, $"/Biletler/Index?etkinlikId={etkinlikId}",
                "/Biletler/GenelGirisSepeteEkle",
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["adet"] = "5"
                });

            var sepet = await istemci.GetStringAsync("/Biletler/Sepetim");
            Assert.Contains("Sepetinizde bekleyen bir rezervasyon yok", sepet);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Favoriler ----------

    [Fact]
    public async Task Favori_EklenipCikarilabilmeli()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("favori"));

            // Başlangıçta favori listesi boş.
            var bos = await istemci.GetStringAsync("/Favori");
            Assert.Contains("Henüz favori etkinli", bos);

            await FormGonder(istemci, $"/Etkinlik/Detay?id={etkinlikId}", "/Favori/Degistir",
                new Dictionary<string, string> { ["etkinlikId"] = etkinlikId.ToString() });

            var dolu = await istemci.GetStringAsync("/Favori");
            Assert.DoesNotContain("Henüz favori etkinli", dolu);
            Assert.Contains("ZZ UctanUca", dolu);

            // Aynı düğmeye tekrar basmak çıkarmalı.
            await FormGonder(istemci, $"/Etkinlik/Detay?id={etkinlikId}", "/Favori/Degistir",
                new Dictionary<string, string> { ["etkinlikId"] = etkinlikId.ToString() });

            var tekrarBos = await istemci.GetStringAsync("/Favori");
            Assert.Contains("Henüz favori etkinli", tekrarBos);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task Favori_BaskasininFavorisiGorunmemeli()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var birinci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("favori-sahip"));
            await FormGonder(birinci, $"/Etkinlik/Detay?id={etkinlikId}", "/Favori/Degistir",
                new Dictionary<string, string> { ["etkinlikId"] = etkinlikId.ToString() });

            var ikinci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("favori-yabanci"));
            var yabancininListesi = await ikinci.GetStringAsync("/Favori");

            Assert.Contains("Henüz favori etkinli", yabancininListesi);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Dönüş adresi istemciden geliyor; dış bir siteye yönlendirilememeli.
    [Fact]
    public async Task Favori_DisAdreseYonlendirmemeli()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("favori-yonlendirme"));

            var html = await istemci.GetStringAsync($"/Etkinlik/Detay?id={etkinlikId}");
            var cevap = await istemci.PostAsync("/Favori/Degistir", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["donusAdresi"] = "https://kotu-site.example/calindi",
                    ["__RequestVerificationToken"] = UygulamaFabrikasi.AntiforgeryJetonu(html)
                }));

            var hedef = cevap.Headers.Location?.OriginalString ?? "";
            Assert.DoesNotContain("kotu-site", hedef);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Başkasının verisine erişim ----------

    [Fact]
    public async Task BaskaKullanicininSepeti_KendiSepetindeGorunmemeli()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var birinci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("sahip"));
            var html = await birinci.GetStringAsync($"/Biletler/Index?etkinlikId={etkinlikId}");
            await birinci.PostAsync("/Biletler/SepeteEkle", new FormUrlEncodedContent(
                new List<KeyValuePair<string, string>>
                {
                    new("etkinlikId", etkinlikId.ToString()),
                    new("biletIds", biletIdleri[0].ToString()),
                    new("__RequestVerificationToken", UygulamaFabrikasi.AntiforgeryJetonu(html))
                }));

            var ikinci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("yabanci"));
            var yabancininSepeti = await ikinci.GetStringAsync("/Biletler/Sepetim");

            Assert.Contains("Sepetinizde bekleyen bir rezervasyon yok", yabancininSepeti);

            // Başkasının rezervasyonunu iptal etmeyi denemek de işe yaramamalı.
            await FormGonder(ikinci, "/Biletler/Sepetim", "/Biletler/IptalEt",
                new Dictionary<string, string> { ["biletId"] = biletIdleri[0].ToString() });

            using var db = DatabaseFixture.CreateContext();
            var bilet = await db.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletIdleri[0]);
            Assert.Equal(BiletDurumu.Sepette, bilet.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task NormalKullanici_AdminSayfalarinaErisememeli()
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("normal"));

        foreach (var yol in new[] { "/Admin", "/Admin/EtkinlikEkle", "/Giris/Dogrula?kod=x" })
        {
            var cevap = await istemci.GetAsync(yol);
            Assert.True(cevap.StatusCode == HttpStatusCode.Found || cevap.StatusCode == HttpStatusCode.Forbidden,
                $"{yol} normal kullanıcıya açık: {cevap.StatusCode}");
        }
    }

    // ---------- Yönetici akışları ----------

    [Fact]
    public async Task Yonetici_PaneliVeKapiKontrolunuAcabilmeli()
    {
        var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("yonetici"), rol: "Admin");

        var panel = await istemci.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, panel.StatusCode);

        var kapi = await istemci.GetAsync("/Giris/Dogrula?kod=gecersiz");
        Assert.Equal(HttpStatusCode.OK, kapi.StatusCode);
    }

    [Fact]
    public async Task Yonetici_SatilmisBiletiOlanEtkinligiSilememeli()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Biletler SET Durum = N'Satıldı', RezerveEdenKullaniciId = 'alici'
                    WHERE Id = {biletIdleri[0]}
                    """);
            }

            var istemci = await _fabrika.GirisYapmisIstemciAsync(BenzersizEposta("silen"), rol: "Admin");

            await FormGonder(istemci, "/Admin", "/Admin/EtkinlikSil",
                new Dictionary<string, string> { ["etkinlikId"] = etkinlikId.ToString() });

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.True(await kontrol.Etkinlikler.AnyAsync(e => e.Id == etkinlikId),
                "Satılmış bileti olan etkinlik silinmiş!");
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Değerlendirme ----------

    [Fact]
    public async Task Degerlendirme_GirisYapilmamisBiletle_Reddedilmeli()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var eposta = BenzersizEposta("yorumcu");
            var istemci = await _fabrika.GirisYapmisIstemciAsync(eposta);

            // Bileti satılmış yap ama kapıda okutma.
            using (var db = DatabaseFixture.CreateContext())
            {
                var kullaniciId = await db.Users.Where(u => u.Email == eposta).Select(u => u.Id).FirstAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Biletler SET Durum = N'Satıldı', RezerveEdenKullaniciId = {kullaniciId}, GirisYapildi = 0
                    WHERE Id = {biletIdleri[0]}
                    """);
            }

            await FormGonder(istemci, $"/Etkinlik/Detay?id={etkinlikId}", "/Etkinlik/Degerlendir",
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["puan"] = "5",
                    ["yorum"] = "Hiç gitmedim ama yazıyorum"
                });

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False(await kontrol.Degerlendirmeler.AnyAsync(d => d.EtkinlikId == etkinlikId),
                "Kapıdan geçmeyen kullanıcı değerlendirme bırakabildi!");
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task Degerlendirme_KapidanGecmisKullaniciYazabilmeli()
    {
        var (etkinlikId, biletIdleri) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var eposta = BenzersizEposta("katilimci");
            var istemci = await _fabrika.GirisYapmisIstemciAsync(eposta);

            using (var db = DatabaseFixture.CreateContext())
            {
                var kullaniciId = await db.Users.Where(u => u.Email == eposta).Select(u => u.Id).FirstAsync();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Biletler SET Durum = N'Satıldı', RezerveEdenKullaniciId = {kullaniciId},
                        GirisYapildi = 1, GirisZamani = GETUTCDATE()
                    WHERE Id = {biletIdleri[0]}
                    """);
            }

            await FormGonder(istemci, $"/Etkinlik/Detay?id={etkinlikId}", "/Etkinlik/Degerlendir",
                new Dictionary<string, string>
                {
                    ["etkinlikId"] = etkinlikId.ToString(),
                    ["puan"] = "4",
                    ["yorum"] = "Gerçekten gittim, güzeldi"
                });

            using var kontrol = DatabaseFixture.CreateContext();
            var kayit = await kontrol.Degerlendirmeler.AsNoTracking()
                .SingleAsync(d => d.EtkinlikId == etkinlikId);
            Assert.Equal(4, kayit.Puan);

            // Yorum etkinlik sayfasında görünmeli. ASP.NET, ASCII dışı karakterleri
            // varsayılan olarak HTML varlığına çevirir ("ç" → "&#xE7;") — tarayıcıda
            // doğru görünür ama metin araması tutmaz. Bu yüzden ASCII bir parça aranıyor.
            var detay = await istemci.GetStringAsync($"/Etkinlik/Detay?id={etkinlikId}");
            Assert.Contains("gittim", detay);
            Assert.Contains("mekan", detay, StringComparison.OrdinalIgnoreCase); // "… mekanında izledi" rozeti
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Kuyruk ----------

    [Fact]
    public async Task Kuyruga_IkiKezKatilinsa_TekKayitOlusmali()
    {
        var (etkinlikId, _) = await EtkinlikVeBiletlerOlustur();
        try
        {
            var eposta = BenzersizEposta("kuyruk");
            var istemci = await _fabrika.GirisYapmisIstemciAsync(eposta);

            for (var i = 0; i < 2; i++)
            {
                await FormGonder(istemci, $"/Kuyruk/Durum?etkinlikId={etkinlikId}", "/Kuyruk/Katil",
                    new Dictionary<string, string> { ["etkinlikId"] = etkinlikId.ToString() });
            }

            using var db = DatabaseFixture.CreateContext();
            var kullaniciId = await db.Users.Where(u => u.Email == eposta).Select(u => u.Id).FirstAsync();
            var adet = await db.RezervasyonKuyrugu.CountAsync(k => k.EtkinlikId == etkinlikId && k.KullaniciId == kullaniciId);

            Assert.Equal(1, adet);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Güvenlik başlıkları ----------

    [Fact]
    public async Task GuvenlikBasliklari_HerCevaptaGonderilmeli()
    {
        var istemci = _fabrika.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var cevap = await istemci.GetAsync("/Account/GirisYap");

        var csp = Assert.Single(cevap.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("script-src 'self' 'nonce-", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Contains("checkout.stripe.com", csp); // ödeme yönlendirmesi engellenmemeli

        Assert.Equal("nosniff", Assert.Single(cevap.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(cevap.Headers.GetValues("X-Frame-Options")));
    }
}

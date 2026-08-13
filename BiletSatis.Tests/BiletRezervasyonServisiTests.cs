using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiletSatis.Tests;

[Collection("Veritabanı")]
public class BiletRezervasyonServisiTests
{
    private static BiletRezervasyonServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) =>
        new(db, NullLogger<BiletRezervasyonServisi>.Instance);

    // ---------- Geçici hata yeniden denemesiyle uyum ----------
    //
    // Uygulama bağlantısında EnableRetryOnFailure açık. O ayar açıkken EF, kendi
    // açtığın işlemleri (BeginTransaction) yürütme stratejisinin içinde görmek
    // ister; dışarıda kalırsa çalışma anında şu hatayı verir:
    //
    //   "The configured execution strategy 'SqlServerRetryingExecutionStrategy'
    //    does not support user-initiated transactions."
    //
    // Derleme sırasında değil, yalnızca o kod yolu çalıştığında. Çoklu koltuk ve
    // genel giriş rezervasyonu işlem kullanan tek iki yer — yani hata tam da
    // projenin çekirdek özelliğinde patlardı.
    //
    // Aşağıdaki iki test, servisi yeniden deneme AÇIK bir bağlamla çalıştırıyor.
    // Sarmalama kaldırılırsa bu testler kırılır.

    [Fact]
    public async Task CokluKoltuk_YenidenDenemeAcikkenDeCalismali()
    {
        var (etkinlikId, biletIdleri) = await BiletlerOlustur(3);

        await using var db = DatabaseFixture.CreateContextWithRetry();
        var servis = YeniServis(db);

        var sonuc = await servis.TryAddManyToCartAsync(biletIdleri, "kullanici-yeniden-deneme");

        Assert.True(sonuc.Basarili);

        await using var kontrol = DatabaseFixture.CreateContext();
        Assert.Equal(3, await kontrol.Biletler
            .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum == BiletDurumu.Sepette));
    }

    [Fact]
    public async Task GenelGiris_YenidenDenemeAcikkenDeCalismali()
    {
        var (etkinlikId, _) = await BiletlerOlustur(5);

        await using var db = DatabaseFixture.CreateContextWithRetry();
        var servis = YeniServis(db);

        var sonuc = await servis.TryClaimAnyAsync(etkinlikId, 3, "kullanici-yeniden-deneme");

        Assert.True(sonuc.Basarili);

        await using var kontrol = DatabaseFixture.CreateContext();
        Assert.Equal(3, await kontrol.Biletler
            .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum == BiletDurumu.Sepette));
    }

    /// <summary>
    /// "Hepsi ya da hiçbiri" garantisi yeniden deneme açıkken de geçerli olmalı:
    /// geri alma (rollback) yolu da işlem içinde çalışıyor.
    /// </summary>
    [Fact]
    public async Task GenelGiris_YenidenDenemeAcikken_YetersizBilettteHicbiriAlinmamali()
    {
        var (etkinlikId, _) = await BiletlerOlustur(2);

        await using var db = DatabaseFixture.CreateContextWithRetry();
        var servis = YeniServis(db);

        var sonuc = await servis.TryClaimAnyAsync(etkinlikId, 5, "kullanici-yeniden-deneme");

        Assert.False(sonuc.Basarili);

        await using var kontrol = DatabaseFixture.CreateContext();
        Assert.Equal(0, await kontrol.Biletler
            .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum != BiletDurumu.Satista));
    }

    private static async Task<(int etkinlikId, int biletId)> BiletOlustur(decimal fiyat = 100m)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik { Ad = $"Test-{Guid.NewGuid()}", Tarih = DateTime.UtcNow.AddDays(1) };
        var bilet = new Bilet { KoltukNo = "T-01", Fiyat = fiyat, Durum = BiletDurumu.Satista };
        etkinlik.Biletler.Add(bilet);
        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return (etkinlik.Id, bilet.Id);
    }

    private static async Task<(int etkinlikId, int[] biletIdleri)> BiletlerOlustur(int adet, decimal fiyat = 100m)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik { Ad = $"Test-{Guid.NewGuid()}", Tarih = DateTime.UtcNow.AddDays(1) };

        for (var i = 1; i <= adet; i++)
        {
            etkinlik.Biletler.Add(new Bilet { KoltukNo = $"T-{i:00}", Fiyat = fiyat, Durum = BiletDurumu.Satista });
        }

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();

        return (etkinlik.Id, etkinlik.Biletler.Select(b => b.Id).OrderBy(id => id).ToArray());
    }

    private static async Task<BiletDurumu> DurumOku(int biletId)
    {
        using var db = DatabaseFixture.CreateContext();
        var bilet = await db.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletId);
        return bilet.Durum;
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = await db.Etkinlikler.FindAsync(etkinlikId);
        if (etkinlik != null)
        {
            db.Etkinlikler.Remove(etkinlik);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task TryAddToCartAsync_SatistakiBileti_BasariylaSepeteEklemeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var sonuc = await servis.TryAddToCartAsync(biletId, "kullanici-1");

            Assert.Equal(SepeteEklemeSonucu.Basarili, sonuc);

            var bilet = await db.Biletler.FindAsync(biletId);
            Assert.Equal(BiletDurumu.Sepette, bilet!.Durum);
            Assert.Equal("kullanici-1", bilet.RezerveEdenKullaniciId);
            Assert.NotNull(bilet.KilitBitisZamani);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task TryAddToCartAsync_SepettekiBileti_ZatenAlinmisDondurmeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            await servis.TryAddToCartAsync(biletId, "kullanici-1");
            var ikinciSonuc = await servis.TryAddToCartAsync(biletId, "kullanici-2");

            Assert.Equal(SepeteEklemeSonucu.ZatenAlinmis, ikinciSonuc);

            var bilet = await db.Biletler.FindAsync(biletId);
            Assert.Equal("kullanici-1", bilet!.RezerveEdenKullaniciId);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Projenin çekirdek iddiası: aynı bilete aynı anda saldıran onlarca istekten
    // sadece biri başarılı olmalı. Her "kullanıcı" kendi DbContext'iyle (ayrı bağlantı)
    // gerçek SQL Server'a karşı atomik UPDATE gönderir.
    [Fact]
    public async Task TryAddToCartAsync_ElliEsZamanliIstek_SadeceBiriBasariliOlmali()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            var gorevler = Enumerable.Range(0, 50).Select(async i =>
            {
                using var db = DatabaseFixture.CreateContext();
                var servis = YeniServis(db);
                return await servis.TryAddToCartAsync(biletId, $"kullanici-{i}");
            });

            var sonuclar = await Task.WhenAll(gorevler);

            Assert.Equal(1, sonuclar.Count(s => s == SepeteEklemeSonucu.Basarili));
            Assert.Equal(49, sonuclar.Count(s => s == SepeteEklemeSonucu.ZatenAlinmis));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task TryAddManyToCartAsync_TumKoltuklarMusaitse_HepsiniKilitlemeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(4);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var sonuc = await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            Assert.True(sonuc.Basarili);
            Assert.Empty(sonuc.AlinamayanKoltuklar);

            foreach (var id in idler)
            {
                Assert.Equal(BiletDurumu.Sepette, await DurumOku(id));
            }
        }
        finally { await Temizle(etkinlikId); }
    }

    // Özelliğin can alıcı noktası: dört koltuktan biri araya girilirse diğer üçü de
    // sepete girmemeli. Aksi halde kullanıcı yan yana oturmak isterken dağınık
    // koltuklarla kalırdı.
    [Fact]
    public async Task TryAddManyToCartAsync_KoltuklardanBiriAlinmissa_HicbiriniKilitlememeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(4);
        try
        {
            using (var baskaKullanici = DatabaseFixture.CreateContext())
            {
                await YeniServis(baskaKullanici).TryAddToCartAsync(idler[2], "kullanici-2");
            }

            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).TryAddManyToCartAsync(idler, "kullanici-1");

            Assert.False(sonuc.Basarili);
            Assert.Equal(new[] { "T-03" }, sonuc.AlinamayanKoltuklar);

            // Geri alma çalıştı mı: araya girilen koltuk dışındakiler hâlâ satışta olmalı.
            Assert.Equal(BiletDurumu.Satista, await DurumOku(idler[0]));
            Assert.Equal(BiletDurumu.Satista, await DurumOku(idler[1]));
            Assert.Equal(BiletDurumu.Satista, await DurumOku(idler[3]));

            using var kontrol = DatabaseFixture.CreateContext();
            var araya = await kontrol.Biletler.AsNoTracking().FirstAsync(b => b.Id == idler[2]);
            Assert.Equal("kullanici-2", araya.RezerveEdenKullaniciId);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Tek koltuktaki yarış durumunun çoklu hâli: 20 kullanıcı aynı üç koltuğu aynı anda
    // isterse yalnızca biri tamamını almalı, kalanlar hiçbir koltuk almamalı.
    [Fact]
    public async Task TryAddManyToCartAsync_YirmiEsZamanliIstek_SadeceBiriTumKoltuklariAlmali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(3);
        try
        {
            var kapi = new TaskCompletionSource();

            var gorevler = Enumerable.Range(0, 20).Select(async i =>
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).TryAddManyToCartAsync(idler, $"kullanici-{i}");
            }).ToList();

            await Task.Delay(250);
            kapi.SetResult();

            var sonuclar = await Task.WhenAll(gorevler);

            Assert.Equal(1, sonuclar.Count(s => s.Basarili));

            // Üç koltuğun da aynı kişide olduğunu doğrula — kimse yarım sepet almamış olmalı.
            using var kontrol = DatabaseFixture.CreateContext();
            var biletler = await kontrol.Biletler.AsNoTracking().Where(b => idler.Contains(b.Id)).ToListAsync();

            Assert.All(biletler, b => Assert.Equal(BiletDurumu.Sepette, b.Durum));
            Assert.Single(biletler.Select(b => b.RezerveEdenKullaniciId).Distinct());
        }
        finally { await Temizle(etkinlikId); }
    }

    // Kesişen kümeler: iki kullanıcı ortak bir koltuk isterse ikisi birden başarılı olamaz.
    [Fact]
    public async Task TryAddManyToCartAsync_KesisenKoltukKumeleri_SadeceBiriBasariliOlmali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(5);
        try
        {
            // İki isteğin gerçekten aynı anda çarpışması için ortak kapı kullanılıyor;
            // aksi halde biri diğerinden önce bitip yarış hiç oluşmuyor.
            var kapi = new TaskCompletionSource();

            async Task<CokluSepeteEklemeSonucu> Dene(int[] istenen, string kullanici)
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).TryAddManyToCartAsync(istenen, kullanici);
            }

            var birinci = Dene(new[] { idler[0], idler[1], idler[2] }, "kullanici-1");
            var ikinci = Dene(new[] { idler[2], idler[3], idler[4] }, "kullanici-2");

            await Task.Delay(250);
            kapi.SetResult();

            var sonuclar = await Task.WhenAll(birinci, ikinci);

            Assert.Equal(1, sonuclar.Count(s => s.Basarili));

            // Kaybeden tarafın koltukları serbest kalmış olmalı: ortak koltuk hariç
            // sepette olan koltuk sayısı tam olarak 3 (kazananın kümesi) olmalı.
            using var kontrol = DatabaseFixture.CreateContext();
            var sepetteki = await kontrol.Biletler.AsNoTracking()
                .CountAsync(b => idler.Contains(b.Id) && b.Durum == BiletDurumu.Sepette);

            Assert.Equal(3, sepetteki);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CompletePaymentManyAsync_SepettekiTumBiletleri_SatildiYapmali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(3);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            var tamamlanan = (await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_test")).SahipOlunan;

            Assert.Equal(3, tamamlanan);
            foreach (var id in idler)
            {
                Assert.Equal(BiletDurumu.Satildi, await DurumOku(id));
            }
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CompletePaymentManyAsync_BaskasininBiletiVarsa_SadeceKendiBiletleriniSaymali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(3);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(new[] { idler[0], idler[1] }, "kullanici-1");
            await servis.TryAddToCartAsync(idler[2], "kullanici-2");

            var tamamlanan = (await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_test")).SahipOlunan;

            Assert.Equal(2, tamamlanan);
            Assert.Equal(BiletDurumu.Sepette, await DurumOku(idler[2]));
        }
        finally { await Temizle(etkinlikId); }
    }

    // Kullanıcı ödeme başarı sayfasını yenilerse işlem ikinci kez çalışır. İkinci
    // çağrı hiçbir satırı güncellemez ama kullanıcı biletlerine sahip olduğu için
    // yine tam sayı dönmeli — aksi halde ekranda sahte "biletiniz kayboldu" uyarısı çıkar.
    [Fact]
    public async Task CompletePaymentManyAsync_IkinciKezCagrilirsa_YineTamSayiDonmeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(3);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            var ilk = (await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_test")).SahipOlunan;
            var ikinci = (await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_test")).SahipOlunan;

            Assert.Equal(3, ilk);
            Assert.Equal(3, ikinci);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Kullanıcı Stripe sayfasındayken kilit süresi dolup koltuk serbest bırakılmış
    // olabilir. Koltuğu bu arada kimse almadıysa parası ödenmiş bilet geri kazanılmalı.
    [Fact]
    public async Task CompletePaymentManyAsync_KilidiDusmusAmaBostaKalanKoltugu_GeriKazanmali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            // CartExpiryWorker'ın süresi dolan kilidi serbest bırakmasını taklit et.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE Biletler SET Durum = N'Satışta', RezerveEdenKullaniciId = NULL, KilitBitisZamani = NULL
                WHERE Id = {idler[1]}
                """);

            var tamamlanan = (await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_test")).SahipOlunan;

            Assert.Equal(2, tamamlanan);
            Assert.Equal(BiletDurumu.Satildi, await DurumOku(idler[1]));
        }
        finally { await Temizle(etkinlikId); }
    }

    // Kurtarma yalnızca koltuk gerçekten boştaysa çalışmalı; başkasının sepetindeki
    // koltuk asla geri alınmamalı.
    [Fact]
    public async Task CompletePaymentManyAsync_KoltugaBaskasiGectiyse_GeriAlmamali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE Biletler SET Durum = N'Satışta', RezerveEdenKullaniciId = NULL, KilitBitisZamani = NULL
                WHERE Id = {idler[1]}
                """);

            // Serbest kalan koltuğu başka bir kullanıcı sepetine aldı.
            await servis.TryAddToCartAsync(idler[1], "kullanici-2");

            var tamamlanan = (await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_test")).SahipOlunan;

            Assert.Equal(1, tamamlanan);

            using var kontrol = DatabaseFixture.CreateContext();
            var kapilan = await kontrol.Biletler.AsNoTracking().FirstAsync(b => b.Id == idler[1]);
            Assert.Equal(BiletDurumu.Sepette, kapilan.Durum);
            Assert.Equal("kullanici-2", kapilan.RezerveEdenKullaniciId);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Kullanıcı iki sekmede ödemeye geçip ikisini de tamamlarsa aynı koltuğun parası
    // iki kez alınır. Bunu engelleyemiyoruz (iade akışı yok) ama sessizce geçmemeli:
    // ikinci ödeme farklı bir oturuma ait olduğu için tespit edilip raporlanmalı.
    [Fact]
    public async Task CompletePaymentManyAsync_FarkliOdemeOturumuyla_CifteOdemeyiBildirmeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            var ilk = await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_birinci");
            Assert.Empty(ilk.CifteOdenenKoltuklar);

            // İkinci sekmede açılmış olan diğer ödeme oturumu da tamamlandı.
            var ikinci = await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_ikinci");

            Assert.Equal(2, ikinci.SahipOlunan);
            Assert.Equal(new[] { "T-01", "T-02" }, ikinci.CifteOdenenKoltuklar);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Aynı oturumun tekrar çalışması (kullanıcı başarı sayfasını yeniledi) çifte
    // ödeme sayılmamalı — yoksa her yenilemede yanlış uyarı verirdik.
    [Fact]
    public async Task CompletePaymentManyAsync_AyniOdemeOturumuTekrarlanirsa_CifteOdemeSaymamali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_ayni");
            var tekrar = await servis.CompletePaymentManyAsync(idler, "kullanici-1", "sess_ayni");

            Assert.Equal(2, tekrar.SahipOlunan);
            Assert.Empty(tekrar.CifteOdenenKoltuklar);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task ExtendCartHoldsAsync_KendiBiletlerininSuresiniUzatmali()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            var oncekiBitis = (await db.Biletler.AsNoTracking().FirstAsync(b => b.Id == idler[0])).KilitBitisZamani;

            var uzatilan = await servis.ExtendCartHoldsAsync(idler, "kullanici-1", 15);

            Assert.Equal(2, uzatilan);

            var sonrakiBitis = (await db.Biletler.AsNoTracking().FirstAsync(b => b.Id == idler[0])).KilitBitisZamani;
            Assert.True(sonrakiBitis > oncekiBitis);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task ExtendCartHoldsAsync_BaskaKullaniciDenerse_UzatmamaLi()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddManyToCartAsync(idler, "kullanici-1");

            var uzatilan = await servis.ExtendCartHoldsAsync(idler, "kullanici-2", 15);

            Assert.Equal(0, uzatilan);
        }
        finally { await Temizle(etkinlikId); }
    }

    // ---------- Genel giriş: "hangisi olursa olsun N tane" ----------

    [Fact]
    public async Task TryClaimAnyAsync_YeterliBiletVarsa_IstenenKadariniVermeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(10);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).TryClaimAnyAsync(etkinlikId, 4, "kullanici-1");

            Assert.True(sonuc.Basarili);

            using var kontrol = DatabaseFixture.CreateContext();
            var alinan = await kontrol.Biletler.AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId
                              && b.Durum == BiletDurumu.Sepette
                              && b.RezerveEdenKullaniciId == "kullanici-1");

            Assert.Equal(4, alinan);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Kısmi başarı burada da kabul edilemez: 5 bilet isteyip 3'üyle kalmak yerine
    // hiçbiri alınmamalı.
    [Fact]
    public async Task TryClaimAnyAsync_YeterliBiletYoksa_HicbiriniVermemeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(3);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).TryClaimAnyAsync(etkinlikId, 5, "kullanici-1");

            Assert.False(sonuc.Basarili);

            using var kontrol = DatabaseFixture.CreateContext();
            var sepetteki = await kontrol.Biletler.AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum == BiletDurumu.Sepette);

            Assert.Equal(0, sepetteki);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Genel girişin yarış durumu: 10 biletlik etkinlikte 10 kullanıcı aynı anda
    // 3'er bilet isterse, en fazla 3 kişi alabilmeli (3×3=9) ve toplam satılan
    // asla kapasiteyi aşmamalı. Aşması "overselling" demektir.
    [Fact]
    public async Task TryClaimAnyAsync_EsZamanliTalepler_KapasiteyiAsmamali()
    {
        var (etkinlikId, _) = await BiletlerOlustur(10);
        try
        {
            var kapi = new TaskCompletionSource();

            var gorevler = Enumerable.Range(0, 10).Select(async i =>
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).TryClaimAnyAsync(etkinlikId, 3, $"kullanici-{i}");
            }).ToList();

            await Task.Delay(250);
            kapi.SetResult();

            var sonuclar = await Task.WhenAll(gorevler);
            var basariliSayisi = sonuclar.Count(s => s.Basarili);

            using var kontrol = DatabaseFixture.CreateContext();
            var sepetteki = await kontrol.Biletler.AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId && b.Durum == BiletDurumu.Sepette);

            // Her başarılı istek tam 3 bilet almış olmalı — ne eksik ne fazla.
            Assert.Equal(basariliSayisi * 3, sepetteki);
            Assert.True(sepetteki <= 10, $"10 biletlik etkinlikte {sepetteki} bilet dağıtılmış (overselling)");
            Assert.True(basariliSayisi is >= 1 and <= 3, $"Beklenmeyen başarılı sayısı: {basariliSayisi}");
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task TryClaimAnyAsync_BaskasininSepetindekiBiletiVermemeli()
    {
        var (etkinlikId, idler) = await BiletlerOlustur(4);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            // 4 biletin 2'si başkasının sepetinde; kalan 2 ile 3 bilet verilemez.
            await servis.TryAddManyToCartAsync(new[] { idler[0], idler[1] }, "baskasi");

            var sonuc = await servis.TryClaimAnyAsync(etkinlikId, 3, "kullanici-1");

            Assert.False(sonuc.Basarili);

            using var kontrol = DatabaseFixture.CreateContext();
            var baskasininki = await kontrol.Biletler.AsNoTracking()
                .CountAsync(b => b.EtkinlikId == etkinlikId && b.RezerveEdenKullaniciId == "baskasi");

            Assert.Equal(2, baskasininki);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task ReleaseExpiredCartHoldsAsync_SuresiDolanBileti_SatistaGeriDondurmeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE Biletler SET Durum = N'Sepette', RezerveEdenKullaniciId = 'kullanici-1',
                        KilitBitisZamani = DATEADD(MINUTE, -1, GETUTCDATE())
                    WHERE Id = {biletId}
                    """);
            }

            using var db2 = DatabaseFixture.CreateContext();
            var servis = YeniServis(db2);

            var serbestKalan = await servis.ReleaseExpiredCartHoldsAsync();

            Assert.True(serbestKalan >= 1);
            var bilet = await db2.Biletler.FindAsync(biletId);
            Assert.Equal(BiletDurumu.Satista, bilet!.Durum);
            Assert.Null(bilet.RezerveEdenKullaniciId);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CompletePaymentAsync_DogruSahibiIcin_BasariliOlmali()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddToCartAsync(biletId, "kullanici-1");

            var basarili = await servis.CompletePaymentAsync(biletId, "kullanici-1");

            Assert.True(basarili);
            var bilet = await db.Biletler.FindAsync(biletId);
            Assert.Equal(BiletDurumu.Satildi, bilet!.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CompletePaymentAsync_BaskaKullaniciDenerse_BasarisizOlmali()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddToCartAsync(biletId, "kullanici-1");

            var basarili = await servis.CompletePaymentAsync(biletId, "kullanici-2");

            Assert.False(basarili);
            var bilet = await db.Biletler.FindAsync(biletId);
            Assert.Equal(BiletDurumu.Sepette, bilet!.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CancelReservationAsync_SepettekiBileti_SatistaGeriDondurmeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);
            await servis.TryAddToCartAsync(biletId, "kullanici-1");

            var basarili = await servis.CancelReservationAsync(biletId, "kullanici-1");

            Assert.True(basarili);
            var bilet = await db.Biletler.FindAsync(biletId);
            Assert.Equal(BiletDurumu.Satista, bilet!.Durum);
            Assert.Null(bilet.RezerveEdenKullaniciId);
        }
        finally { await Temizle(etkinlikId); }
    }
}

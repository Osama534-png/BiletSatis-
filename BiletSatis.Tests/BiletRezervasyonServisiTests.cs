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
            var gorevler = Enumerable.Range(0, 20).Select(async i =>
            {
                using var db = DatabaseFixture.CreateContext();
                return await YeniServis(db).TryAddManyToCartAsync(idler, $"kullanici-{i}");
            });

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
            var birinci = Task.Run(async () =>
            {
                using var db = DatabaseFixture.CreateContext();
                return await YeniServis(db).TryAddManyToCartAsync(new[] { idler[0], idler[1], idler[2] }, "kullanici-1");
            });

            var ikinci = Task.Run(async () =>
            {
                using var db = DatabaseFixture.CreateContext();
                return await YeniServis(db).TryAddManyToCartAsync(new[] { idler[2], idler[3], idler[4] }, "kullanici-2");
            });

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

            var tamamlanan = await servis.CompletePaymentManyAsync(idler, "kullanici-1");

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

            var tamamlanan = await servis.CompletePaymentManyAsync(idler, "kullanici-1");

            Assert.Equal(2, tamamlanan);
            Assert.Equal(BiletDurumu.Sepette, await DurumOku(idler[2]));
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

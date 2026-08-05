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

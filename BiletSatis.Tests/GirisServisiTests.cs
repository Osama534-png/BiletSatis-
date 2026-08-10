using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Giris;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BiletSatis.Tests;

// Kapı kontrolü: bir bilet yalnızca bir kez giriş sağlamalı ve iki görevli
// aynı anda okutsa bile tek giriş kaydedilmeli.
[Collection("Veritabanı")]
public class GirisServisiTests
{
    private static readonly IBiletKoduServisi Kodlayici =
        new BiletKoduServisi(Options.Create(new GirisAyarlari { ImzaAnahtari = "test-imza-anahtari" }));

    private static GirisServisi YeniServis(BiletSatisDbContext db) =>
        new(db, Kodlayici, NullLogger<GirisServisi>.Instance);

    private static async Task<(int EtkinlikId, int BiletId)> BiletOlustur(BiletDurumu durum)
    {
        using var db = DatabaseFixture.CreateContext();

        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Giris Testi {Guid.NewGuid():N}",
            Mekan = "Test Salonu, Ankara",
            Tarih = DateTime.UtcNow.AddDays(5),
            YasSiniri = 18
        };
        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();

        var bilet = new Bilet
        {
            EtkinlikId = etkinlik.Id,
            KoltukNo = "C-12",
            Fiyat = 900m,
            Durum = durum,
            BildirimGonderildi = true
        };
        db.Biletler.Add(bilet);
        await db.SaveChangesAsync();

        return (etkinlik.Id, bilet.Id);
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Biletler WHERE EtkinlikId = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
    }

    [Fact]
    public async Task SatilmisBiletinIlkOkutmasi_GirisiOnaylamali()
    {
        var (etkinlikId, biletId) = await BiletOlustur(BiletDurumu.Satildi);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var kod = Kodlayici.KodUret(biletId, 1);

            var sonuc = await YeniServis(db).GirisiOnaylaAsync(kod);

            Assert.Equal(GirisDurumu.GirisOnaylandi, sonuc.Durum);
            Assert.Equal("C-12", sonuc.KoltukNo);
            Assert.Equal(18, sonuc.YasSiniri);

            using var kontrol = DatabaseFixture.CreateContext();
            var bilet = await kontrol.Biletler.FirstAsync(b => b.Id == biletId);
            Assert.True(bilet.GirisYapildi);
            Assert.NotNull(bilet.GirisZamani);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task IkinciOkutma_ZatenKullanildiDonmeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur(BiletDurumu.Satildi);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var kod = Kodlayici.KodUret(biletId, 1);
            var servis = YeniServis(db);

            await servis.GirisiOnaylaAsync(kod);
            var ikinci = await servis.GirisiOnaylaAsync(kod);

            Assert.Equal(GirisDurumu.ZatenKullanildi, ikinci.Durum);
            Assert.NotNull(ikinci.GirisZamani);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Bilet satın alma mantığındaki yarış durumu koruması burada da geçerli olmalı:
    // 20 görevli aynı anda okutsa bile yalnızca biri girişi onaylayabilmeli.
    [Fact]
    public async Task YirmiEsZamanliOkutma_SadeceBiriOnaylanmali()
    {
        var (etkinlikId, biletId) = await BiletOlustur(BiletDurumu.Satildi);
        try
        {
            var kod = Kodlayici.KodUret(biletId, 1);

            var gorevler = Enumerable.Range(0, 20).Select(async _ =>
            {
                using var db = DatabaseFixture.CreateContext();
                return await YeniServis(db).GirisiOnaylaAsync(kod);
            });

            var sonuclar = await Task.WhenAll(gorevler);

            Assert.Equal(1, sonuclar.Count(s => s.Durum == GirisDurumu.GirisOnaylandi));
            Assert.Equal(19, sonuclar.Count(s => s.Durum == GirisDurumu.ZatenKullanildi));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task SatilmamisBilet_GirisVermemeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur(BiletDurumu.Satista);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var kod = Kodlayici.KodUret(biletId, 1);

            var sonuc = await YeniServis(db).GirisiOnaylaAsync(kod);

            Assert.Equal(GirisDurumu.SatilmamisBilet, sonuc.Durum);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False((await kontrol.Biletler.FirstAsync(b => b.Id == biletId)).GirisYapildi);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Sahte imzayla gelen istek veritabanına hiç ulaşmamalı.
    [Fact]
    public async Task SahteImza_GecersizKodDonmeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur(BiletDurumu.Satildi);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var sonuc = await YeniServis(db).GirisiOnaylaAsync($"{biletId}.sahteimza");

            Assert.Equal(GirisDurumu.GecersizKod, sonuc.Durum);
            Assert.False(sonuc.BiletBulundu);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False((await kontrol.Biletler.FirstAsync(b => b.Id == biletId)).GirisYapildi);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Sorgulama sayfası bileti değiştirmemeli; görevli önce bakıp sonra onaylıyor.
    [Fact]
    public async Task DurumSorgulama_BiletiDegistirmemeli()
    {
        var (etkinlikId, biletId) = await BiletOlustur(BiletDurumu.Satildi);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var kod = Kodlayici.KodUret(biletId, 1);

            var sonuc = await YeniServis(db).DurumSorgulaAsync(kod);

            Assert.Equal(GirisDurumu.GirisOnaylandi, sonuc.Durum);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False((await kontrol.Biletler.FirstAsync(b => b.Id == biletId)).GirisYapildi);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task OlmayanBilet_GecersizKodDonmeli()
    {
        using var db = DatabaseFixture.CreateContext();

        var sonuc = await YeniServis(db).GirisiOnaylaAsync(Kodlayici.KodUret(999_999, 1));

        Assert.Equal(GirisDurumu.GecersizKod, sonuc.Durum);
    }
}

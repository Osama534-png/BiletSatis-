using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Favoriler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiletSatis.Tests;

[Collection("Veritabanı")]
public class FavoriServisiTests
{
    private static FavoriServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) =>
        new(db, NullLogger<FavoriServisi>.Instance);

    private static async Task<int> EtkinlikOlustur()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Favori {Guid.NewGuid():N}",
            Mekan = "Test Salonu, Ankara",
            Tarih = DateTime.UtcNow.AddDays(30)
        };
        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
    }

    private static async Task<int> FavoriSayisi(int etkinlikId, string kullaniciId)
    {
        using var db = DatabaseFixture.CreateContext();
        return await db.Favoriler.AsNoTracking()
            .CountAsync(f => f.EtkinlikId == etkinlikId && f.KullaniciId == kullaniciId);
    }

    [Fact]
    public async Task IlkBasista_Eklenmeli_IkincidE_Cikarilmali()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var ilk = await servis.DegistirAsync(etkinlikId, "kullanici-1");
            Assert.Equal(FavoriDurumu.Eklendi, ilk);
            Assert.Equal(1, await FavoriSayisi(etkinlikId, "kullanici-1"));

            var ikinci = await servis.DegistirAsync(etkinlikId, "kullanici-1");
            Assert.Equal(FavoriDurumu.Cikarildi, ikinci);
            Assert.Equal(0, await FavoriSayisi(etkinlikId, "kullanici-1"));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task FarkliKullanicilar_AyniEtkinligiFavoriyeAlabilmeli()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            await servis.DegistirAsync(etkinlikId, "kullanici-1");
            await servis.DegistirAsync(etkinlikId, "kullanici-2");

            Assert.Equal(1, await FavoriSayisi(etkinlikId, "kullanici-1"));
            Assert.Equal(1, await FavoriSayisi(etkinlikId, "kullanici-2"));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task FavoriIdleri_YalnizcaKendiFavorileriniDonmeli()
    {
        var birinciEtkinlik = await EtkinlikOlustur();
        var ikinciEtkinlik = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            await servis.DegistirAsync(birinciEtkinlik, "kullanici-1");
            await servis.DegistirAsync(ikinciEtkinlik, "kullanici-2");

            var idler = await servis.FavoriIdleriAsync("kullanici-1");

            Assert.Contains(birinciEtkinlik, idler);
            Assert.DoesNotContain(ikinciEtkinlik, idler);
        }
        finally
        {
            await Temizle(birinciEtkinlik);
            await Temizle(ikinciEtkinlik);
        }
    }

    [Fact]
    public async Task EtkinlikSilinince_FavoriKaydiDaSilinmeli()
    {
        var etkinlikId = await EtkinlikOlustur();

        using (var db = DatabaseFixture.CreateContext())
        {
            await YeniServis(db).DegistirAsync(etkinlikId, "kullanici-1");
        }

        Assert.Equal(1, await FavoriSayisi(etkinlikId, "kullanici-1"));

        await Temizle(etkinlikId);

        // Foreign key cascade: etkinlik gidince favori kaydı öksüz kalmamalı.
        Assert.Equal(0, await FavoriSayisi(etkinlikId, "kullanici-1"));
    }

    // Kalbe hızlıca iki kez basmak ya da iki sekmeden aynı anda istek göndermek
    // mükerrer kayıt oluşturmamalı; bileşik birincil anahtar bunu engelliyor.
    [Fact]
    public async Task EsZamanliIstekler_MukerrerKayitOlusturmamali()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            var kapi = new TaskCompletionSource();

            var gorevler = Enumerable.Range(0, 6).Select(async _ =>
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).DegistirAsync(etkinlikId, "kullanici-1");
            }).ToList();

            await Task.Delay(250);
            kapi.SetResult();

            await Task.WhenAll(gorevler);

            // Sonuç ekli ya da ekli değil olabilir (çift sayıda basış), ama kayıt
            // sayısı asla 1'i aşmamalı.
            var sayi = await FavoriSayisi(etkinlikId, "kullanici-1");
            Assert.InRange(sayi, 0, 1);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task FavorideMi_DogruCevapVermeli()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            Assert.False(await servis.FavorideMiAsync(etkinlikId, "kullanici-1"));

            await servis.DegistirAsync(etkinlikId, "kullanici-1");

            Assert.True(await servis.FavorideMiAsync(etkinlikId, "kullanici-1"));
            Assert.False(await servis.FavorideMiAsync(etkinlikId, "kullanici-2"));
        }
        finally { await Temizle(etkinlikId); }
    }
}

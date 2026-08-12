using BiletSatis.Web.Controllers;
using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiletSatis.Tests;

// Satılmış bilet gerçek bir satın alma kaydıdır; etkinlik silinince yok olurdu.
// Bu koruma sunucu tarafında olduğu için burada doğrudan action üzerinden test edilir.
[Collection("Veritabanı")]
public class AdminEtkinlikSilmeTests
{
    private static AdminController YeniController(BiletSatisDbContext db) =>
        new(db,
            new KuyrukServisi(db, NullLogger<KuyrukServisi>.Instance),
            new SahteOrtam(),
            NullLogger<AdminController>.Instance)
        {
            TempData = new TempDataDictionary(new DefaultHttpContext(), new SahteTempDataSaglayici())
        };

    private static Task<int> EtkinlikOlustur(BiletSatisDbContext db, params Bilet[] biletler) =>
        EtkinlikOlustur(db, DateTime.Now.AddDays(30), biletler);

    private static async Task<int> EtkinlikOlustur(BiletSatisDbContext db, DateTime tarih, params Bilet[] biletler)
    {
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Test {Guid.NewGuid():N}",
            Mekan = "Test Salonu, Test",
            Tarih = tarih
        };
        etkinlik.Biletler.AddRange(biletler);

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RezervasyonKuyrugu WHERE EtkinlikId = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Biletler WHERE EtkinlikId = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
    }

    // Sona ermiş etkinlik, satılmış bileti olsa bile silinebilir: biletler artık
    // kullanılamaz, arşiv temizliği yöneticinin kararıdır. Gelecek etkinlikte aynı
    // durum silmeyi engelliyor (bir alttaki test) — ayrım tarihte.
    [Fact]
    public async Task EtkinlikSil_SonaErmisEtkinlikSatilmisBiletleOlsaBileSilinmeli()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlikId = await EtkinlikOlustur(db, DateTime.Now.AddDays(-2),
            new Bilet { KoltukNo = "A-01", Fiyat = 100m, Durum = BiletDurumu.Satildi },
            new Bilet { KoltukNo = "A-02", Fiyat = 100m, Durum = BiletDurumu.Satista });

        try
        {
            var controller = YeniController(db);

            await controller.EtkinlikSil(etkinlikId);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False(await kontrol.Etkinlikler.AnyAsync(e => e.Id == etkinlikId));
            Assert.Equal(0, await kontrol.Biletler.CountAsync(b => b.EtkinlikId == etkinlikId));
            Assert.NotNull(controller.TempData["Bilgi"]);
            Assert.Null(controller.TempData["Hata"]);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task EtkinlikSil_SatilmisBiletVarsaSilmemeli()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlikId = await EtkinlikOlustur(db,
            new Bilet { KoltukNo = "A-01", Fiyat = 100m, Durum = BiletDurumu.Satildi },
            new Bilet { KoltukNo = "A-02", Fiyat = 100m, Durum = BiletDurumu.Satista });

        try
        {
            var controller = YeniController(db);

            await controller.EtkinlikSil(etkinlikId);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.True(await kontrol.Etkinlikler.AnyAsync(e => e.Id == etkinlikId));
            Assert.Equal(2, await kontrol.Biletler.CountAsync(b => b.EtkinlikId == etkinlikId));
            Assert.NotNull(controller.TempData["Hata"]);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task EtkinlikSil_SatilmisBiletYoksaBiletleriyleBirlikteSilmeli()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlikId = await EtkinlikOlustur(db,
            new Bilet { KoltukNo = "A-01", Fiyat = 100m, Durum = BiletDurumu.Satista },
            new Bilet { KoltukNo = "A-02", Fiyat = 100m, Durum = BiletDurumu.Sepette });

        try
        {
            var controller = YeniController(db);

            await controller.EtkinlikSil(etkinlikId);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False(await kontrol.Etkinlikler.AnyAsync(e => e.Id == etkinlikId));
            Assert.Equal(0, await kontrol.Biletler.CountAsync(b => b.EtkinlikId == etkinlikId));
        }
        finally { await Temizle(etkinlikId); }
    }

    // RezervasyonKuyrugu'nun Etkinlik'e foreign key'i yok; cascade ile silinmez,
    // elle temizlenmezse öksüz satır kalır.
    [Fact]
    public async Task EtkinlikSil_KuyrukKayitlariniDaTemizlemeli()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlikId = await EtkinlikOlustur(db,
            new Bilet { KoltukNo = "A-01", Fiyat = 100m, Durum = BiletDurumu.Satista });

        try
        {
            var kuyruk = new KuyrukServisi(db, NullLogger<KuyrukServisi>.Instance);
            await kuyruk.EnqueueWaitlistAsync(etkinlikId, "test-kullanici-1");
            await kuyruk.EnqueueWaitlistAsync(etkinlikId, "test-kullanici-2");

            using (var oncesi = DatabaseFixture.CreateContext())
            {
                Assert.Equal(2, await oncesi.RezervasyonKuyrugu.CountAsync(k => k.EtkinlikId == etkinlikId));
            }

            await YeniController(db).EtkinlikSil(etkinlikId);

            using var kontrol = DatabaseFixture.CreateContext();
            Assert.False(await kontrol.Etkinlikler.AnyAsync(e => e.Id == etkinlikId));
            Assert.Equal(0, await kontrol.RezervasyonKuyrugu.CountAsync(k => k.EtkinlikId == etkinlikId));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task EtkinlikSil_OlmayanEtkinlikIcinNotFoundDonmeli()
    {
        using var db = DatabaseFixture.CreateContext();

        var sonuc = await YeniController(db).EtkinlikSil(999_999);

        Assert.IsType<NotFoundResult>(sonuc);
    }

    // AdminController yapıcısı istiyor ama silme akışında kullanılmıyor.
    private sealed class SahteOrtam : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "BiletSatis.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string EnvironmentName { get; set; } = "Test";
    }

    private sealed class SahteTempDataSaglayici : ITempDataProvider
    {
        public IDictionary<string, object?> LoadTempData(HttpContext context) => new Dictionary<string, object?>();

        public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
    }
}

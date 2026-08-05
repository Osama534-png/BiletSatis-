using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiletSatis.Tests;

[Collection("Veritabanı")]
public class KuyrukServisiTests
{
    private static KuyrukServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) =>
        new(db, NullLogger<KuyrukServisi>.Instance);

    // RezervasyonKuyrugu.EtkinlikId'nin gerçek bir Etkinlikler satırına FK'ı yok,
    // bu yüzden testler arası çakışmayı önlemek için her testte benzersiz bir sahte Id kullanılır.
    private static int YeniEtkinlikId() => Random.Shared.Next(1_000_000, 2_000_000);

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RezervasyonKuyrugu WHERE EtkinlikId = {etkinlikId}");
    }

    [Fact]
    public async Task EnqueueWaitlistAsync_YirmiEsZamanliKatilim_BenzersizSiraNoVermeli()
    {
        var etkinlikId = YeniEtkinlikId();
        try
        {
            var gorevler = Enumerable.Range(0, 20).Select(async i =>
            {
                using var db = DatabaseFixture.CreateContext();
                var servis = YeniServis(db);
                return await servis.EnqueueWaitlistAsync(etkinlikId, $"kullanici-{i}");
            });

            var siraNolar = await Task.WhenAll(gorevler);

            Assert.Equal(20, siraNolar.Distinct().Count());
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task AllocateWaitlistBatchAsync_EnDusukSiraNolaraHakTanimali()
    {
        var etkinlikId = YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var siraNolar = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                siraNolar.Add(await servis.EnqueueWaitlistAsync(etkinlikId, $"kullanici-{i}"));
            }
            siraNolar.Sort();

            var atanan = await servis.AllocateWaitlistBatchAsync(etkinlikId, 4);

            Assert.Equal(4, atanan);

            var kayitlar = await db.RezervasyonKuyrugu
                .Where(k => k.EtkinlikId == etkinlikId)
                .ToListAsync();

            var enDusuk4 = siraNolar.Take(4).ToHashSet();
            foreach (var kayit in kayitlar)
            {
                var beklenenDurum = enDusuk4.Contains(kayit.SiraNo) ? KuyrukDurumu.HakTanindi : KuyrukDurumu.Beklemede;
                Assert.Equal(beklenenDurum, kayit.Durum);
            }
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task PromoteExpiredAndFillAsync_SuresiDolaniSiradakineDevretmeli()
    {
        var etkinlikId = YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var hakliSiraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "hakli-kullanici");
            var beklemedeSiraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "bekleyen-kullanici");

            // Hak tanınan kullanıcının süresi geçmişte dolmuş gibi işaretle
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE RezervasyonKuyrugu
                SET Durum = N'HakTanindi', HakBitisZamani = DATEADD(MINUTE, -1, GETUTCDATE())
                WHERE SiraNo = {hakliSiraNo}
                """);

            var devredilen = await servis.PromoteExpiredAndFillAsync(etkinlikId);

            Assert.Equal(1, devredilen);

            var eskiKayit = await db.RezervasyonKuyrugu.AsNoTracking().FirstAsync(k => k.SiraNo == hakliSiraNo);
            var yeniKayit = await db.RezervasyonKuyrugu.AsNoTracking().FirstAsync(k => k.SiraNo == beklemedeSiraNo);

            Assert.Equal(KuyrukDurumu.SuresiDoldu, eskiKayit.Durum);
            Assert.Equal(KuyrukDurumu.HakTanindi, yeniKayit.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CompleteQueueEntryAsync_DogruKullaniciIcin_TamamlandiYapmali()
    {
        var etkinlikId = YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var siraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "kullanici-1");
            await servis.AllocateWaitlistBatchAsync(etkinlikId, 1);

            var basarili = await servis.CompleteQueueEntryAsync(siraNo, "kullanici-1");

            Assert.True(basarili);
            var kayit = await db.RezervasyonKuyrugu.AsNoTracking().FirstAsync(k => k.SiraNo == siraNo);
            Assert.Equal(KuyrukDurumu.Tamamlandi, kayit.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }
}

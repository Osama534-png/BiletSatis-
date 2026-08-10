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

    // RezervasyonKuyrugu.EtkinlikId artık Etkinlikler tablosuna foreign key ile bağlı,
    // bu yüzden testler gerçek bir etkinlik satırı oluşturur. Kuyruk kayıtları etkinlik
    // silinince cascade ile temizlenir.
    private static async Task<int> YeniEtkinlikId()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik { Ad = $"ZZ Kuyruk {Guid.NewGuid():N}", Tarih = DateTime.UtcNow.AddDays(30) };
        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
    }

    [Fact]
    public async Task EnqueueWaitlistAsync_YirmiEsZamanliKatilim_BenzersizSiraNoVermeli()
    {
        var etkinlikId = await YeniEtkinlikId();
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

    // Aynı kullanıcı iki sekmeden aynı anda "kuyruğa katıl" derse tek sıra numarası
    // almalı. Kontrol ile ekleme ayrı sorgular olsaydı ikisi de "sırada değil" görüp
    // iki kayıt açardı ve kullanıcı kuyrukta iki yer kaplardı.
    [Fact]
    public async Task EnqueueWaitlistAsync_AyniKullaniciEsZamanliOnIstek_TekKayitOlusmali()
    {
        var etkinlikId = await YeniEtkinlikId();
        try
        {
            // Yarış durumunu güvenilir biçimde tetiklemek için istekler gerçekten aynı
            // anda başlamalı. Her görev önce bağlantısını açıp ısınıyor, sonra ortak
            // kapının açılmasını bekliyor; kapı açılınca hepsi birlikte koşuyor.
            var kapi = new TaskCompletionSource();

            var gorevler = Enumerable.Range(0, 10).Select(async _ =>
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).EnqueueWaitlistAsync(etkinlikId, "tek-kullanici");
            }).ToList();

            await Task.Delay(250);
            kapi.SetResult();

            var sonuclar = await Task.WhenAll(gorevler);

            Assert.Equal(1, sonuclar.Count(s => s.HasValue));

            using var kontrol = DatabaseFixture.CreateContext();
            var kayitSayisi = await kontrol.RezervasyonKuyrugu
                .AsNoTracking()
                .CountAsync(k => k.EtkinlikId == etkinlikId && k.KullaniciId == "tek-kullanici");

            Assert.Equal(1, kayitSayisi);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task EnqueueWaitlistAsync_SuresiDolmusKaydiOlan_TekrarSirayaGirebilmeli()
    {
        var etkinlikId = await YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var ilkSiraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "kullanici-1");

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE RezervasyonKuyrugu SET Durum = N'SuresiDoldu' WHERE SiraNo = {ilkSiraNo!.Value}
                """);

            var ikinciSiraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "kullanici-1");

            Assert.NotNull(ikinciSiraNo);
            Assert.NotEqual(ilkSiraNo, ikinciSiraNo);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Arka plan görevi ile admin paneli aynı anda hak tanıyabilir. Toplam hak sayısı
    // bekleyen kişi sayısını aşmamalı — yani kimseye iki kez hak tanınmamalı.
    [Fact]
    public async Task AllocateWaitlistBatchAsync_EsZamanliCagrilar_ToplamHakSayisiniAsmamali()
    {
        var etkinlikId = await YeniEtkinlikId();
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                var servis = YeniServis(db);
                for (var i = 0; i < 10; i++)
                {
                    await servis.EnqueueWaitlistAsync(etkinlikId, $"kullanici-{i}");
                }
            }

            var kapi = new TaskCompletionSource();

            var gorevler = Enumerable.Range(0, 5).Select(async _ =>
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).AllocateWaitlistBatchAsync(etkinlikId, 4);
            }).ToList();

            await Task.Delay(250);
            kapi.SetResult();

            var atananlar = await Task.WhenAll(gorevler);

            using var kontrol = DatabaseFixture.CreateContext();
            var hakTaninan = await kontrol.RezervasyonKuyrugu
                .AsNoTracking()
                .CountAsync(k => k.EtkinlikId == etkinlikId && k.Durum == KuyrukDurumu.HakTanindi);

            // Bildirilen toplam, veritabanındaki gerçek sayıyla birebir örtüşmeli;
            // fazlası aynı kişiye iki kez hak tanındığı anlamına gelir.
            Assert.Equal(hakTaninan, atananlar.Sum());
            Assert.True(hakTaninan <= 10, $"10 bekleyen varken {hakTaninan} hak tanınmış");
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task AllocateWaitlistBatchAsync_EnDusukSiraNolaraHakTanimali()
    {
        var etkinlikId = await YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var siraNolar = new List<int>();
            for (var i = 0; i < 10; i++)
            {
                var siraNo = await servis.EnqueueWaitlistAsync(etkinlikId, $"kullanici-{i}");
                siraNolar.Add(siraNo!.Value);
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
        var etkinlikId = await YeniEtkinlikId();
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

    // Arka plan görevi önce bütün etkinlikleri listeleyip her biri için ayrı sorgu
    // çalıştırıyordu; 2000 etkinlikte her turda 4000'den fazla sorgu demekti. Tarama
    // artık tek sorgu, ama davranış aynı kalmalı: birden çok etkinlikte süresi dolan
    // haklar aynı anda kapanmalı ve her etkinlikte sıradaki kişiye devredilmeli.
    [Fact]
    public async Task PromoteExpiredAndFillAllAsync_BirdenCokEtkinlikte_HepsiniDevretmeli()
    {
        var birinciEtkinlik = await YeniEtkinlikId();
        var ikinciEtkinlik = await YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var birinciHakli = await servis.EnqueueWaitlistAsync(birinciEtkinlik, "birinci-hakli");
            var birinciBekleyen = await servis.EnqueueWaitlistAsync(birinciEtkinlik, "birinci-bekleyen");
            var ikinciHakli = await servis.EnqueueWaitlistAsync(ikinciEtkinlik, "ikinci-hakli");
            var ikinciBekleyen = await servis.EnqueueWaitlistAsync(ikinciEtkinlik, "ikinci-bekleyen");

            // İki etkinlikte de hak tanınan kişinin süresi dolmuş olsun.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE RezervasyonKuyrugu
                SET Durum = N'HakTanindi', HakBitisZamani = DATEADD(MINUTE, -1, GETUTCDATE())
                WHERE SiraNo IN ({birinciHakli}, {ikinciHakli})
                """);

            var devredilen = await servis.PromoteExpiredAndFillAllAsync();

            Assert.Equal(2, devredilen);

            var kayitlar = await db.RezervasyonKuyrugu.AsNoTracking()
                .Where(k => k.EtkinlikId == birinciEtkinlik || k.EtkinlikId == ikinciEtkinlik)
                .ToDictionaryAsync(k => k.SiraNo, k => k.Durum);

            Assert.Equal(KuyrukDurumu.SuresiDoldu, kayitlar[birinciHakli!.Value]);
            Assert.Equal(KuyrukDurumu.SuresiDoldu, kayitlar[ikinciHakli!.Value]);

            // Her etkinlikte boşalan yer kendi sırasındaki kişiye gitmeli — biri
            // diğerinin yerini almamalı.
            Assert.Equal(KuyrukDurumu.HakTanindi, kayitlar[birinciBekleyen!.Value]);
            Assert.Equal(KuyrukDurumu.HakTanindi, kayitlar[ikinciBekleyen!.Value]);
        }
        finally
        {
            await Temizle(birinciEtkinlik);
            await Temizle(ikinciEtkinlik);
        }
    }

    [Fact]
    public async Task PromoteExpiredAndFillAllAsync_SuresiDolanYoksa_HicbirSeyDegistirmemeli()
    {
        var etkinlikId = await YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var siraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "bekleyen");
            await servis.AllocateWaitlistBatchAsync(etkinlikId, 1);

            var devredilen = await servis.PromoteExpiredAndFillAllAsync();

            Assert.Equal(0, devredilen);

            var kayit = await db.RezervasyonKuyrugu.AsNoTracking().FirstAsync(k => k.SiraNo == siraNo);
            Assert.Equal(KuyrukDurumu.HakTanindi, kayit.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task CompleteQueueEntryAsync_DogruKullaniciIcin_TamamlandiYapmali()
    {
        var etkinlikId = await YeniEtkinlikId();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var siraNo = await servis.EnqueueWaitlistAsync(etkinlikId, "kullanici-1");
            await servis.AllocateWaitlistBatchAsync(etkinlikId, 1);

            var basarili = await servis.CompleteQueueEntryAsync(siraNo!.Value, "kullanici-1");

            Assert.True(basarili);
            var kayit = await db.RezervasyonKuyrugu.AsNoTracking().FirstAsync(k => k.SiraNo == siraNo);
            Assert.Equal(KuyrukDurumu.Tamamlandi, kayit.Durum);
        }
        finally { await Temizle(etkinlikId); }
    }
}

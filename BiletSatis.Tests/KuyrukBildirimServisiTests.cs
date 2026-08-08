using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Eposta;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BiletSatis.Tests;

// Bildirim, hak tanımadan ayrı bir turda gönderilir. Kritik davranışlar:
// aynı kişiye iki kez gönderilmemesi ve gönderim hatasında kaybolmaması.
[Collection("Veritabanı")]
public class KuyrukBildirimServisiTests
{
    private sealed class SahteGonderici : IEpostaGonderici
    {
        public List<(string Alici, string Konu, string Govde)> Gonderilenler { get; } = [];
        public bool HataVer { get; set; }

        public Task GonderAsync(
            string aliciAdresi,
            string konu,
            string htmlGovde,
            IReadOnlyList<GomuluGorsel>? gorseller = null,
            CancellationToken ct = default)
        {
            if (HataVer) throw new InvalidOperationException("SMTP sunucusuna ulaşılamadı");

            Gonderilenler.Add((aliciAdresi, konu, htmlGovde));
            return Task.CompletedTask;
        }
    }

    private static KuyrukBildirimServisi YeniServis(BiletSatisDbContext db, IEpostaGonderici gonderici) =>
        new(db,
            gonderici,
            Options.Create(new EpostaAyarlari { SiteAdresi = "https://test.local" }),
            NullLogger<KuyrukBildirimServisi>.Instance);

    /// <summary>Test için etkinlik + kullanıcı + hakkı tanınmış kuyruk kaydı oluşturur.</summary>
    private static async Task<(int EtkinlikId, string KullaniciId)> OrtamHazirla(string eposta)
    {
        using var db = DatabaseFixture.CreateContext();

        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Bildirim Testi {Guid.NewGuid():N}",
            Mekan = "Test Salonu, Test",
            Tarih = DateTime.UtcNow.AddDays(20)
        };
        db.Etkinlikler.Add(etkinlik);

        var kullanici = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Ad = "Test Kullanıcı",
            UserName = eposta,
            NormalizedUserName = eposta.ToUpperInvariant(),
            Email = eposta,
            NormalizedEmail = eposta.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(kullanici);

        await db.SaveChangesAsync();

        var kuyruk = new KuyrukServisi(db, NullLogger<KuyrukServisi>.Instance);
        await kuyruk.EnqueueWaitlistAsync(etkinlik.Id, kullanici.Id);
        await kuyruk.AllocateWaitlistBatchAsync(etkinlik.Id, 1);

        return (etkinlik.Id, kullanici.Id);
    }

    private static async Task Temizle(int etkinlikId, string kullaniciId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM RezervasyonKuyrugu WHERE EtkinlikId = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AspNetUsers WHERE Id = {kullaniciId}");
    }

    [Fact]
    public async Task HakkiTaninanKullaniciyaEpostaGondermeli()
    {
        var (etkinlikId, kullaniciId) = await OrtamHazirla("bildirim-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var gonderici = new SahteGonderici();

            var sayi = await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            Assert.Equal(1, sayi);
            var mesaj = Assert.Single(gonderici.Gonderilenler);
            Assert.Equal("bildirim-test@ornek.local", mesaj.Alici);
            Assert.Contains("Sıran geldi", mesaj.Konu);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // E-posta istemcisinde göreli adres çalışmaz; bağlantı tam adres olmalı.
    [Fact]
    public async Task BiletBaglantisiTamAdresOlmali()
    {
        var (etkinlikId, kullaniciId) = await OrtamHazirla("link-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var gonderici = new SahteGonderici();

            await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            var govde = Assert.Single(gonderici.Gonderilenler).Govde;
            Assert.Contains($"https://test.local/Biletler?etkinlikId={etkinlikId}", govde);
            Assert.DoesNotContain("href=\"/Biletler", govde);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // Bayrak sayesinde ikinci turda aynı kişiye tekrar gönderilmemeli.
    [Fact]
    public async Task IkinciTurdaAyniKisiyeTekrarGondermemeli()
    {
        var (etkinlikId, kullaniciId) = await OrtamHazirla("tekrar-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var gonderici = new SahteGonderici();
            var servis = YeniServis(db, gonderici);

            await servis.BekleyenBildirimleriGonderAsync();
            var ikinciTur = await servis.BekleyenBildirimleriGonderAsync();

            Assert.Equal(0, ikinciTur);
            Assert.Single(gonderici.Gonderilenler);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // Gönderim hata verirse bayrak işaretlenmemeli; bildirim kaybolmamalı.
    [Fact]
    public async Task GonderimHataVerirseSonrakiTurdaTekrarDenemeli()
    {
        var (etkinlikId, kullaniciId) = await OrtamHazirla("hata-test@ornek.local");
        try
        {
            var gonderici = new SahteGonderici { HataVer = true };

            using (var db = DatabaseFixture.CreateContext())
            {
                var sayi = await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();
                Assert.Equal(0, sayi);
                Assert.Empty(gonderici.Gonderilenler);
            }

            using (var kontrol = DatabaseFixture.CreateContext())
            {
                var kayit = await kontrol.RezervasyonKuyrugu.FirstAsync(k => k.EtkinlikId == etkinlikId);
                Assert.False(kayit.BildirimGonderildi);
            }

            // SMTP düzelince aynı kayıt gönderilebilmeli.
            gonderici.HataVer = false;
            using (var db = DatabaseFixture.CreateContext())
            {
                var sayi = await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();
                Assert.Equal(1, sayi);
            }
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // Henüz sırası gelmemiş (Beklemede) kullanıcıya bildirim gitmemeli.
    [Fact]
    public async Task SadeceBekleyenKullaniciyaGondermemeli()
    {
        using var db = DatabaseFixture.CreateContext();

        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Beklemede Testi {Guid.NewGuid():N}",
            Mekan = "Test, Test",
            Tarih = DateTime.UtcNow.AddDays(20)
        };
        db.Etkinlikler.Add(etkinlik);

        var kullanici = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Ad = "Bekleyen",
            UserName = "bekleyen@ornek.local",
            NormalizedUserName = "BEKLEYEN@ORNEK.LOCAL",
            Email = "bekleyen@ornek.local",
            NormalizedEmail = "BEKLEYEN@ORNEK.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(kullanici);
        await db.SaveChangesAsync();

        try
        {
            var kuyruk = new KuyrukServisi(db, NullLogger<KuyrukServisi>.Instance);
            await kuyruk.EnqueueWaitlistAsync(etkinlik.Id, kullanici.Id);
            // Hak tanınmadı — sadece sıraya girdi.

            var gonderici = new SahteGonderici();
            var sayi = await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            Assert.Equal(0, sayi);
            Assert.Empty(gonderici.Gonderilenler);
        }
        finally { await Temizle(etkinlik.Id, kullanici.Id); }
    }
}

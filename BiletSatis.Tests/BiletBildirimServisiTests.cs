using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Eposta;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BiletSatis.Tests;

// Satın alma bildirimi ödemeden ayrı bir turda gönderilir. Kritik davranışlar:
// bileti alan kişiye gitmesi, iki kez gönderilmemesi, hata sonrası kaybolmaması.
[Collection("Veritabanı")]
public class BiletBildirimServisiTests
{
    private sealed class SahteGonderici : IEpostaGonderici
    {
        public List<(string Alici, string Konu, string Govde, IReadOnlyList<GomuluGorsel>? Gorseller)> Gonderilenler { get; } = [];
        public bool HataVer { get; set; }

        public Task GonderAsync(
            string aliciAdresi,
            string konu,
            string htmlGovde,
            IReadOnlyList<GomuluGorsel>? gorseller = null,
            CancellationToken ct = default)
        {
            if (HataVer) throw new InvalidOperationException("SMTP sunucusuna ulaşılamadı");

            Gonderilenler.Add((aliciAdresi, konu, htmlGovde, gorseller));
            return Task.CompletedTask;
        }
    }

    private static BiletBildirimServisi YeniServis(BiletSatisDbContext db, IEpostaGonderici gonderici) =>
        new(db,
            gonderici,
            new QrKodUretici(),
            Options.Create(new EpostaAyarlari { SiteAdresi = "https://test.local" }),
            NullLogger<BiletBildirimServisi>.Instance);

    /// <summary>Etkinlik + kullanıcı + satın alınmış (bildirimi bekleyen) bilet oluşturur.</summary>
    private static async Task<(int EtkinlikId, string KullaniciId, int BiletId)> OrtamHazirla(string eposta)
    {
        using var db = DatabaseFixture.CreateContext();

        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Bilet Bildirim {Guid.NewGuid():N}",
            Mekan = "Test Salonu, İzmir",
            Tarih = DateTime.UtcNow.AddDays(15),
            Aciklama = "Test açıklaması",
            YasSiniri = 18
        };
        db.Etkinlikler.Add(etkinlik);

        var kullanici = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Ad = "Bilet Alan",
            UserName = eposta,
            NormalizedUserName = eposta.ToUpperInvariant(),
            Email = eposta,
            NormalizedEmail = eposta.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(kullanici);
        await db.SaveChangesAsync();

        var bilet = new Bilet
        {
            EtkinlikId = etkinlik.Id,
            KoltukNo = "A-07",
            Fiyat = 750m,
            Durum = BiletDurumu.Sepette,
            RezerveEdenKullaniciId = kullanici.Id,
            KilitBitisZamani = DateTime.UtcNow.AddMinutes(5)
        };
        db.Biletler.Add(bilet);
        await db.SaveChangesAsync();

        // Ödemeyi gerçek servisle tamamla: bayrağı sıfırlayan yol burası.
        var rezervasyon = new BiletRezervasyonServisi(db, NullLogger<BiletRezervasyonServisi>.Instance);
        await rezervasyon.CompletePaymentAsync(bilet.Id, kullanici.Id);

        return (etkinlik.Id, kullanici.Id, bilet.Id);
    }

    private static async Task Temizle(int etkinlikId, string kullaniciId)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Biletler WHERE EtkinlikId = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AspNetUsers WHERE Id = {kullaniciId}");
    }

    // Ödeme tamamlanınca bayrak sıfırlanmalı, yoksa bildirim hiç gönderilmez.
    [Fact]
    public async Task OdemeTamamlaninca_BildirimBekleyenDurumaGecmeli()
    {
        var (etkinlikId, kullaniciId, biletId) = await OrtamHazirla("odeme-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var bilet = await db.Biletler.FirstAsync(b => b.Id == biletId);

            Assert.Equal(BiletDurumu.Satildi, bilet.Durum);
            Assert.False(bilet.BildirimGonderildi);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    [Fact]
    public async Task BiletiAlanKisiyeEpostaGondermeli()
    {
        var (etkinlikId, kullaniciId, _) = await OrtamHazirla("alici-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var gonderici = new SahteGonderici();

            var sayi = await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            Assert.Equal(1, sayi);
            var mesaj = Assert.Single(gonderici.Gonderilenler);
            Assert.Equal("alici-test@ornek.local", mesaj.Alici);
            Assert.Contains("Biletin hazır", mesaj.Konu);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // E-postada koltuk, fiyat, mekan, yaş sınırı ve kurallar bulunmalı.
    [Fact]
    public async Task EpostaGerekliTumBilgileriIcermeli()
    {
        var (etkinlikId, kullaniciId, _) = await OrtamHazirla("icerik-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var gonderici = new SahteGonderici();

            await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            var govde = Assert.Single(gonderici.Gonderilenler).Govde;
            Assert.Contains("A-07", govde);                      // koltuk
            Assert.Contains("750", govde);                        // fiyat
            Assert.Contains("Test Salonu", govde);                // mekan
            Assert.Contains("İzmir", govde);                      // şehir
            Assert.Contains("18 yaş ve üzeri", govde);            // yaş sınırı
            Assert.Contains("Test açıklaması", govde);            // açıklama
            Assert.Contains("Kapılar etkinlikten 1 saat önce", govde); // kurallar
            Assert.Contains("https://test.local/Biletler/Biletlerim", govde); // tam adres
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    [Fact]
    public async Task EpostayaQrKoduGomulmeli()
    {
        var (etkinlikId, kullaniciId, biletId) = await OrtamHazirla("qr-test@ornek.local");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var gonderici = new SahteGonderici();

            await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            var mesaj = Assert.Single(gonderici.Gonderilenler);
            var gorsel = Assert.Single(mesaj.Gorseller!);

            Assert.Equal("image/png", gorsel.MimeTuru);
            Assert.NotEmpty(gorsel.Icerik);
            // Gövde, gömülü görseli cid ile referanslamalı.
            Assert.Contains($"cid:{gorsel.ContentId}", mesaj.Govde);
            // Bilet kodu hem gövdede hem QR içeriğinde geçmeli.
            Assert.Contains($"-{biletId}-A-07", mesaj.Govde);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    [Fact]
    public async Task IkinciTurdaTekrarGondermemeli()
    {
        var (etkinlikId, kullaniciId, _) = await OrtamHazirla("tekrar-bilet@ornek.local");
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

    [Fact]
    public async Task GonderimHataVerirseSonrakiTurdaTekrarDenemeli()
    {
        var (etkinlikId, kullaniciId, biletId) = await OrtamHazirla("hata-bilet@ornek.local");
        try
        {
            var gonderici = new SahteGonderici { HataVer = true };

            using (var db = DatabaseFixture.CreateContext())
            {
                Assert.Equal(0, await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync());
            }

            using (var kontrol = DatabaseFixture.CreateContext())
            {
                var bilet = await kontrol.Biletler.FirstAsync(b => b.Id == biletId);
                Assert.False(bilet.BildirimGonderildi);
            }

            gonderici.HataVer = false;
            using (var db = DatabaseFixture.CreateContext())
            {
                Assert.Equal(1, await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync());
            }
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // Henüz satılmamış (sepetteki) bilet için bildirim gitmemeli.
    [Fact]
    public async Task SatilmamisBiletIcinGondermemeli()
    {
        using var db = DatabaseFixture.CreateContext();

        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Sepette {Guid.NewGuid():N}",
            Mekan = "Test, Test",
            Tarih = DateTime.UtcNow.AddDays(10)
        };
        db.Etkinlikler.Add(etkinlik);

        var kullanici = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Ad = "Sepetteki",
            UserName = "sepette@ornek.local",
            NormalizedUserName = "SEPETTE@ORNEK.LOCAL",
            Email = "sepette@ornek.local",
            NormalizedEmail = "SEPETTE@ORNEK.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(kullanici);
        await db.SaveChangesAsync();

        db.Biletler.Add(new Bilet
        {
            EtkinlikId = etkinlik.Id,
            KoltukNo = "B-01",
            Fiyat = 300m,
            Durum = BiletDurumu.Sepette,
            RezerveEdenKullaniciId = kullanici.Id,
            BildirimGonderildi = false
        });
        await db.SaveChangesAsync();

        try
        {
            var gonderici = new SahteGonderici();
            var sayi = await YeniServis(db, gonderici).BekleyenBildirimleriGonderAsync();

            Assert.Equal(0, sayi);
            Assert.Empty(gonderici.Gonderilenler);
        }
        finally { await Temizle(etkinlik.Id, kullanici.Id); }
    }
}

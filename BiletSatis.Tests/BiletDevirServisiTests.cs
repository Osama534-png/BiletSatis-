using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Devir;
using BiletSatis.Web.Services.Giris;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BiletSatis.Tests;

// Devrin can alıcı noktası: bilet el değiştirdiğinde eski sahibin QR'ı ölmeli.
// Ölmezse iki kişi aynı biletle kapıya gelir ve biri boşa gitmiş olur.
[Collection("Veritabanı")]
public class BiletDevirServisiTests
{
    private static BiletDevirServisi YeniServis(BiletSatisDbContext db) =>
        new(db, NullLogger<BiletDevirServisi>.Instance);

    private static readonly IBiletKoduServisi Kodlayici =
        new BiletKoduServisi(Options.Create(new GirisAyarlari { ImzaAnahtari = "test-imza-anahtari" }));

    private static GirisServisi YeniGirisServisi(BiletSatisDbContext db) =>
        new(db, Kodlayici, NullLogger<GirisServisi>.Instance);

    private static async Task<string> KullaniciOlustur(string eposta, bool dogrulanmis = true)
    {
        using var db = DatabaseFixture.CreateContext();
        var kullanici = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            Ad = "Devir Testi",
            UserName = eposta,
            NormalizedUserName = eposta.ToUpperInvariant(),
            Email = eposta,
            NormalizedEmail = eposta.ToUpperInvariant(),
            EmailConfirmed = dogrulanmis,
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(kullanici);
        await db.SaveChangesAsync();
        return kullanici.Id;
    }

    private static async Task<(int EtkinlikId, int BiletId)> SatilmisBiletOlustur(
        string sahibiId, bool girisYapildi = false, int gunSonra = 10)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Devir {Guid.NewGuid():N}",
            Mekan = "Test Salonu, Bursa",
            Tarih = DateTime.UtcNow.AddDays(gunSonra)
        };
        etkinlik.Biletler.Add(new Bilet
        {
            KoltukNo = "A-01",
            Fiyat = 300m,
            Durum = BiletDurumu.Satildi,
            RezerveEdenKullaniciId = sahibiId,
            BildirimGonderildi = true,
            GirisYapildi = girisYapildi
        });

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();

        return (etkinlik.Id, etkinlik.Biletler[0].Id);
    }

    private static async Task Temizle(int etkinlikId, params string[] kullaniciIdler)
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Id = {etkinlikId}");
        foreach (var id in kullaniciIdler)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM AspNetUsers WHERE Id = {id}");
        }
    }

    [Fact]
    public async Task Devir_BiletiYeniSahibeGecirmeli()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"alici-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);

            Assert.Equal(DevirSonucu.Basarili, sonuc);

            using var kontrol = DatabaseFixture.CreateContext();
            var bilet = await kontrol.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletId);

            Assert.Equal(aliciId, bilet.RezerveEdenKullaniciId);
            Assert.Equal(2, bilet.KodSurumu);

            // Yeni sahibe QR'lı bilet e-postası gitsin diye bayrak sıfırlanmalı.
            Assert.False(bilet.BildirimGonderildi);
        }
        finally { await Temizle(etkinlikId, devredenId, aliciId); }
    }

    // Özelliğin güvenlik çekirdeği: devirden sonra eski sahibin elindeki QR kapıda
    // reddedilmeli, yeni sahibinki kabul edilmeli.
    [Fact]
    public async Task DevirdenSonra_EskiQrGecersizYeniQrGecerliOlmali()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"alici-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId);
        try
        {
            // Devirden önce eski sahibin elindeki kod.
            var eskiKod = Kodlayici.KodUret(biletId, 1);

            using (var db = DatabaseFixture.CreateContext())
            {
                await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);
            }

            var yeniKod = Kodlayici.KodUret(biletId, 2);

            using var kontrol = DatabaseFixture.CreateContext();
            var giris = YeniGirisServisi(kontrol);

            var eskiSonuc = await giris.DurumSorgulaAsync(eskiKod);
            Assert.Equal(GirisDurumu.GecersizKod, eskiSonuc.Durum);

            var yeniSonuc = await giris.DurumSorgulaAsync(yeniKod);
            Assert.Equal(GirisDurumu.GirisOnaylandi, yeniSonuc.Durum);
        }
        finally { await Temizle(etkinlikId, devredenId, aliciId); }
    }

    [Fact]
    public async Task DevirdenSonra_EskiQrIleGirisOnaylanamamali()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"alici-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId);
        try
        {
            var eskiKod = Kodlayici.KodUret(biletId, 1);

            using (var db = DatabaseFixture.CreateContext())
            {
                await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);
            }

            using var kontrol = DatabaseFixture.CreateContext();
            var sonuc = await YeniGirisServisi(kontrol).GirisiOnaylaAsync(eskiKod);

            Assert.Equal(GirisDurumu.GecersizKod, sonuc.Durum);

            // Giriş kaydı da düşmemeli: bilet hâlâ kullanılmamış olmalı.
            var bilet = await kontrol.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletId);
            Assert.False(bilet.GirisYapildi);
        }
        finally { await Temizle(etkinlikId, devredenId, aliciId); }
    }

    [Fact]
    public async Task KapidaOkutulmusBilet_DevredilememeLi()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"alici-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId, girisYapildi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);

            Assert.Equal(DevirSonucu.GirisYapilmis, sonuc);
        }
        finally { await Temizle(etkinlikId, devredenId, aliciId); }
    }

    [Fact]
    public async Task GecmisEtkinlik_DevredilememeLi()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"alici-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId, gunSonra: -1);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);

            Assert.Equal(DevirSonucu.EtkinlikGecmis, sonuc);
        }
        finally { await Temizle(etkinlikId, devredenId, aliciId); }
    }

    [Fact]
    public async Task BaskasininBileti_DevredilememeLi()
    {
        var sahibiId = await KullaniciOlustur($"sahibi-{Guid.NewGuid():N}@test.local");
        var yabanciId = await KullaniciOlustur($"yabanci-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"alici-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(sahibiId);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).DevretAsync(biletId, yabanciId, aliciEposta);

            Assert.Equal(DevirSonucu.BiletSizinDegil, sonuc);

            using var kontrol = DatabaseFixture.CreateContext();
            var bilet = await kontrol.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletId);
            Assert.Equal(sahibiId, bilet.RezerveEdenKullaniciId);
            Assert.Equal(1, bilet.KodSurumu);
        }
        finally { await Temizle(etkinlikId, sahibiId, yabanciId, aliciId); }
    }

    [Fact]
    public async Task DogrulanmamisHesabaDevredilememeLi()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var aliciEposta = $"dogrulanmamis-{Guid.NewGuid():N}@test.local";
        var aliciId = await KullaniciOlustur(aliciEposta, dogrulanmis: false);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);

            Assert.Equal(DevirSonucu.AliciBulunamadi, sonuc);
        }
        finally { await Temizle(etkinlikId, devredenId, aliciId); }
    }

    [Fact]
    public async Task KendineDevir_Reddedilmeli()
    {
        var eposta = $"kendisi-{Guid.NewGuid():N}@test.local";
        var kullaniciId = await KullaniciOlustur(eposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(kullaniciId);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).DevretAsync(biletId, kullaniciId, eposta);

            Assert.Equal(DevirSonucu.KendinizeDevredemezsiniz, sonuc);
        }
        finally { await Temizle(etkinlikId, kullaniciId); }
    }

    // İki sekmeden aynı bileti iki farklı kişiye devretmeye çalışmak: yalnızca biri
    // tutmalı, aksi halde bilet iki kez el değiştirmiş sayılır.
    [Fact]
    public async Task AyniBiletiIkiKisiyeAyniAndaDevretme_YalnizcaBiriTutmali()
    {
        var devredenId = await KullaniciOlustur($"devreden-{Guid.NewGuid():N}@test.local");
        var birinciEposta = $"birinci-{Guid.NewGuid():N}@test.local";
        var ikinciEposta = $"ikinci-{Guid.NewGuid():N}@test.local";
        var birinciId = await KullaniciOlustur(birinciEposta);
        var ikinciId = await KullaniciOlustur(ikinciEposta);
        var (etkinlikId, biletId) = await SatilmisBiletOlustur(devredenId);
        try
        {
            var kapi = new TaskCompletionSource();

            async Task<DevirSonucu> Dene(string aliciEposta)
            {
                using var db = DatabaseFixture.CreateContext();
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                await kapi.Task;
                return await YeniServis(db).DevretAsync(biletId, devredenId, aliciEposta);
            }

            var birinci = Dene(birinciEposta);
            var ikinci = Dene(ikinciEposta);

            await Task.Delay(250);
            kapi.SetResult();

            var sonuclar = await Task.WhenAll(birinci, ikinci);

            Assert.Equal(1, sonuclar.Count(s => s == DevirSonucu.Basarili));

            // Sürüm yalnızca bir kez artmış olmalı.
            using var kontrol = DatabaseFixture.CreateContext();
            var bilet = await kontrol.Biletler.AsNoTracking().FirstAsync(b => b.Id == biletId);
            Assert.Equal(2, bilet.KodSurumu);
        }
        finally { await Temizle(etkinlikId, devredenId, birinciId, ikinciId); }
    }
}

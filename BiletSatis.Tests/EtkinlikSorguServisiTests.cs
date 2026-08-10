using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services.Etkinlikler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BiletSatis.Tests;

// Ana sayfa filtreleri ve sayfalama artık veritabanında çalışıyor. Bu testler
// hem sonuçların doğruluğunu hem de sayfanın gerçekten sınırlandığını doğrular:
// sayfalama bozulursa sunucu yine tüm etkinlikleri okumaya başlar.
[Collection("Veritabanı")]
public class EtkinlikSorguServisiTests
{
    private static EtkinlikSorguServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()));

    /// <summary>Testler ortak veritabanını paylaştığı için her küme kendi önekiyle ayrılır.</summary>
    private readonly string _onek = $"ZZSORGU-{Guid.NewGuid():N}";

    private async Task<int> EtkinlikOlustur(
        string ad,
        string mekan = "Test Salonu, Ankara",
        EtkinlikKategorisi kategori = EtkinlikKategorisi.Konser,
        int gunSonra = 30,
        decimal fiyat = 200m,
        int biletSayisi = 5,
        bool tukendi = false)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"{_onek} {ad}",
            Mekan = mekan,
            Kategori = kategori,
            Tarih = DateTime.Now.AddDays(gunSonra)
        };

        for (var i = 1; i <= biletSayisi; i++)
        {
            etkinlik.Biletler.Add(new Bilet
            {
                KoltukNo = $"A-{i:00}",
                Fiyat = fiyat,
                Durum = tukendi ? BiletDurumu.Satildi : BiletDurumu.Satista,
                RezerveEdenKullaniciId = tukendi ? "alici" : null
            });
        }

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private async Task Temizle()
    {
        using var db = DatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Etkinlikler WHERE Ad LIKE {_onek + "%"}");
    }

    private EtkinlikFiltresi Filtre(Action<EtkinlikFiltresi>? ayarla = null)
    {
        // Testin kendi verisini ayıklamak için arama filtresi öneki kullanılıyor.
        var filtre = new EtkinlikFiltresi { Arama = _onek };
        ayarla?.Invoke(filtre);
        return filtre;
    }

    [Fact]
    public async Task Sayfalama_YalnizcaIstenenSayfayiDondurmeli()
    {
        for (var i = 1; i <= 7; i++) await EtkinlikOlustur($"Etkinlik {i:00}", gunSonra: i);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var ilk = await servis.AraAsync(Filtre(f => { f.Sayfa = 1; f.SayfaBoyutu = 3; }));

            Assert.Equal(7, ilk.ToplamKayit);
            Assert.Equal(3, ilk.Ogeler.Count);
            Assert.Equal(3, ilk.ToplamSayfa);
            Assert.False(ilk.OncekiVarMi);
            Assert.True(ilk.SonrakiVarMi);

            var son = await servis.AraAsync(Filtre(f => { f.Sayfa = 3; f.SayfaBoyutu = 3; }));

            Assert.Single(son.Ogeler);
            Assert.True(son.OncekiVarMi);
            Assert.False(son.SonrakiVarMi);

            // Sayfalar çakışmamalı.
            Assert.Empty(ilk.Ogeler.Select(o => o.Id).Intersect(son.Ogeler.Select(o => o.Id)));
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task Kategori_FiltresiUygulanmali()
    {
        await EtkinlikOlustur("Konser", kategori: EtkinlikKategorisi.Konser);
        await EtkinlikOlustur("Tiyatro", kategori: EtkinlikKategorisi.Tiyatro);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).AraAsync(Filtre(f => f.Kategori = EtkinlikKategorisi.Tiyatro));

            Assert.Equal(1, sonuc.ToplamKayit);
            Assert.All(sonuc.Ogeler, o => Assert.Equal(EtkinlikKategorisi.Tiyatro, o.Kategori));
        }
        finally { await Temizle(); }
    }

    // Şehir artık Mekan metninden ayrıştırılmıyor, kendi sütununda tutuluyor;
    // değerin kaydetme sırasında doğru türetildiğini de bu test doğruluyor.
    [Fact]
    public async Task Sehir_FiltresiUygulanmali()
    {
        await EtkinlikOlustur("İzmirli", mekan: "Kültürpark Açıkhava, İzmir");
        await EtkinlikOlustur("Ankaralı", mekan: "Congresium, Ankara");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).AraAsync(Filtre(f => f.Sehir = "İzmir"));

            Assert.Equal(1, sonuc.ToplamKayit);
            Assert.Contains("İzmirli", sonuc.Ogeler[0].Ad);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task TukenenEtkinlikler_VarsayilanOlarakGizlenmeli()
    {
        await EtkinlikOlustur("Musait");
        await EtkinlikOlustur("Tukenmis", tukendi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var gizli = await servis.AraAsync(Filtre());
            Assert.Equal(1, gizli.ToplamKayit);
            Assert.Contains("Musait", gizli.Ogeler[0].Ad);

            var hepsi = await servis.AraAsync(Filtre(f => f.TukenenleriGoster = true));
            Assert.Equal(2, hepsi.ToplamKayit);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task FiyatTavani_UstundekileriElemeli()
    {
        await EtkinlikOlustur("Ucuz", fiyat: 100m);
        await EtkinlikOlustur("Pahali", fiyat: 900m);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).AraAsync(Filtre(f => f.EnYuksekFiyat = 500m));

            Assert.Equal(1, sonuc.ToplamKayit);
            Assert.Contains("Ucuz", sonuc.Ogeler[0].Ad);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task Siralama_FiyataGoreCalismali()
    {
        await EtkinlikOlustur("Orta", fiyat: 300m, gunSonra: 5);
        await EtkinlikOlustur("Ucuz", fiyat: 100m, gunSonra: 10);
        await EtkinlikOlustur("Pahali", fiyat: 900m, gunSonra: 1);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var artan = await servis.AraAsync(Filtre(f => f.Siralama = "fiyat-artan"));
            Assert.Equal(new decimal?[] { 100m, 300m, 900m }, artan.Ogeler.Select(o => o.EnDusukFiyat).ToArray());

            var azalan = await servis.AraAsync(Filtre(f => f.Siralama = "fiyat-azalan"));
            Assert.Equal(new decimal?[] { 900m, 300m, 100m }, azalan.Ogeler.Select(o => o.EnDusukFiyat).ToArray());
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task Siralama_VarsayilanOlarakYaklasanTarih()
    {
        await EtkinlikOlustur("Uzak", gunSonra: 40);
        await EtkinlikOlustur("Yakin", gunSonra: 2);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).AraAsync(Filtre());

            Assert.Contains("Yakin", sonuc.Ogeler[0].Ad);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task TarihAraligi_HaftaFiltresiUygulanmali()
    {
        await EtkinlikOlustur("BuHafta", gunSonra: 3);
        await EtkinlikOlustur("GelecekAy", gunSonra: 25);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).AraAsync(Filtre(f => f.Tarih = "hafta"));

            Assert.Equal(1, sonuc.ToplamKayit);
            Assert.Contains("BuHafta", sonuc.Ogeler[0].Ad);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public void SayfaBoyutu_UstSinirinUstuneCikamamali()
    {
        var filtre = new EtkinlikFiltresi { SayfaBoyutu = 5000 };

        // İstemciden gelen değer sınırlanmazsa tek istekle tüm tablo okunabilirdi.
        Assert.Equal(EtkinlikFiltresi.VarsayilanSayfaBoyutu, filtre.SayfaBoyutu);
    }

    [Fact]
    public async Task Sehirler_YalnizcaDoluSehirleriDondurmeli()
    {
        await EtkinlikOlustur("Sehirli", mekan: "Salon, Bursa");
        await EtkinlikOlustur("Sehirsiz", mekan: "Salon");
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sehirler = await YeniServis(db).SehirlerAsync();

            Assert.Contains("Bursa", sehirler);
            Assert.DoesNotContain("", sehirler);
        }
        finally { await Temizle(); }
    }
}

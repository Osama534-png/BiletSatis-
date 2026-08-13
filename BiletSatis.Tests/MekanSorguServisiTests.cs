using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Mekanlar;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Tests;

// Mekan ayrı bir tablo değil: kimliği etkinlik satırındaki "Salon Adı, Şehir"
// metninin tamamı. Bu testler gruplamanın o metne göre yapıldığını, yaklaşan ile
// geçmişin doğru ayrıldığını ve sayfalamanın gerçekten sınırladığını doğrular.
[Collection("Veritabanı")]
public class MekanSorguServisiTests
{
    /// <summary>Testler ortak veritabanını paylaştığı için her küme kendi önekiyle ayrılır.</summary>
    private readonly string _onek = $"ZZMEKAN-{Guid.NewGuid():N}";

    private string Mekan(string salon, string sehir) => $"{_onek} {salon}, {sehir}";

    private static MekanSorguServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) => new(db);

    private async Task<int> EtkinlikOlustur(
        string ad,
        string mekan,
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

    [Fact]
    public async Task Ozet_YaklasanVeGecmisiAyriSaymali()
    {
        var mekan = Mekan("Ana Salon", "Ankara");
        await EtkinlikOlustur("Yaklasan 1", mekan, gunSonra: 5);
        await EtkinlikOlustur("Yaklasan 2", mekan, gunSonra: 20);
        await EtkinlikOlustur("Gecmis 1", mekan, gunSonra: -10);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var ozet = await YeniServis(db).OzetAsync(mekan);

            Assert.NotNull(ozet);
            Assert.Equal(2, ozet!.YaklasanEtkinlik);
            Assert.Equal(1, ozet.GecmisEtkinlik);
            Assert.Equal(3, ozet.ToplamEtkinlik);
            Assert.Equal("Ankara", ozet.Sehir);
            Assert.EndsWith("Ana Salon", ozet.SalonAdi);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task Ozet_EtkinligiOlmayanMekandaNullDonmeli()
    {
        using var db = DatabaseFixture.CreateContext();

        Assert.Null(await YeniServis(db).OzetAsync(Mekan("Hic Yok", "İzmir")));
        Assert.Null(await YeniServis(db).OzetAsync(""));
    }

    // Gruplama yalnızca salon adına göre yapılsaydı farklı şehirlerdeki aynı adlı
    // salonlar ("Kültür Merkezi, Ankara" / "Kültür Merkezi, İzmir") tek mekan sayılır,
    // kullanıcı başka şehirdeki etkinliği kendi mekanının programında görürdü.
    [Fact]
    public async Task Ozet_AyniAdliFarkliSehirdekiSalonlarAyriMekanSayilmali()
    {
        var ankara = Mekan("Kultur Merkezi", "Ankara");
        var izmir = Mekan("Kultur Merkezi", "İzmir");
        await EtkinlikOlustur("Ankara Konseri", ankara);
        await EtkinlikOlustur("Izmir Konseri 1", izmir);
        await EtkinlikOlustur("Izmir Konseri 2", izmir);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            Assert.Equal(1, (await servis.OzetAsync(ankara))!.ToplamEtkinlik);
            Assert.Equal(2, (await servis.OzetAsync(izmir))!.ToplamEtkinlik);
        }
        finally { await Temizle(); }
    }

    // "… ₺'den başlıyor" yalnızca hâlâ satın alınabilir biletler için anlamlı:
    // geçmiş etkinliğin ya da satılmış biletin fiyatı kullanıcıya bir şey vaat etmez.
    [Fact]
    public async Task Ozet_EnDusukFiyatYalnizcaYaklasanVeSatistakiBiletlerdenGelmeli()
    {
        var mekan = Mekan("Fiyat Salonu", "Bursa");
        await EtkinlikOlustur("Ucuz Gecmis", mekan, gunSonra: -3, fiyat: 50m);
        await EtkinlikOlustur("Ucuz Tukenmis", mekan, gunSonra: 10, fiyat: 80m, tukendi: true);
        await EtkinlikOlustur("Yaklasan", mekan, gunSonra: 15, fiyat: 300m);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var ozet = await YeniServis(db).OzetAsync(mekan);

            Assert.Equal(300m, ozet!.EnDusukFiyat);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task Etkinlikler_YaklasanlarYakinTarihtenBaslamali()
    {
        var mekan = Mekan("Sira Salonu", "Ankara");
        await EtkinlikOlustur("Uzak", mekan, gunSonra: 40);
        await EtkinlikOlustur("Yakin", mekan, gunSonra: 3);
        await EtkinlikOlustur("Orta", mekan, gunSonra: 15);
        await EtkinlikOlustur("Gecmis", mekan, gunSonra: -5);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var sonuc = await YeniServis(db).EtkinliklerAsync(mekan, gecmis: false, sayfa: 1, sayfaBoyutu: 12);

            Assert.Equal(3, sonuc.ToplamKayit);
            Assert.Collection(sonuc.Ogeler,
                e => Assert.EndsWith("Yakin", e.Ad),
                e => Assert.EndsWith("Orta", e.Ad),
                e => Assert.EndsWith("Uzak", e.Ad));
        }
        finally { await Temizle(); }
    }

    // Geçmişte kullanıcı "burada en son ne oldu" diye bakar; sıralama ters olmalı.
    [Fact]
    public async Task Etkinlikler_GecmisteEnSonYapilanEnUsttteOlmali()
    {
        var mekan = Mekan("Arsiv Salonu", "Ankara");
        await EtkinlikOlustur("Cok Eski", mekan, gunSonra: -60);
        await EtkinlikOlustur("Yeni Gecmis", mekan, gunSonra: -2);
        await EtkinlikOlustur("Orta Gecmis", mekan, gunSonra: -20);
        await EtkinlikOlustur("Gelecek", mekan, gunSonra: 10);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var sonuc = await YeniServis(db).EtkinliklerAsync(mekan, gecmis: true, sayfa: 1, sayfaBoyutu: 12);

            Assert.Equal(3, sonuc.ToplamKayit);
            Assert.Collection(sonuc.Ogeler,
                e => Assert.EndsWith("Yeni Gecmis", e.Ad),
                e => Assert.EndsWith("Orta Gecmis", e.Ad),
                e => Assert.EndsWith("Cok Eski", e.Ad));
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task Etkinlikler_YalnizcaIstenenSayfayiOkumali()
    {
        var mekan = Mekan("Sayfa Salonu", "Ankara");
        for (var i = 1; i <= 7; i++) await EtkinlikOlustur($"Etkinlik {i:00}", mekan, gunSonra: i);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var servis = YeniServis(db);

            var ilk = await servis.EtkinliklerAsync(mekan, gecmis: false, sayfa: 1, sayfaBoyutu: 3);

            Assert.Equal(7, ilk.ToplamKayit);
            Assert.Equal(3, ilk.Ogeler.Count);
            Assert.Equal(3, ilk.ToplamSayfa);
            Assert.False(ilk.OncekiVarMi);
            Assert.True(ilk.SonrakiVarMi);

            var son = await servis.EtkinliklerAsync(mekan, gecmis: false, sayfa: 3, sayfaBoyutu: 3);

            Assert.Single(son.Ogeler);
            Assert.True(son.OncekiVarMi);
            Assert.False(son.SonrakiVarMi);
        }
        finally { await Temizle(); }
    }

    // Sayfa numarası adres çubuğundan geliyor; ana sayfadaki taşma korumasının
    // (negatif OFFSET) aynısı burada da geçerli olmalı.
    [Fact]
    public async Task Etkinlikler_AsiriSayfaNumarasiSunucuyuDusurmemeli()
    {
        var mekan = Mekan("Sinir Salonu", "Ankara");
        await EtkinlikOlustur("Tek", mekan);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var sonuc = await YeniServis(db)
                .EtkinliklerAsync(mekan, gecmis: false, sayfa: int.MaxValue, sayfaBoyutu: int.MaxValue);

            Assert.Empty(sonuc.Ogeler);
            Assert.Equal(1, sonuc.ToplamKayit);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task DigerEtkinlikler_KendisiniVeGecmisleriIcermemeli()
    {
        var mekan = Mekan("Diger Salonu", "Ankara");
        var buEtkinlik = await EtkinlikOlustur("Bu Etkinlik", mekan, gunSonra: 10);
        await EtkinlikOlustur("Diger 1", mekan, gunSonra: 20);
        await EtkinlikOlustur("Diger 2", mekan, gunSonra: 30);
        await EtkinlikOlustur("Gecmis", mekan, gunSonra: -5);
        await EtkinlikOlustur("Baska Mekan", Mekan("Baska", "Ankara"), gunSonra: 12);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var digerler = await YeniServis(db).DigerEtkinliklerAsync(mekan, buEtkinlik, 5);

            Assert.Equal(2, digerler.Count);
            Assert.DoesNotContain(digerler, e => e.Id == buEtkinlik);
            Assert.All(digerler, e => Assert.Equal(mekan, e.Mekan));
            Assert.Collection(digerler,
                e => Assert.EndsWith("Diger 1", e.Ad),
                e => Assert.EndsWith("Diger 2", e.Ad));
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task DigerEtkinlikler_IstenenSayidanFazlasiniDondurmemeli()
    {
        var mekan = Mekan("Limit Salonu", "Ankara");
        var buEtkinlik = await EtkinlikOlustur("Bu", mekan, gunSonra: 1);
        for (var i = 1; i <= 6; i++) await EtkinlikOlustur($"Diger {i}", mekan, gunSonra: i + 5);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var digerler = await YeniServis(db).DigerEtkinliklerAsync(mekan, buEtkinlik, 3);

            Assert.Equal(3, digerler.Count);
            Assert.Empty(await YeniServis(db).DigerEtkinliklerAsync(mekan, buEtkinlik, 0));
            Assert.Empty(await YeniServis(db).DigerEtkinliklerAsync("", buEtkinlik, 3));
        }
        finally { await Temizle(); }
    }
}

using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Populerlik;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BiletSatis.Tests;

// "En çok satan" sıralaması ve trend penceresi. Buradaki hatalar sessizdir:
// yanlış sıralanan liste de, boş kalan dönem de hiçbir hata vermeden görünür.
[Collection("Veritabanı")]
public class PopulerlikServisiTests
{
    private readonly string _onek = $"ZZPOP-{Guid.NewGuid():N}";

    // Her test kendi verisini görsün diye önbellek her serviste sıfırdan kurulur;
    // paylaşılan bir önbellek testleri birbirine bağlardı.
    private static PopulerlikServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) =>
        new(db, new MemoryCache(new MemoryCacheOptions()));

    /// <summary>
    /// Belirtilen sayıda satılmış bilet içeren etkinlik kurar.
    /// <paramref name="satisGunOnce"/> null ise satış zamanı yazılmaz — sütun
    /// eklenmeden önce satılmış eski kayıtları temsil eder.
    /// </summary>
    private async Task<int> EtkinlikOlustur(
        string ad,
        int satilan,
        int satista = 0,
        int gunSonra = 30,
        double? satisGunOnce = 1)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"{_onek} {ad}",
            Mekan = $"{_onek} Salon, Ankara",
            Tarih = DateTime.Now.AddDays(gunSonra)
        };

        var no = 1;
        for (var i = 0; i < satilan; i++)
        {
            etkinlik.Biletler.Add(new Bilet
            {
                KoltukNo = $"A-{no++:00}",
                Fiyat = 100m,
                Durum = BiletDurumu.Satildi,
                RezerveEdenKullaniciId = "alici",
                SatisZamani = satisGunOnce.HasValue ? DateTime.UtcNow.AddDays(-satisGunOnce.Value) : null
            });
        }

        for (var i = 0; i < satista; i++)
        {
            etkinlik.Biletler.Add(new Bilet { KoltukNo = $"A-{no++:00}", Fiyat = 100m, Durum = BiletDurumu.Satista });
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

    private async Task<List<BiletSatis.Web.Models.PopulerEtkinlikVm>> Liste(
        PopulerlikDonemi donem, int adet = 10)
    {
        using var db = DatabaseFixture.CreateContext();
        var tumu = await YeniServis(db).EnCokSatanlarAsync(donem, adet);

        // Ortak veritabanında başka testlerin verisi de olabilir; kendi önekimizi ayıklıyoruz.
        return tumu.Where(p => p.Kart.Ad.StartsWith(_onek, StringComparison.Ordinal)).ToList();
    }

    [Fact]
    public async Task EnCokSatanlar_SatisSayisinaGoreAzalanSiralanmali()
    {
        await EtkinlikOlustur("Az Satan", satilan: 2, satista: 8);
        await EtkinlikOlustur("Cok Satan", satilan: 9, satista: 1);
        await EtkinlikOlustur("Orta Satan", satilan: 5, satista: 5);
        try
        {
            var liste = await Liste(PopulerlikDonemi.TumZamanlar);

            Assert.Collection(liste,
                p => Assert.EndsWith("Cok Satan", p.Kart.Ad),
                p => Assert.EndsWith("Orta Satan", p.Kart.Ad),
                p => Assert.EndsWith("Az Satan", p.Kart.Ad));

            Assert.Equal(9, liste[0].SatilanBilet);
            Assert.Equal(5, liste[1].SatilanBilet);
            Assert.Equal(2, liste[2].SatilanBilet);
        }
        finally { await Temizle(); }
    }

    // Kullanıcının artık bilet alamayacağı bir etkinliği "en çok satan" diye
    // önermenin karşılığı yok; liste satın alınabilir etkinlikleri gösterir.
    [Fact]
    public async Task EnCokSatanlar_SonaErmisEtkinligiIcermemeli()
    {
        await EtkinlikOlustur("Gecmis Rekortmen", satilan: 50, gunSonra: -5);
        await EtkinlikOlustur("Yaklasan", satilan: 3);
        try
        {
            var liste = await Liste(PopulerlikDonemi.TumZamanlar);

            Assert.Single(liste);
            Assert.EndsWith("Yaklasan", liste[0].Kart.Ad);
        }
        finally { await Temizle(); }
    }

    // Trend'in bütün anlamı bu: toplam satışta önde olan etkinlik, dar pencerede
    // geride kalabilir. Pencere çalışmazsa liste sessizce "en çok satan"a döner.
    [Fact]
    public async Task EnCokSatanlar_DonemPenceresiDisindakiSatislariSaymamali()
    {
        // Toplamda daha çok satmış ama satışları eski.
        await EtkinlikOlustur("Eski Rekortmen", satilan: 20, satista: 5, satisGunOnce: 40);
        // Toplamda daha az ama satışları bu hafta.
        await EtkinlikOlustur("Bu Hafta Trend", satilan: 6, satista: 5, satisGunOnce: 2);
        try
        {
            var tumZamanlar = await Liste(PopulerlikDonemi.TumZamanlar);
            Assert.EndsWith("Eski Rekortmen", tumZamanlar[0].Kart.Ad);

            var hafta = await Liste(PopulerlikDonemi.Hafta);
            Assert.Single(hafta);
            Assert.EndsWith("Bu Hafta Trend", hafta[0].Kart.Ad);
            Assert.Equal(6, hafta[0].SatilanBilet);

            // 30 günlük pencere ikisini de değil, yalnızca 40 gün öncesini dışarıda bırakır.
            var ay = await Liste(PopulerlikDonemi.Ay);
            Assert.Single(ay);
            Assert.EndsWith("Bu Hafta Trend", ay[0].Kart.Ad);
        }
        finally { await Temizle(); }
    }

    /// <summary>
    /// Satış zamanı sütunu eklenmeden önce satılmış biletlerin zamanı bilinmiyor.
    /// Bunları dönem sıralamasına katmak, olmayan bir satış hareketi uydurmak olurdu;
    /// "tüm zamanlar" sıralamasından düşürmek ise gerçek satışı yok saymak olurdu.
    /// </summary>
    [Fact]
    public async Task EnCokSatanlar_SatisZamaniBilinmeyenKayitlarYalnizcaTumZamanlardaSayilmali()
    {
        await EtkinlikOlustur("Zamansiz Satis", satilan: 15, satista: 5, satisGunOnce: null);
        try
        {
            var tumZamanlar = await Liste(PopulerlikDonemi.TumZamanlar);
            Assert.Single(tumZamanlar);
            Assert.Equal(15, tumZamanlar[0].SatilanBilet);

            Assert.Empty(await Liste(PopulerlikDonemi.Hafta));
            Assert.Empty(await Liste(PopulerlikDonemi.Ay));
        }
        finally { await Temizle(); }
    }

    // Doluluk dönemden bağımsız olmalı: "son 7 günde 6 bilet sattı" ile
    // "kapasitesinin %6'sı dolu" aynı şey değil.
    [Fact]
    public async Task EnCokSatanlar_DolulukDonemdenBagimsizOlmali()
    {
        // 20 satılmış (10'u eski, 10'u yeni) + 20 satışta = 40 kapasite, %50 dolu.
        var id = await EtkinlikOlustur("Doluluk", satilan: 10, satista: 20, satisGunOnce: 2);
        using (var db = DatabaseFixture.CreateContext())
        {
            var etkinlik = await db.Etkinlikler.Include(e => e.Biletler).FirstAsync(e => e.Id == id);
            for (var i = 1; i <= 10; i++)
            {
                etkinlik.Biletler.Add(new Bilet
                {
                    KoltukNo = $"B-{i:00}",
                    Fiyat = 100m,
                    Durum = BiletDurumu.Satildi,
                    RezerveEdenKullaniciId = "alici",
                    SatisZamani = DateTime.UtcNow.AddDays(-100)
                });
            }
            await db.SaveChangesAsync();
        }

        try
        {
            var hafta = (await Liste(PopulerlikDonemi.Hafta)).Single();

            // Sıralama ölçütü dönemdeki satış: 10.
            Assert.Equal(10, hafta.SatilanBilet);
            // Doluluk ise bütün satışlardan: 20 / 40 = %50.
            Assert.Equal(20, hafta.ToplamSatilan);
            Assert.Equal(40, hafta.ToplamBilet);
            Assert.Equal(50, hafta.DolulukYuzdesi);
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task EnCokSatanlar_IstenenSayidanFazlasiniDondurmemeli()
    {
        for (var i = 1; i <= 6; i++) await EtkinlikOlustur($"Etkinlik {i:00}", satilan: i, satista: 2);
        try
        {
            using var db = DatabaseFixture.CreateContext();

            var liste = await YeniServis(db).EnCokSatanlarAsync(PopulerlikDonemi.TumZamanlar, 3);
            Assert.Equal(3, liste.Count);

            Assert.Empty(await YeniServis(db).EnCokSatanlarAsync(PopulerlikDonemi.TumZamanlar, 0));
        }
        finally { await Temizle(); }
    }

    [Fact]
    public async Task EnCokSatanlar_SatisiOlmayanEtkinligiIcermemeli()
    {
        await EtkinlikOlustur("Hic Satmamis", satilan: 0, satista: 10);
        await EtkinlikOlustur("Satmis", satilan: 4, satista: 6);
        try
        {
            var liste = await Liste(PopulerlikDonemi.TumZamanlar);

            Assert.Single(liste);
            Assert.EndsWith("Satmis", liste[0].Kart.Ad);
        }
        finally { await Temizle(); }
    }

    [Theory]
    [InlineData("hafta", PopulerlikDonemi.Hafta)]
    [InlineData("ay", PopulerlikDonemi.Ay)]
    [InlineData("tumu", PopulerlikDonemi.TumZamanlar)]
    [InlineData(null, PopulerlikDonemi.TumZamanlar)]
    [InlineData("uydurma", PopulerlikDonemi.TumZamanlar)]
    public void DonemCozme_TaninmayanDegerVarsayilanaDusmeli(string? anahtar, PopulerlikDonemi beklenen)
    {
        Assert.Equal(beklenen, PopulerlikDonemleri.Coz(anahtar));
    }
}

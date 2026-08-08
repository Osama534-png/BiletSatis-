using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Degerlendirmeler;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BiletSatis.Tests;

[Collection("Veritabanı")]
public class DegerlendirmeServisiTests
{
    private static DegerlendirmeServisi YeniServis(BiletSatis.Web.Data.BiletSatisDbContext db) =>
        new(db, NullLogger<DegerlendirmeServisi>.Instance);

    /// <summary>
    /// Bir etkinlik ve tek bilet oluşturur. <paramref name="girisYapildi"/> ile biletin
    /// kapıda okutulup okutulmadığı ayarlanır — değerlendirme hakkının koşulu budur.
    /// </summary>
    private static async Task<int> EtkinlikOlustur(
        string? sahibiKullaniciId = null, bool satildi = false, bool girisYapildi = false)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik { Ad = $"Test-{Guid.NewGuid()}", Tarih = DateTime.UtcNow.AddDays(-1) };

        etkinlik.Biletler.Add(new Bilet
        {
            KoltukNo = "T-01",
            Fiyat = 100m,
            Durum = satildi ? BiletDurumu.Satildi : BiletDurumu.Satista,
            RezerveEdenKullaniciId = sahibiKullaniciId,
            GirisYapildi = girisYapildi,
            GirisZamani = girisYapildi ? DateTime.UtcNow.AddHours(-2) : null
        });

        db.Etkinlikler.Add(etkinlik);
        await db.SaveChangesAsync();
        return etkinlik.Id;
    }

    private static async Task<int> DegerlendirmeSayisi(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        return await db.Degerlendirmeler.AsNoTracking().CountAsync(d => d.EtkinlikId == etkinlikId);
    }

    private static async Task Temizle(int etkinlikId)
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = await db.Etkinlikler.FindAsync(etkinlikId);
        if (etkinlik != null)
        {
            db.Etkinlikler.Remove(etkinlik);
            await db.SaveChangesAsync();
        }
    }

    // Özelliğin can alıcı kuralı: bilet satın almak yetmiyor, kapıda okutulmuş olmalı.
    [Fact]
    public async Task DegerlendirebilirMiAsync_BiletiVarAmaGirisYapmamis_FalseDonmeli()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: false);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            Assert.False(await YeniServis(db).DegerlendirebilirMiAsync(etkinlikId, "kullanici-1"));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task DegerlendirebilirMiAsync_GirisiOnaylanmisBiletSahibi_TrueDonmeli()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            Assert.True(await YeniServis(db).DegerlendirebilirMiAsync(etkinlikId, "kullanici-1"));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task DegerlendirebilirMiAsync_BaskasininOkutulmusBileti_FalseDonmeli()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            Assert.False(await YeniServis(db).DegerlendirebilirMiAsync(etkinlikId, "kullanici-2"));
        }
        finally { await Temizle(etkinlikId); }
    }

    // Arayüzde formu gizlemek yeterli değil — form doğrudan da gönderilebilir.
    [Fact]
    public async Task KaydetAsync_KatilmayanKullanici_KayitOlusturmamali()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: false);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).KaydetAsync(etkinlikId, "kullanici-1", 5, "Harikaydı");

            Assert.Equal(DegerlendirmeSonucu.KatilimYok, sonuc);
            Assert.Equal(0, await DegerlendirmeSayisi(etkinlikId));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task KaydetAsync_GecersizPuan_ReddetmeLi(int puan)
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).KaydetAsync(etkinlikId, "kullanici-1", puan, null);

            Assert.Equal(DegerlendirmeSonucu.GecersizPuan, sonuc);
            Assert.Equal(0, await DegerlendirmeSayisi(etkinlikId));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task KaydetAsync_GirisYapmisKullanici_KaydetmeLi()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db).KaydetAsync(etkinlikId, "kullanici-1", 4, "  Güzeldi  ");

            Assert.Equal(DegerlendirmeSonucu.Kaydedildi, sonuc);

            using var kontrol = DatabaseFixture.CreateContext();
            var kayit = await kontrol.Degerlendirmeler.AsNoTracking().SingleAsync(d => d.EtkinlikId == etkinlikId);
            Assert.Equal(4, kayit.Puan);
            Assert.Equal("Güzeldi", kayit.Yorum);
            Assert.Null(kayit.GuncellemeZamani);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task KaydetAsync_AyniKullaniciIkinciKez_YeniKayitAcmayipGuncellemeLi()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                await YeniServis(db).KaydetAsync(etkinlikId, "kullanici-1", 2, "İdare eder");
            }

            using var db2 = DatabaseFixture.CreateContext();
            var sonuc = await YeniServis(db2).KaydetAsync(etkinlikId, "kullanici-1", 5, "Fikrim değişti");

            Assert.Equal(DegerlendirmeSonucu.Guncellendi, sonuc);
            Assert.Equal(1, await DegerlendirmeSayisi(etkinlikId));

            using var kontrol = DatabaseFixture.CreateContext();
            var kayit = await kontrol.Degerlendirmeler.AsNoTracking().SingleAsync(d => d.EtkinlikId == etkinlikId);
            Assert.Equal(5, kayit.Puan);
            Assert.Equal("Fikrim değişti", kayit.Yorum);
            Assert.NotNull(kayit.GuncellemeZamani);
        }
        finally { await Temizle(etkinlikId); }
    }

    // Aynı kullanıcının iki isteği aynı anda gelirse benzersiz dizin ikinci satırı
    // engellemeli — kullanıcı iki kez oy kullanıp ortalamayı bozamaz.
    [Fact]
    public async Task KaydetAsync_AyniAndaIkiIstek_TekKayitOlusmali()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            var gorevler = Enumerable.Range(0, 2).Select(async i =>
            {
                using var db = DatabaseFixture.CreateContext();
                return await YeniServis(db).KaydetAsync(etkinlikId, "kullanici-1", 5, $"deneme-{i}");
            });

            var sonuclar = await Task.WhenAll(gorevler);

            Assert.All(sonuclar, s => Assert.True(
                s is DegerlendirmeSonucu.Kaydedildi or DegerlendirmeSonucu.Guncellendi,
                $"Beklenmeyen sonuç: {s}"));

            Assert.Equal(1, await DegerlendirmeSayisi(etkinlikId));
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task KaydetAsync_CokUzunYorum_SiniraKirpmali()
    {
        var etkinlikId = await EtkinlikOlustur("kullanici-1", satildi: true, girisYapildi: true);
        try
        {
            using var db = DatabaseFixture.CreateContext();
            await YeniServis(db).KaydetAsync(etkinlikId, "kullanici-1", 3, new string('x', 1500));

            using var kontrol = DatabaseFixture.CreateContext();
            var kayit = await kontrol.Degerlendirmeler.AsNoTracking().SingleAsync(d => d.EtkinlikId == etkinlikId);
            Assert.Equal(Degerlendirme.EnUzunYorum, kayit.Yorum.Length);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task OzetAsync_OrtalamaVeDagilimiHesaplamali()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using (var db = DatabaseFixture.CreateContext())
            {
                // Üç ayrı kullanıcının okutulmuş bileti olsun ki değerlendirme hakları doğsun.
                var etkinlik = await db.Etkinlikler.Include(e => e.Biletler).FirstAsync(e => e.Id == etkinlikId);
                foreach (var kullanici in new[] { "kullanici-1", "kullanici-2", "kullanici-3" })
                {
                    etkinlik.Biletler.Add(new Bilet
                    {
                        KoltukNo = $"T-{kullanici[^1]}0",
                        Fiyat = 100m,
                        Durum = BiletDurumu.Satildi,
                        RezerveEdenKullaniciId = kullanici,
                        GirisYapildi = true
                    });
                }
                await db.SaveChangesAsync();
            }

            using (var db = DatabaseFixture.CreateContext())
            {
                var servis = YeniServis(db);
                await servis.KaydetAsync(etkinlikId, "kullanici-1", 5, "");
                await servis.KaydetAsync(etkinlikId, "kullanici-2", 4, "");
                await servis.KaydetAsync(etkinlikId, "kullanici-3", 5, "");
            }

            using var kontrol = DatabaseFixture.CreateContext();
            var ozet = await YeniServis(kontrol).OzetAsync(etkinlikId);

            Assert.Equal(3, ozet.Adet);
            Assert.Equal(4.7m, ozet.Ortalama);
            Assert.Equal(2, ozet.Dagilim[5]);
            Assert.Equal(1, ozet.Dagilim[4]);
            Assert.Equal(0, ozet.Dagilim[1]);
            Assert.Equal(3, ozet.Satirlar.Count);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task OzetAsync_DegerlendirmeYoksa_BosOzetDonmeli()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var ozet = await YeniServis(db).OzetAsync(etkinlikId);

            Assert.Equal(0, ozet.Adet);
            Assert.Null(ozet.Ortalama);
            Assert.Empty(ozet.Satirlar);
        }
        finally { await Temizle(etkinlikId); }
    }
}

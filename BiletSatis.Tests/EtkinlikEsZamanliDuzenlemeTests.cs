using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Tests;

// Etkinlik düzenleme ekranı oku-değiştir-kaydet akışıyla çalışır. Bilet satın almada
// bu akış hiç kullanılmıyor (tek atomik UPDATE var), ama burada kaçınılmaz: form
// açılıyor, kullanıcı düşünüyor, sonra kaydediyor. Araya başka bir yönetici girerse
// ikinci kayıt birincinin değişikliğini sessizce ezmemeli.
[Collection("Veritabanı")]
public class EtkinlikEsZamanliDuzenlemeTests
{
    private static async Task<int> EtkinlikOlustur()
    {
        using var db = DatabaseFixture.CreateContext();
        var etkinlik = new Etkinlik
        {
            Ad = $"ZZ Duzenleme {Guid.NewGuid():N}",
            Mekan = "Test Salonu, Ankara",
            Tarih = DateTime.UtcNow.AddDays(20)
        };
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
    public async Task IkiYoneticiAyniEtkinligiDuzenlerse_IkincisiReddedilmeli()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            // İki yönetici de formu aynı anda açtı: ikisi de aynı satır sürümünü okudu.
            using var birinciBaglanti = DatabaseFixture.CreateContext();
            using var ikinciBaglanti = DatabaseFixture.CreateContext();

            var birincininKopyasi = await birinciBaglanti.Etkinlikler.FirstAsync(e => e.Id == etkinlikId);
            var ikincininKopyasi = await ikinciBaglanti.Etkinlikler.FirstAsync(e => e.Id == etkinlikId);

            birincininKopyasi.Ad = "Birinci yöneticinin adı";
            await birinciBaglanti.SaveChangesAsync();

            // İkincinin elindeki satır sürümü artık eski; kaydetmesi reddedilmeli.
            ikincininKopyasi.Ad = "İkinci yöneticinin adı";

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
                () => ikinciBaglanti.SaveChangesAsync());

            // Birincinin değişikliği korunmuş olmalı — sessizce ezilmemeli.
            using var kontrol = DatabaseFixture.CreateContext();
            var guncel = await kontrol.Etkinlikler.AsNoTracking().FirstAsync(e => e.Id == etkinlikId);
            Assert.Equal("Birinci yöneticinin adı", guncel.Ad);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task ArayaKimseGirmezse_KayitNormalCalismali()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var etkinlik = await db.Etkinlikler.FirstAsync(e => e.Id == etkinlikId);

            etkinlik.Ad = "Tek yönetici";
            await db.SaveChangesAsync();

            using var kontrol = DatabaseFixture.CreateContext();
            var guncel = await kontrol.Etkinlikler.AsNoTracking().FirstAsync(e => e.Id == etkinlikId);
            Assert.Equal("Tek yönetici", guncel.Ad);
        }
        finally { await Temizle(etkinlikId); }
    }

    [Fact]
    public async Task SatirSurumu_HerGuncellemedeDegismeli()
    {
        var etkinlikId = await EtkinlikOlustur();
        try
        {
            using var db = DatabaseFixture.CreateContext();
            var etkinlik = await db.Etkinlikler.FirstAsync(e => e.Id == etkinlikId);
            var oncekiSurum = etkinlik.SatirSurumu;

            Assert.NotNull(oncekiSurum);

            etkinlik.Aciklama = "Değişti";
            await db.SaveChangesAsync();

            Assert.NotEqual(oncekiSurum, etkinlik.SatirSurumu);
        }
        finally { await Temizle(etkinlikId); }
    }
}

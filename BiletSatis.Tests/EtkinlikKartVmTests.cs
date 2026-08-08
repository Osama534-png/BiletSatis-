using BiletSatis.Web.Models;

namespace BiletSatis.Tests;

// Kartlardaki geri sayım ve "son N bilet" uyarıları bu hesaplamalardan geliyor.
public class EtkinlikKartVmTests
{
    private static EtkinlikKartVm Kart(int gunSonra, int musaitKoltuk = 50) => new()
    {
        Ad = "Test Etkinliği",
        Tarih = DateTime.Now.Date.AddDays(gunSonra).AddHours(20),
        MusaitKoltukSayisi = musaitKoltuk
    };

    [Theory]
    [InlineData(0, "Bugün!")]
    [InlineData(1, "Yarın")]
    [InlineData(3, "3 gün kaldı")]
    [InlineData(45, "45 gün kaldı")]
    [InlineData(-1, "Sona erdi")]
    [InlineData(-10, "Sona erdi")]
    public void GeriSayimMetni_KalanGuneGoreDegisir(int gunSonra, string beklenen)
    {
        Assert.Equal(beklenen, Kart(gunSonra).GeriSayimMetni);
    }

    // Bir hafta ve altı kaldıysa rozet vurgulanır; geçmiş etkinlikler vurgulanmaz.
    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(-1, false)]
    public void YaklasanEtkinlik_SadeceBirHaftaIcindekiler(int gunSonra, bool beklenen)
    {
        Assert.Equal(beklenen, Kart(gunSonra).YaklasanEtkinlik);
    }

    [Theory]
    [InlineData(0, false)]   // tükendi, kıtlık uyarısı değil
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void SonBiletler_OnVeAltindaUyarir(int musait, bool beklenen)
    {
        Assert.Equal(beklenen, Kart(5, musait).SonBiletler);
    }

    [Fact]
    public void Sehir_MekandanTuretilir()
    {
        var kart = new EtkinlikKartVm { Mekan = "Kültürpark Açıkhava, İzmir" };
        Assert.Equal("İzmir", kart.Sehir);
    }
}

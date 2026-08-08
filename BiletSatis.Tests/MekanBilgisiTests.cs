using BiletSatis.Web.Domain;

namespace BiletSatis.Tests;

// Mekan alanında ayrı bir şehir sütunu yok; "Salon, Şehir" metni ayrıştırılıyor.
// Şehir seçici bu ayrıştırmaya dayandığından uç durumlar test ediliyor.
public class MekanBilgisiTests
{
    [Theory]
    [InlineData("Volkswagen Arena, İstanbul", "İstanbul")]
    [InlineData("Oran Açıkhava Sahnesi, Ankara", "Ankara")]
    [InlineData("Zorlu PSM,İstanbul", "İstanbul")]
    [InlineData("Kültürpark  ,  İzmir  ", "İzmir")]
    public void Sehir_VirgulSonrasiniDondurur(string mekan, string beklenen)
    {
        Assert.Equal(beklenen, MekanBilgisi.Sehir(mekan));
    }

    [Theory]
    [InlineData("Harbiye Açıkhava Tiyatrosu")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Sehir_VirgulYoksaBosDondurur(string? mekan)
    {
        Assert.Equal("", MekanBilgisi.Sehir(mekan));
    }

    // Salon adının kendisinde virgül olabilir; şehir son virgülden sonrasıdır.
    [Fact]
    public void Sehir_BirdenFazlaVirguldeSonuncusunuAlir()
    {
        Assert.Equal("İstanbul", MekanBilgisi.Sehir("Zorlu PSM, Turkcell Sahnesi, İstanbul"));
    }

    [Theory]
    [InlineData("Volkswagen Arena, İstanbul", "Volkswagen Arena")]
    [InlineData("Zorlu PSM, Turkcell Sahnesi, İstanbul", "Zorlu PSM, Turkcell Sahnesi")]
    [InlineData("Harbiye Açıkhava Tiyatrosu", "Harbiye Açıkhava Tiyatrosu")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void SalonAdi_SehirKisminiAyirir(string? mekan, string beklenen)
    {
        Assert.Equal(beklenen, MekanBilgisi.SalonAdi(mekan));
    }
}

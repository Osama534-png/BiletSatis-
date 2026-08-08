using BiletSatis.Web.Models;

namespace BiletSatis.Tests;

// Profil sayfasındaki avatar baş harflerden üretiliyor.
public class ProfilVmTests
{
    [Theory]
    [InlineData("Zeynep Yılmaz", "ZY")]
    [InlineData("Ahmet", "A")]
    [InlineData("Ayşe Fatma Kaya", "AK")]      // ilk ve son isim
    [InlineData("  Mehmet   Demir  ", "MD")]   // fazladan boşluklar
    public void BasHarfler_AddanUretilir(string ad, string beklenen)
    {
        var vm = new ProfilVm { Ad = ad, Email = "test@ornek.local" };
        Assert.Equal(beklenen, vm.BasHarfler);
    }

    // Ad boşsa e-postanın ilk harfine düşülür.
    [Fact]
    public void BasHarfler_AdBossaEpostayaDuser()
    {
        var vm = new ProfilVm { Ad = "", Email = "deneme@ornek.local" };
        Assert.Equal("D", vm.BasHarfler);
    }

    [Fact]
    public void BasHarfler_HicbiriYoksaSoruIsareti()
    {
        var vm = new ProfilVm { Ad = "", Email = "" };
        Assert.Equal("?", vm.BasHarfler);
    }
}

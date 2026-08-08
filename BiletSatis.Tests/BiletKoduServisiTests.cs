using BiletSatis.Web.Services.Giris;
using Microsoft.Extensions.Options;

namespace BiletSatis.Tests;

// QR koddaki imza, kapı doğrulamasının tek güvenlik katmanı.
// İmza kırılırsa herkes sahte bilet üretebilir.
public class BiletKoduServisiTests
{
    private static BiletKoduServisi YeniServis(string anahtar = "test-imza-anahtari") =>
        new(Options.Create(new GirisAyarlari { ImzaAnahtari = anahtar }));

    [Fact]
    public void UretilenKod_AyniServisTarafindanCozulebilmeli()
    {
        var servis = YeniServis();

        var kod = servis.KodUret(1399);

        Assert.Equal(1399, servis.BiletIdCoz(kod));
    }

    [Fact]
    public void KodBiletNumarasiVeImzaIcermeli()
    {
        var kod = YeniServis().KodUret(42);

        Assert.StartsWith("42.", kod);
        Assert.True(kod.Length > 3, "İmza kısmı boş olmamalı");
    }

    // En kritik test: imza uydurulamamalı.
    [Theory]
    [InlineData("1399.sahteimza")]
    [InlineData("1399.")]
    [InlineData("1399")]
    [InlineData(".abc123")]
    [InlineData("abc.def")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GecersizKod_Reddedilmeli(string? kod)
    {
        Assert.Null(YeniServis().BiletIdCoz(kod));
    }

    // Numarayı değiştirip imzayı korumak işe yaramamalı; yoksa kapıda
    // sıradaki bilet numaralarını deneyerek başkalarının biletleri yakılabilirdi.
    [Fact]
    public void BaskaBiletinImzasiKullanilamamali()
    {
        var servis = YeniServis();
        var kod = servis.KodUret(100);
        var imza = kod.Split('.')[1];

        Assert.Null(servis.BiletIdCoz($"101.{imza}"));
        Assert.Null(servis.BiletIdCoz($"99.{imza}"));
    }

    // Anahtar sızmadıkça başka bir sunucu geçerli kod üretemez.
    [Fact]
    public void FarkliAnahtarlaUretilenKod_Gecersiz()
    {
        var kod = YeniServis("birinci-anahtar").KodUret(1399);

        Assert.Null(YeniServis("ikinci-anahtar").BiletIdCoz(kod));
    }

    [Fact]
    public void FarkliBiletler_FarkliImzaAlmali()
    {
        var servis = YeniServis();

        var imza1 = servis.KodUret(1).Split('.')[1];
        var imza2 = servis.KodUret(2).Split('.')[1];

        Assert.NotEqual(imza1, imza2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GecersizBiletNumarasi_Cozulmemeli(int biletId)
    {
        var servis = YeniServis();
        var imza = servis.KodUret(1).Split('.')[1];

        Assert.Null(servis.BiletIdCoz($"{biletId}.{imza}"));
    }
}

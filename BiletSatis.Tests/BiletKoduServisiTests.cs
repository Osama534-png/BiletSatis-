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

        var kod = servis.KodUret(1399, 1);
        var cozulen = servis.Coz(kod);

        Assert.NotNull(cozulen);
        Assert.Equal(1399, cozulen.BiletId);
        Assert.Equal(1, cozulen.KodSurumu);
    }

    [Fact]
    public void KodBiletNumarasiSurumVeImzaIcermeli()
    {
        var kod = YeniServis().KodUret(42, 3);

        Assert.StartsWith("42.3.", kod);
        Assert.Equal(3, kod.Split('.').Length);
    }

    // En kritik test: imza uydurulamamalı.
    [Theory]
    [InlineData("1399.sahteimza")]
    [InlineData("1399.1.sahteimza")]
    [InlineData("1399.")]
    [InlineData("1399")]
    [InlineData(".abc123")]
    [InlineData("abc.def")]
    [InlineData("1.2.3.4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void GecersizKod_Reddedilmeli(string? kod)
    {
        Assert.Null(YeniServis().Coz(kod));
    }

    // Numarayı değiştirip imzayı korumak işe yaramamalı; yoksa kapıda
    // sıradaki bilet numaralarını deneyerek başkalarının biletleri yakılabilirdi.
    [Fact]
    public void BaskaBiletinImzasiKullanilamamali()
    {
        var servis = YeniServis();
        var imza = servis.KodUret(100, 1).Split('.')[2];

        Assert.Null(servis.Coz($"101.1.{imza}"));
        Assert.Null(servis.Coz($"99.1.{imza}"));
    }

    // Devrin güvenliği buna dayanıyor: kullanıcı kendi kodundaki sürüm numarasını
    // artırıp yeni sürümün imzasını üretemez.
    [Fact]
    public void SurumDegistirilirse_ImzaTutmamali()
    {
        var servis = YeniServis();
        var imza = servis.KodUret(500, 1).Split('.')[2];

        Assert.Null(servis.Coz($"500.2.{imza}"));
        Assert.Null(servis.Coz($"500.99.{imza}"));
    }

    [Fact]
    public void FarkliSurumler_FarkliImzaAlmali()
    {
        var servis = YeniServis();

        var birinci = servis.KodUret(7, 1).Split('.')[2];
        var ikinci = servis.KodUret(7, 2).Split('.')[2];

        Assert.NotEqual(birinci, ikinci);
    }

    // Sürüm eklenmeden önce gönderilmiş e-postalardaki QR'lar çalışmaya devam etmeli;
    // aksi halde o biletler bir gecede geçersiz olurdu.
    [Fact]
    public void SurumsuzEskiKod_HalaCozulebilmeli()
    {
        var servis = YeniServis();

        // Eski biçim: "id.imza" — sürüm yok, imza yalnızca id üzerinden.
        var eskiKod = EskiBicimdeKodUret(servis, 1399);
        var cozulen = servis.Coz(eskiKod);

        Assert.NotNull(cozulen);
        Assert.Equal(1399, cozulen.BiletId);
        Assert.Equal(1, cozulen.KodSurumu);
    }

    /// <summary>
    /// Sürüm alanı eklenmeden önceki kod biçimini üretir. Servis artık yalnızca yeni
    /// biçimi ürettiği için eski biçim burada elle kuruluyor.
    /// </summary>
    private static string EskiBicimdeKodUret(BiletKoduServisi servis, int biletId)
    {
        var anahtar = System.Text.Encoding.UTF8.GetBytes("test-imza-anahtari");
        var veri = System.Text.Encoding.UTF8.GetBytes($"bilet:{biletId}");
        var hash = System.Security.Cryptography.HMACSHA256.HashData(anahtar, veri);
        var imza = Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();

        return $"{biletId}.{imza}";
    }

    // Anahtar sızmadıkça başka bir sunucu geçerli kod üretemez.
    [Fact]
    public void FarkliAnahtarlaUretilenKod_Gecersiz()
    {
        var kod = YeniServis("birinci-anahtar").KodUret(1399, 1);

        Assert.Null(YeniServis("ikinci-anahtar").Coz(kod));
    }

    [Fact]
    public void FarkliBiletler_FarkliImzaAlmali()
    {
        var servis = YeniServis();

        var imza1 = servis.KodUret(1, 1).Split('.')[2];
        var imza2 = servis.KodUret(2, 1).Split('.')[2];

        Assert.NotEqual(imza1, imza2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GecersizBiletNumarasi_Cozulmemeli(int biletId)
    {
        var servis = YeniServis();
        var imza = servis.KodUret(1, 1).Split('.')[2];

        Assert.Null(servis.Coz($"{biletId}.1.{imza}"));
    }
}

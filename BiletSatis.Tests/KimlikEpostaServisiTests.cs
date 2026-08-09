using BiletSatis.Web.Services.Eposta;

namespace BiletSatis.Tests;

public class KimlikEpostaServisiTests
{
    private sealed class YakalayanGonderici : IEpostaGonderici
    {
        public string? Alici { get; private set; }
        public string? Konu { get; private set; }
        public string? Govde { get; private set; }
        public int Sayac { get; private set; }

        public Task GonderAsync(
            string aliciAdresi, string konu, string htmlGovde,
            IReadOnlyList<GomuluGorsel>? gorseller = null, CancellationToken ct = default)
        {
            Alici = aliciAdresi;
            Konu = konu;
            Govde = htmlGovde;
            Sayac++;
            return Task.CompletedTask;
        }
    }

    private sealed class PatlayanGonderici : IEpostaGonderici
    {
        public Task GonderAsync(
            string aliciAdresi, string konu, string htmlGovde,
            IReadOnlyList<GomuluGorsel>? gorseller = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("SMTP yok");
    }

    [Fact]
    public async Task DogrulamaGonderAsync_AdresiVeAdiGovdeyeKoymali()
    {
        var gonderici = new YakalayanGonderici();
        var servis = new KimlikEpostaServisi(gonderici);

        await servis.DogrulamaGonderAsync("kisi@ornek.test", "Ayşe", "https://site.test/dogrula?jeton=abc");

        Assert.Equal("kisi@ornek.test", gonderici.Alici);
        Assert.Contains("doğrula", gonderici.Konu!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Merhaba Ayşe", gonderici.Govde);
        Assert.Contains("https://site.test/dogrula?jeton=abc", gonderici.Govde);
    }

    [Fact]
    public async Task SifirlamaGonderAsync_SifirlamaAdresiniGovdeyeKoymali()
    {
        var gonderici = new YakalayanGonderici();
        var servis = new KimlikEpostaServisi(gonderici);

        await servis.SifirlamaGonderAsync("kisi@ornek.test", "", "https://site.test/sifirla?jeton=xyz");

        Assert.Contains("Merhaba,", gonderici.Govde);
        Assert.Contains("https://site.test/sifirla?jeton=xyz", gonderici.Govde);

        // İsteği yapmayan kullanıcıya "bir şey yapmanıza gerek yok" güvencesi verilmeli.
        Assert.Contains("şifreniz değişmez", gonderici.Govde!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Gonderici_HataVerirse_IstisnaYukariTasinmali()
    {
        var servis = new KimlikEpostaServisi(new PatlayanGonderici());

        // Servis hatayı yutmaz; yeniden deneme/loglama kararı çağırana aittir.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => servis.DogrulamaGonderAsync("kisi@ornek.test", "Ali", "https://site.test/x"));
    }
}

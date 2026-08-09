namespace BiletSatis.Web.Services.Eposta;

public class KimlikEpostaServisi : IKimlikEpostaServisi
{
    private readonly IEpostaGonderici _gonderici;

    public KimlikEpostaServisi(IEpostaGonderici gonderici)
    {
        _gonderici = gonderici;
    }

    public Task DogrulamaGonderAsync(string alici, string ad, string dogrulamaAdresi, CancellationToken ct = default) =>
        _gonderici.GonderAsync(
            alici,
            "E-posta adresinizi doğrulayın",
            Govde(
                baslik: "Adresinizi doğrulayın",
                selamlama: Selamlama(ad),
                metin: "Hesabınızı kullanmaya başlamak için e-posta adresinizi doğrulamanız gerekiyor. " +
                       "Aşağıdaki düğmeye tıklamanız yeterli.",
                dugmeMetni: "E-postamı doğrula",
                adres: dogrulamaAdresi,
                dipnot: "Bu hesabı siz açmadıysanız bu e-postayı yok sayabilirsiniz."),
            null,
            ct);

    public Task SifirlamaGonderAsync(string alici, string ad, string sifirlamaAdresi, CancellationToken ct = default) =>
        _gonderici.GonderAsync(
            alici,
            "Şifre sıfırlama isteği",
            Govde(
                baslik: "Şifrenizi sıfırlayın",
                selamlama: Selamlama(ad),
                metin: "Şifrenizi sıfırlamak için bir istek aldık. Yeni şifrenizi belirlemek için " +
                       "aşağıdaki düğmeye tıklayın. Bağlantı kısa süre sonra geçersiz olur.",
                dugmeMetni: "Yeni şifre belirle",
                adres: sifirlamaAdresi,
                dipnot: "Bu isteği siz yapmadıysanız hiçbir şey yapmanıza gerek yok; şifreniz değişmez."),
            null,
            ct);

    private static string Selamlama(string ad) =>
        string.IsNullOrWhiteSpace(ad) ? "Merhaba" : $"Merhaba {ad}";

    private static string Govde(
        string baslik, string selamlama, string metin, string dugmeMetni, string adres, string dipnot) =>
        $"""
        <div style="font-family:Segoe UI,Arial,sans-serif;max-width:560px;color:#1b1812">
          <h2 style="color:#1f8a70;margin:0 0 4px">{baslik}</h2>
          <p style="margin:0 0 20px;color:#7a7266">{selamlama},</p>

          <p style="margin:0 0 24px;line-height:1.6;color:#4a453d">{metin}</p>

          <p style="margin:0 0 24px">
            <a href="{adres}"
               style="display:inline-block;background:#ff4d2e;color:#fff;text-decoration:none;
                      padding:12px 24px;border-radius:8px;font-weight:700">{dugmeMetni}</a>
          </p>

          <p style="margin:0 0 8px;color:#7a7266;font-size:13px">
            Düğme çalışmazsa bu adresi tarayıcınıza yapıştırın:
          </p>
          <p style="margin:0;font-size:12px;word-break:break-all;color:#4a453d">{adres}</p>

          <p style="color:#7a7266;font-size:12px;margin-top:24px">{dipnot}</p>
        </div>
        """;
}

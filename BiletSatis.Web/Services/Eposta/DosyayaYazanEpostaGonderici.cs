using Microsoft.Extensions.Options;

namespace BiletSatis.Web.Services.Eposta;

/// <summary>
/// SMTP yapılandırılmadığında kullanılır: e-postayı göndermez, diske .html dosyası
/// olarak yazar. Böylece proje SMTP hesabı olmadan da çalışır ve bildirimlerin
/// içeriği geliştirme sırasında kontrol edilebilir.
/// </summary>
public class DosyayaYazanEpostaGonderici : IEpostaGonderici
{
    private readonly string _klasor;
    private readonly ILogger<DosyayaYazanEpostaGonderici> _logger;

    public DosyayaYazanEpostaGonderici(
        IOptions<EpostaAyarlari> ayarlar,
        IWebHostEnvironment ortam,
        ILogger<DosyayaYazanEpostaGonderici> logger)
    {
        _klasor = Path.Combine(ortam.ContentRootPath, ayarlar.Value.GelistirmeKlasoru);
        _logger = logger;
    }

    public async Task GonderAsync(string aliciAdresi, string konu, string htmlGovde, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_klasor);

        var dosyaAdi = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.html";
        var tamYol = Path.Combine(_klasor, dosyaAdi);

        var icerik = $"""
            <!doctype html>
            <meta charset="utf-8">
            <p><strong>Alıcı:</strong> {aliciAdresi}</p>
            <p><strong>Konu:</strong> {konu}</p>
            <hr>
            {htmlGovde}
            """;

        await File.WriteAllTextAsync(tamYol, icerik, ct);

        _logger.LogInformation(
            "SMTP yapılandırılmadığı için e-posta gönderilmedi, diske yazıldı: {DosyaYolu} (Alici={Alici})",
            tamYol, aliciAdresi);
    }
}

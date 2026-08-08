using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BiletSatis.Web.Services.Eposta;

public class SmtpEpostaGonderici : IEpostaGonderici
{
    private readonly EpostaAyarlari _ayarlar;
    private readonly ILogger<SmtpEpostaGonderici> _logger;

    public SmtpEpostaGonderici(IOptions<EpostaAyarlari> ayarlar, ILogger<SmtpEpostaGonderici> logger)
    {
        _ayarlar = ayarlar.Value;
        _logger = logger;
    }

    public async Task GonderAsync(string aliciAdresi, string konu, string htmlGovde, CancellationToken ct = default)
    {
        var mesaj = new MimeMessage();
        mesaj.From.Add(new MailboxAddress(_ayarlar.GondericiAdi, _ayarlar.GondericiAdresi));
        mesaj.To.Add(MailboxAddress.Parse(aliciAdresi));
        mesaj.Subject = konu;
        mesaj.Body = new BodyBuilder { HtmlBody = htmlGovde }.ToMessageBody();

        using var istemci = new SmtpClient();

        var guvenlik = _ayarlar.SslKullan
            ? SecureSocketOptions.StartTlsWhenAvailable
            : SecureSocketOptions.None;

        await istemci.ConnectAsync(_ayarlar.SmtpSunucu, _ayarlar.SmtpPort, guvenlik, ct);

        if (!string.IsNullOrWhiteSpace(_ayarlar.KullaniciAdi))
        {
            await istemci.AuthenticateAsync(_ayarlar.KullaniciAdi, _ayarlar.Sifre, ct);
        }

        await istemci.SendAsync(mesaj, ct);
        await istemci.DisconnectAsync(quit: true, ct);

        _logger.LogInformation("E-posta gönderildi: Alici={Alici} Konu={Konu}", aliciAdresi, konu);
    }
}

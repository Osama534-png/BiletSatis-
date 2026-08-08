using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Giris;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiletSatis.Web.Services.Eposta;

public class BiletBildirimServisi : IBiletBildirimServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly IEpostaGonderici _gonderici;
    private readonly IQrKodUretici _qr;
    private readonly IBiletKoduServisi _biletKodu;
    private readonly EpostaAyarlari _ayarlar;
    private readonly ILogger<BiletBildirimServisi> _logger;

    private const int TurBasinaAzami = 50;
    private const string QrContentId = "biletqr";

    public BiletBildirimServisi(
        BiletSatisDbContext db,
        IEpostaGonderici gonderici,
        IQrKodUretici qr,
        IBiletKoduServisi biletKodu,
        IOptions<EpostaAyarlari> ayarlar,
        ILogger<BiletBildirimServisi> logger)
    {
        _db = db;
        _gonderici = gonderici;
        _qr = qr;
        _biletKodu = biletKodu;
        _ayarlar = ayarlar.Value;
        _logger = logger;
    }

    public async Task<int> BekleyenBildirimleriGonderAsync(CancellationToken ct = default)
    {
        var bekleyenler = await _db.Biletler
            .Include(b => b.Etkinlik)
            .Where(b => b.Durum == BiletDurumu.Satildi && !b.BildirimGonderildi)
            .OrderBy(b => b.Id)
            .Take(TurBasinaAzami)
            .ToListAsync(ct);

        if (bekleyenler.Count == 0) return 0;

        var kullaniciIdler = bekleyenler
            .Select(b => b.RezerveEdenKullaniciId)
            .Where(id => id != null)
            .Distinct()
            .ToList();

        var kullanicilar = await _db.Users
            .Where(u => kullaniciIdler.Contains(u.Id))
            .Select(u => new { u.Id, u.Ad, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        var gonderilen = 0;

        foreach (var bilet in bekleyenler)
        {
            if (bilet.RezerveEdenKullaniciId == null ||
                !kullanicilar.TryGetValue(bilet.RezerveEdenKullaniciId, out var kullanici) ||
                string.IsNullOrWhiteSpace(kullanici.Email) ||
                bilet.Etkinlik == null)
            {
                // Alıcısı ya da etkinliği bulunamayan bilet için bildirim gönderilemez.
                // İşaretlenmezse her turda sonuçsuz denenirdi.
                bilet.BildirimGonderildi = true;
                _logger.LogWarning("Bilet bildirimi gönderilemedi, kayıt eksik: BiletId={BiletId}", bilet.Id);
                continue;
            }

            // QR, kapıdaki görevlinin okutunca açacağı imzalı doğrulama adresini taşır.
            var imzaliKod = _biletKodu.KodUret(bilet.Id);
            var dogrulamaAdresi = $"{_ayarlar.SiteAdresi.TrimEnd('/')}/Giris/Dogrula?kod={imzaliKod}";
            var qrPng = _qr.PngUret(dogrulamaAdresi);

            var konu = $"Biletin hazır: {bilet.Etkinlik.Ad}";
            var govde = GovdeOlustur(kullanici.Ad, bilet, bilet.Etkinlik, imzaliKod);

            try
            {
                await _gonderici.GonderAsync(
                    kullanici.Email,
                    konu,
                    govde,
                    [new GomuluGorsel(QrContentId, "bilet-qr.png", qrPng, "image/png")],
                    ct);

                bilet.BildirimGonderildi = true;
                gonderilen++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bayrak false kalır; bir sonraki turda tekrar denenir.
                _logger.LogError(ex, "Bilet bildirimi gönderilemedi: BiletId={BiletId} Alici={Alici}",
                    bilet.Id, kullanici.Email);
            }
        }

        await _db.SaveChangesAsync(ct);

        if (gonderilen > 0)
        {
            _logger.LogInformation("{Sayi} bilet bildirimi gönderildi", gonderilen);
        }

        return gonderilen;
    }

    private string GovdeOlustur(string ad, Bilet bilet, Etkinlik etkinlik, string biletKodu)
    {
        var selamlama = string.IsNullOrWhiteSpace(ad) ? "Merhaba" : $"Merhaba {ad}";
        var salon = MekanBilgisi.SalonAdi(etkinlik.Mekan);
        var sehir = MekanBilgisi.Sehir(etkinlik.Mekan);
        var mekanSatiri = string.IsNullOrEmpty(sehir) ? salon : $"{salon}, {sehir}";
        var biletlerimLinki = $"{_ayarlar.SiteAdresi.TrimEnd('/')}/Biletler/Biletlerim";

        var yasSatiri = etkinlik.YasSiniri > 0
            ? $"<li>Etkinlik <strong>{etkinlik.YasSiniri} yaş ve üzeri</strong> katılımcılar içindir; girişte kimlik sorulabilir.</li>"
            : "<li>Etkinlik her yaştan katılımcıya uygundur.</li>";

        var aciklamaBolumu = string.IsNullOrWhiteSpace(etkinlik.Aciklama)
            ? ""
            : $"""
               <h3 style="font-size:15px;margin:24px 0 8px">Etkinlik hakkında</h3>
               <p style="margin:0;line-height:1.6;color:#4a453d">{etkinlik.Aciklama}</p>
               """;

        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:560px;color:#1b1812">
              <h2 style="color:#1f8a70;margin:0 0 4px">Biletin hazır!</h2>
              <p style="margin:0 0 20px;color:#7a7266">{selamlama}, ödemen başarıyla tamamlandı.</p>

              <div style="border:1px solid #eae1cf;border-radius:12px;overflow:hidden">
                <div style="background:#17140f;color:#fff;padding:16px 20px">
                  <div style="font-size:18px;font-weight:700">{etkinlik.Ad}</div>
                  <div style="font-size:13px;color:rgba(255,255,255,0.7);margin-top:4px">
                    {etkinlik.Tarih:dd MMMM yyyy, dddd} · {etkinlik.Tarih:HH:mm}
                  </div>
                </div>

                <table style="width:100%;border-collapse:collapse;font-size:14px">
                  <tr>
                    <td style="padding:12px 20px;color:#7a7266;border-bottom:1px solid #f3ecdc">Mekan</td>
                    <td style="padding:12px 20px;text-align:right;border-bottom:1px solid #f3ecdc">{mekanSatiri}</td>
                  </tr>
                  <tr>
                    <td style="padding:12px 20px;color:#7a7266;border-bottom:1px solid #f3ecdc">Koltuk</td>
                    <td style="padding:12px 20px;text-align:right;font-weight:700;border-bottom:1px solid #f3ecdc">{bilet.KoltukNo}</td>
                  </tr>
                  <tr>
                    <td style="padding:12px 20px;color:#7a7266;border-bottom:1px solid #f3ecdc">Ödenen tutar</td>
                    <td style="padding:12px 20px;text-align:right;font-weight:700;border-bottom:1px solid #f3ecdc">{bilet.Fiyat:N0} ₺</td>
                  </tr>
                  <tr>
                    <td style="padding:12px 20px;color:#7a7266">Bilet kodu</td>
                    <td style="padding:12px 20px;text-align:right;font-family:Consolas,monospace;font-size:13px">{biletKodu}</td>
                  </tr>
                </table>

                <div style="text-align:center;padding:20px;background:#faf6ee;border-top:1px solid #eae1cf">
                  <img src="cid:{QrContentId}" alt="Bilet QR kodu" width="180" height="180"
                       style="display:block;margin:0 auto 10px" />
                  <div style="font-size:13px;color:#7a7266">Girişte bu kodu okutun</div>
                </div>
              </div>

              {aciklamaBolumu}

              <h3 style="font-size:15px;margin:24px 0 8px">Bilmeniz gerekenler</h3>
              <ul style="margin:0;padding-left:20px;line-height:1.8;color:#4a453d;font-size:14px">
                {yasSatiri}
                <li>Kapılar etkinlikten 1 saat önce açılır; geç kalanlar uygun bir arada içeri alınır.</li>
                <li>Biletiniz numaralı koltuk içerir; lütfen kendi koltuğunuzda oturun.</li>
                <li>Profesyonel fotoğraf ve video ekipmanı ile girişe izin verilmez.</li>
                <li>Yiyecek, içecek ve kesici alet ile girişe izin verilmez.</li>
                <li>Bu bilet tek kişiliktir ve devredilemez.</li>
              </ul>

              <p style="margin:24px 0 0">
                <a href="{biletlerimLinki}"
                   style="display:inline-block;background:#ff4d2e;color:#fff;text-decoration:none;
                          padding:12px 24px;border-radius:8px;font-weight:700">Biletlerimi görüntüle</a>
              </p>

              <p style="color:#7a7266;font-size:12px;margin-top:24px">
                Bu e-posta BiletSatış tarafından otomatik gönderilmiştir. Biletinizi girişte
                telefonunuzdan gösterebilir ya da çıktısını alabilirsiniz.
              </p>
            </div>
            """;
    }
}

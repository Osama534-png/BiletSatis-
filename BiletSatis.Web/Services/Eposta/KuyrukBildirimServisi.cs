using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiletSatis.Web.Services.Eposta;

public class KuyrukBildirimServisi : IKuyrukBildirimServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly IEpostaGonderici _gonderici;
    private readonly EpostaAyarlari _ayarlar;
    private readonly ILogger<KuyrukBildirimServisi> _logger;

    /// <summary>Tek turda gönderilecek azami bildirim; bir tur uzun sürmesin.</summary>
    private const int TurBasinaAzami = 50;

    public KuyrukBildirimServisi(
        BiletSatisDbContext db,
        IEpostaGonderici gonderici,
        IOptions<EpostaAyarlari> ayarlar,
        ILogger<KuyrukBildirimServisi> logger)
    {
        _db = db;
        _gonderici = gonderici;
        _ayarlar = ayarlar.Value;
        _logger = logger;
    }

    public async Task<int> BekleyenBildirimleriGonderAsync(CancellationToken ct = default)
    {
        var bekleyenler = await _db.RezervasyonKuyrugu
            .Where(k => k.Durum == KuyrukDurumu.HakTanindi && !k.BildirimGonderildi)
            .OrderBy(k => k.SiraNo)
            .Take(TurBasinaAzami)
            .ToListAsync(ct);

        if (bekleyenler.Count == 0) return 0;

        // Kullanıcı e-postaları ve etkinlik adları tek seferde çekilir.
        var kullaniciIdler = bekleyenler.Select(k => k.KullaniciId).Distinct().ToList();
        var etkinlikIdler = bekleyenler.Select(k => k.EtkinlikId).Distinct().ToList();

        var kullanicilar = await _db.Users
            .Where(u => kullaniciIdler.Contains(u.Id))
            .Select(u => new { u.Id, u.Ad, u.Email })
            .ToDictionaryAsync(u => u.Id, ct);

        var etkinlikler = await _db.Etkinlikler
            .Where(e => etkinlikIdler.Contains(e.Id))
            .Select(e => new { e.Id, e.Ad, e.Mekan, e.Tarih })
            .ToDictionaryAsync(e => e.Id, ct);

        var gonderilen = 0;

        foreach (var kayit in bekleyenler)
        {
            if (!kullanicilar.TryGetValue(kayit.KullaniciId, out var kullanici) ||
                string.IsNullOrWhiteSpace(kullanici.Email))
            {
                // Kullanıcı silinmiş ya da e-postası yoksa bildirim gönderilemez.
                // Kaydı işaretle, aksi halde her turda tekrar denenir.
                kayit.BildirimGonderildi = true;
                _logger.LogWarning("Kuyruk bildirimi gönderilemedi, kullanıcı bulunamadı: SiraNo={SiraNo} KullaniciId={KullaniciId}",
                    kayit.SiraNo, kayit.KullaniciId);
                continue;
            }

            if (!etkinlikler.TryGetValue(kayit.EtkinlikId, out var etkinlik))
            {
                kayit.BildirimGonderildi = true;
                _logger.LogWarning("Kuyruk bildirimi gönderilemedi, etkinlik bulunamadı: SiraNo={SiraNo} EtkinlikId={EtkinlikId}",
                    kayit.SiraNo, kayit.EtkinlikId);
                continue;
            }

            var konu = $"Sıran geldi: {etkinlik.Ad}";
            var biletLinki = $"{_ayarlar.SiteAdresi.TrimEnd('/')}/Biletler?etkinlikId={kayit.EtkinlikId}";
            var govde = GovdeOlustur(
                kullanici.Ad,
                etkinlik.Ad,
                etkinlik.Mekan,
                etkinlik.Tarih,
                kayit.HakBitisZamani,
                biletLinki);

            try
            {
                await _gonderici.GonderAsync(kullanici.Email, konu, govde, gorseller: null, ct);
                kayit.BildirimGonderildi = true;
                gonderilen++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bayrak false kalır; bir sonraki turda tekrar denenir.
                _logger.LogError(ex, "Kuyruk bildirimi gönderilemedi: SiraNo={SiraNo} Alici={Alici}",
                    kayit.SiraNo, kullanici.Email);
            }
        }

        await _db.SaveChangesAsync(ct);

        if (gonderilen > 0)
        {
            _logger.LogInformation("{Sayi} kuyruk bildirimi gönderildi", gonderilen);
        }

        return gonderilen;
    }

    private static string GovdeOlustur(
        string ad,
        string etkinlikAdi,
        string mekan,
        DateTime etkinlikTarihi,
        DateTime? hakBitisZamani,
        string biletLinki)
    {
        var sonZaman = hakBitisZamani?.ToLocalTime().ToString("HH:mm") ?? "kısa süre";
        var selamlama = string.IsNullOrWhiteSpace(ad) ? "Merhaba" : $"Merhaba {ad}";

        return $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:520px;color:#1b1812">
              <h2 style="color:#d93a1e;margin:0 0 12px">Sıran geldi!</h2>
              <p>{selamlama},</p>
              <p><strong>{etkinlikAdi}</strong> etkinliği için bilet alma hakkın açıldı.</p>
              <table style="font-size:14px;margin:16px 0">
                <tr><td style="padding:2px 12px 2px 0;color:#7a7266">Etkinlik</td><td>{etkinlikAdi}</td></tr>
                <tr><td style="padding:2px 12px 2px 0;color:#7a7266">Tarih</td><td>{etkinlikTarihi:dd.MM.yyyy HH:mm}</td></tr>
                <tr><td style="padding:2px 12px 2px 0;color:#7a7266">Mekan</td><td>{mekan}</td></tr>
              </table>
              <p style="background:#fbe2db;border-left:4px solid #c23b23;padding:10px 14px">
                Hakkın <strong>{sonZaman}</strong> saatine kadar geçerli. Bu süre içinde bilet almazsan
                hakkın sıradaki kişiye devredilir.
              </p>
              <p><a href="{biletLinki}"
                    style="display:inline-block;background:#ff4d2e;color:#fff;text-decoration:none;
                           padding:10px 20px;border-radius:8px;font-weight:700">Biletini seç</a></p>
              <p style="color:#7a7266;font-size:12px">Bu e-posta BiletSatış tarafından otomatik gönderilmiştir.</p>
            </div>
            """;
    }
}

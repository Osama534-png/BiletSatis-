using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly BiletSatisDbContext _db;
    private readonly IKuyrukServisi _kuyruk;
    private readonly IWebHostEnvironment _env;

    /// <summary>Yüklenen afişlerin kaydedileceği klasör (wwwroot altında).</summary>
    private const string AfisKlasoru = "img/afis/yuklenen";

    private static readonly string[] IzinliUzantilar = [".jpg", ".jpeg", ".png", ".webp"];
    private const long AzamiAfisBoyutu = 4 * 1024 * 1024;

    public AdminController(BiletSatisDbContext db, IKuyrukServisi kuyruk, IWebHostEnvironment env)
    {
        _db = db;
        _kuyruk = kuyruk;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var etkinlikler = await _db.Etkinlikler
            .Select(e => new AdminEtkinlikOzeti
            {
                EtkinlikId = e.Id,
                Ad = e.Ad,
                SatistaSayisi = e.Biletler.Count(b => b.Durum == BiletDurumu.Satista),
                SepetteSayisi = e.Biletler.Count(b => b.Durum == BiletDurumu.Sepette),
                SatildiSayisi = e.Biletler.Count(b => b.Durum == BiletDurumu.Satildi),
                KuyrukBeklemede = _db.RezervasyonKuyrugu.Count(k => k.EtkinlikId == e.Id && k.Durum == KuyrukDurumu.Beklemede),
                KuyrukHakTanindi = _db.RezervasyonKuyrugu.Count(k => k.EtkinlikId == e.Id && k.Durum == KuyrukDurumu.HakTanindi)
            })
            .ToListAsync();

        return View(etkinlikler);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SatisiBaslat(int etkinlikId, int n)
    {
        var atanan = await _kuyruk.AllocateWaitlistBatchAsync(etkinlikId, n);
        TempData["Bilgi"] = $"{atanan} kullanıcıya satın alma hakkı tanındı.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult EtkinlikEkle() => View(new EtkinlikEkleViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> EtkinlikEkle(EtkinlikEkleViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var afisUrl = "";
        if (model.AfisDosyasi is { Length: > 0 })
        {
            var (url, hata) = await AfisKaydetAsync(model.AfisDosyasi);
            if (hata != null)
            {
                ModelState.AddModelError(nameof(model.AfisDosyasi), hata);
                return View(model);
            }
            afisUrl = url!;
        }

        _db.Etkinlikler.Add(new Etkinlik
        {
            Ad = model.Ad,
            Mekan = model.Mekan,
            Kategori = model.Kategori,
            AfisUrl = afisUrl,
            Tarih = model.Tarih
        });
        await _db.SaveChangesAsync();

        TempData["Bilgi"] = $"'{model.Ad}' etkinliği oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Yüklenen afişi doğrulayıp wwwroot altına kaydeder ve web yolunu döner.
    /// Uzantı listesi, boyut ve dosya imzası (magic bytes) kontrol edilir;
    /// dosya adı istemciden alınmaz, sunucuda üretilir.
    /// </summary>
    private async Task<(string? Url, string? Hata)> AfisKaydetAsync(IFormFile dosya)
    {
        var uzanti = Path.GetExtension(dosya.FileName).ToLowerInvariant();
        if (!IzinliUzantilar.Contains(uzanti))
            return (null, "Sadece JPG, PNG veya WEBP dosyası yükleyebilirsiniz.");

        if (dosya.Length > AzamiAfisBoyutu)
            return (null, "Dosya boyutu en fazla 4 MB olabilir.");

        await using var kaynak = dosya.OpenReadStream();
        var baslik = new byte[12];
        var okunan = await kaynak.ReadAsync(baslik);
        if (okunan < 12 || !GercektenGorselMi(baslik))
            return (null, "Dosya geçerli bir görsel değil.");

        kaynak.Position = 0;

        var klasor = Path.Combine(_env.WebRootPath, AfisKlasoru.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(klasor);

        var dosyaAdi = $"{Guid.NewGuid():N}{uzanti}";
        await using (var hedef = System.IO.File.Create(Path.Combine(klasor, dosyaAdi)))
        {
            await kaynak.CopyToAsync(hedef);
        }

        return ($"/{AfisKlasoru}/{dosyaAdi}", null);
    }

    /// <summary>İçeriğin gerçekten JPEG/PNG/WEBP olup olmadığını dosya imzasından doğrular.</summary>
    private static bool GercektenGorselMi(ReadOnlySpan<byte> baslik)
    {
        // JPEG: FF D8 FF
        if (baslik[0] == 0xFF && baslik[1] == 0xD8 && baslik[2] == 0xFF) return true;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (baslik[..8].SequenceEqual(png)) return true;

        // WEBP: "RIFF" .... "WEBP"
        ReadOnlySpan<byte> riff = "RIFF"u8;
        ReadOnlySpan<byte> webp = "WEBP"u8;
        if (baslik[..4].SequenceEqual(riff) && baslik[8..12].SequenceEqual(webp)) return true;

        return false;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BiletEkle(int etkinlikId, string koltukOnEki, int adet, decimal fiyat)
    {
        if (adet < 1 || adet > 1000 || string.IsNullOrWhiteSpace(koltukOnEki) || fiyat <= 0)
        {
            TempData["Hata"] = "Geçersiz bilet ekleme bilgisi.";
            return RedirectToAction(nameof(Index));
        }

        var mevcutSayisi = await _db.Biletler
            .CountAsync(b => b.EtkinlikId == etkinlikId && b.KoltukNo.StartsWith(koltukOnEki + "-"));

        for (var i = 1; i <= adet; i++)
        {
            _db.Biletler.Add(new Bilet
            {
                EtkinlikId = etkinlikId,
                KoltukNo = $"{koltukOnEki}-{(mevcutSayisi + i):00}",
                Fiyat = fiyat,
                Durum = BiletDurumu.Satista
            });
        }
        await _db.SaveChangesAsync();

        TempData["Bilgi"] = $"{adet} yeni bilet eklendi.";
        return RedirectToAction(nameof(Index));
    }

    // Yük testleri (k6) için tanılama endpoint'i — DB'ye doğrudan bağlanmadan durum özetini döner.
    [HttpGet]
    public async Task<IActionResult> Ozet(int etkinlikId)
    {
        var biletler = await _db.Biletler.AsNoTracking().Where(b => b.EtkinlikId == etkinlikId).ToListAsync();
        var kuyruk = await _db.RezervasyonKuyrugu.AsNoTracking().Where(k => k.EtkinlikId == etkinlikId).ToListAsync();

        return Json(new
        {
            toplamBiletSayisi = biletler.Count,
            satistaSayisi = biletler.Count(b => b.Durum == BiletDurumu.Satista),
            sepetteSayisi = biletler.Count(b => b.Durum == BiletDurumu.Sepette),
            satildiSayisi = biletler.Count(b => b.Durum == BiletDurumu.Satildi),
            biletDurumlari = biletler.Select(b => new { b.Id, Durum = b.Durum.ToString() }),
            kuyrukSiraNolari = kuyruk.Select(k => new { k.SiraNo, Durum = k.Durum.ToString() }).OrderBy(k => k.SiraNo)
        });
    }
}

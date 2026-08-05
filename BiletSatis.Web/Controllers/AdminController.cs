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

    public AdminController(BiletSatisDbContext db, IKuyrukServisi kuyruk)
    {
        _db = db;
        _kuyruk = kuyruk;
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
    public async Task<IActionResult> EtkinlikEkle(EtkinlikEkleViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        _db.Etkinlikler.Add(new Etkinlik { Ad = model.Ad, Tarih = model.Tarih });
        await _db.SaveChangesAsync();

        TempData["Bilgi"] = $"'{model.Ad}' etkinliği oluşturuldu.";
        return RedirectToAction(nameof(Index));
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

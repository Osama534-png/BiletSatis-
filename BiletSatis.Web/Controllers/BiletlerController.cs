using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Controllers;

public class BiletlerController : Controller
{
    private readonly BiletSatisDbContext _db;
    private readonly IBiletRezervasyonServisi _rezervasyon;
    private readonly IKuyrukServisi _kuyruk;
    private readonly ICurrentUserService _currentUser;

    public BiletlerController(BiletSatisDbContext db, IBiletRezervasyonServisi rezervasyon, IKuyrukServisi kuyruk, ICurrentUserService currentUser)
    {
        _db = db;
        _rezervasyon = rezervasyon;
        _kuyruk = kuyruk;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(int etkinlikId)
    {
        var etkinlik = await _db.Etkinlikler
            .Include(e => e.Biletler)
            .FirstOrDefaultAsync(e => e.Id == etkinlikId);

        if (etkinlik == null) return NotFound();

        return View(etkinlik);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SepeteEkle(int biletId)
    {
        var bilet = await _db.Biletler.AsNoTracking().FirstOrDefaultAsync(b => b.Id == biletId);
        if (bilet == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();
        var sonuc = await _rezervasyon.TryAddToCartAsync(biletId, kullaniciId);

        if (sonuc == SepeteEklemeSonucu.Basarili)
        {
            return RedirectToAction(nameof(OdemeStub), new { biletId });
        }

        TempData["Hata"] = "Bu bilet az önce başka bir kullanıcı tarafından sepete eklendi.";
        return RedirectToAction(nameof(Index), new { etkinlikId = bilet.EtkinlikId });
    }

    public async Task<IActionResult> Sepetim()
    {
        var kullaniciId = _currentUser.GetKullaniciId();

        var biletler = await _db.Biletler
            .AsNoTracking()
            .Include(b => b.Etkinlik)
            .Where(b => b.RezerveEdenKullaniciId == kullaniciId && b.Durum == BiletDurumu.Sepette)
            .OrderBy(b => b.KilitBitisZamani)
            .ToListAsync();

        return View(biletler);
    }

    public async Task<IActionResult> OdemeStub(int biletId)
    {
        var bilet = await _db.Biletler.AsNoTracking().FirstOrDefaultAsync(b => b.Id == biletId);
        if (bilet == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();
        if (bilet.Durum != BiletDurumu.Sepette || bilet.RezerveEdenKullaniciId != kullaniciId)
        {
            TempData["Hata"] = "Bu bilet üzerinde bir rezervasyonunuz yok.";
            return RedirectToAction(nameof(Index), new { etkinlikId = bilet.EtkinlikId });
        }

        return View(bilet);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OdemeyiTamamla(int biletId)
    {
        var bilet = await _db.Biletler.AsNoTracking().FirstOrDefaultAsync(b => b.Id == biletId);
        if (bilet == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();
        var basarili = await _rezervasyon.CompletePaymentAsync(biletId, kullaniciId);

        if (basarili)
        {
            var haktanindi = await _db.RezervasyonKuyrugu
                .AsNoTracking()
                .Where(k => k.EtkinlikId == bilet.EtkinlikId && k.KullaniciId == kullaniciId && k.Durum == KuyrukDurumu.HakTanindi)
                .OrderByDescending(k => k.SiraNo)
                .FirstOrDefaultAsync();

            if (haktanindi != null)
            {
                await _kuyruk.CompleteQueueEntryAsync(haktanindi.SiraNo, kullaniciId);
            }
        }

        TempData[basarili ? "Bilgi" : "Hata"] = basarili
            ? "Ödeme tamamlandı, biletiniz size ait."
            : "Ödeme tamamlanamadı — rezervasyon süresi dolmuş olabilir.";

        return RedirectToAction(nameof(Index), new { etkinlikId = bilet.EtkinlikId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IptalEt(int biletId)
    {
        var bilet = await _db.Biletler.AsNoTracking().FirstOrDefaultAsync(b => b.Id == biletId);
        if (bilet == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();
        await _rezervasyon.CancelReservationAsync(biletId, kullaniciId);

        return RedirectToAction(nameof(Index), new { etkinlikId = bilet.EtkinlikId });
    }
}

using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Controllers;

public class KuyrukController : Controller
{
    private readonly BiletSatisDbContext _db;
    private readonly IKuyrukServisi _kuyruk;
    private readonly ICurrentUserService _currentUser;

    public KuyrukController(BiletSatisDbContext db, IKuyrukServisi kuyruk, ICurrentUserService currentUser)
    {
        _db = db;
        _kuyruk = kuyruk;
        _currentUser = currentUser;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Katil(int etkinlikId)
    {
        var etkinlik = await _db.Etkinlikler.FindAsync(etkinlikId);
        if (etkinlik == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();

        // "Zaten sırada mı" kontrolü servisin içinde, ekleme ile aynı SQL deyiminde
        // yapılıyor. Burada önce sorup sonra eklemek, iki isteğin aynı anda gelmesi
        // hâlinde aynı kullanıcıya iki sıra numarası verilmesine yol açıyordu.
        await _kuyruk.EnqueueWaitlistAsync(etkinlikId, kullaniciId);

        return RedirectToAction(nameof(Durum), new { etkinlikId });
    }

    public async Task<IActionResult> Durum(int etkinlikId)
    {
        var etkinlik = await _db.Etkinlikler.FindAsync(etkinlikId);
        if (etkinlik == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();

        var kayit = await _db.RezervasyonKuyrugu
            .AsNoTracking()
            .Where(k => k.EtkinlikId == etkinlikId && k.KullaniciId == kullaniciId)
            .OrderByDescending(k => k.SiraNo)
            .FirstOrDefaultAsync();

        ViewBag.Etkinlik = etkinlik;
        return View(kayit);
    }
}

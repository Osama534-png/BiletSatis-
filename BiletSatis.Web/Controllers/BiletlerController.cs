using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace BiletSatis.Web.Controllers;

public class BiletlerController : Controller
{
    private readonly BiletSatisDbContext _db;
    private readonly IBiletRezervasyonServisi _rezervasyon;
    private readonly IKuyrukServisi _kuyruk;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<BiletlerController> _logger;

    public BiletlerController(BiletSatisDbContext db, IBiletRezervasyonServisi rezervasyon, IKuyrukServisi kuyruk, ICurrentUserService currentUser, ILogger<BiletlerController> logger)
    {
        _db = db;
        _rezervasyon = rezervasyon;
        _kuyruk = kuyruk;
        _currentUser = currentUser;
        _logger = logger;
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

    public async Task<IActionResult> Biletlerim()
    {
        var kullaniciId = _currentUser.GetKullaniciId();

        var biletler = await _db.Biletler
            .AsNoTracking()
            .Include(b => b.Etkinlik)
            .Where(b => b.RezerveEdenKullaniciId == kullaniciId && b.Durum == BiletDurumu.Satildi)
            .OrderByDescending(b => b.Id)
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
    public async Task<IActionResult> OdemeBaslat(int biletId)
    {
        var bilet = await _db.Biletler.AsNoTracking().Include(b => b.Etkinlik).FirstOrDefaultAsync(b => b.Id == biletId);
        if (bilet == null) return NotFound();

        var kullaniciId = _currentUser.GetKullaniciId();
        if (bilet.Durum != BiletDurumu.Sepette || bilet.RezerveEdenKullaniciId != kullaniciId)
        {
            TempData["Hata"] = "Bu bilet üzerinde bir rezervasyonunuz yok.";
            return RedirectToAction(nameof(Index), new { etkinlikId = bilet.EtkinlikId });
        }

        var domain = $"{Request.Scheme}://{Request.Host}";
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = "try",
                        UnitAmount = (long)(bilet.Fiyat * 100),
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = $"{bilet.Etkinlik?.Ad} — Koltuk {bilet.KoltukNo}",
                        },
                    },
                    Quantity = 1,
                },
            },
            Mode = "payment",
            SuccessUrl = $"{domain}/Biletler/OdemeBasarili?session_id={{CHECKOUT_SESSION_ID}}&biletId={biletId}",
            CancelUrl = $"{domain}/Biletler/OdemeStub?biletId={biletId}",
            Metadata = new Dictionary<string, string>
            {
                { "BiletId", biletId.ToString() },
                { "KullaniciId", kullaniciId },
            },
        };

        try
        {
            var service = new SessionService();
            var session = await service.CreateAsync(options);
            return Redirect(session.Url);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe ödeme oturumu oluşturulamadı: BiletId={BiletId}", biletId);
            TempData["Hata"] = "Ödeme sayfası açılamadı, lütfen tekrar deneyin.";
            return RedirectToAction(nameof(OdemeStub), new { biletId });
        }
    }

    [HttpGet]
    public async Task<IActionResult> OdemeBasarili(string session_id, int biletId)
    {
        var bilet = await _db.Biletler.AsNoTracking().FirstOrDefaultAsync(b => b.Id == biletId);
        if (bilet == null) return NotFound();

        Stripe.Checkout.Session session;
        try
        {
            var sessionService = new SessionService();
            session = await sessionService.GetAsync(session_id);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe session doğrulanamadı: SessionId={SessionId} BiletId={BiletId}", session_id, biletId);
            TempData["Hata"] = "Ödeme doğrulanamadı.";
            return RedirectToAction(nameof(OdemeStub), new { biletId });
        }

        var kullaniciId = _currentUser.GetKullaniciId();

        var metadataUyusuyor = session.Metadata.TryGetValue("BiletId", out var metaBiletId) && metaBiletId == biletId.ToString()
            && session.Metadata.TryGetValue("KullaniciId", out var metaKullaniciId) && metaKullaniciId == kullaniciId;

        if (session.PaymentStatus != "paid" || !metadataUyusuyor)
        {
            TempData["Hata"] = "Ödeme tamamlanamadı.";
            return RedirectToAction(nameof(OdemeStub), new { biletId });
        }

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

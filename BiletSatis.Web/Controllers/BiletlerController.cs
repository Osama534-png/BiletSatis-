using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace BiletSatis.Web.Controllers;

public class BiletlerController : Controller
{
    /// <summary>Tek seferde seçilebilecek en fazla koltuk sayısı.</summary>
    public const int MaxKoltuk = 6;

    /// <summary>
    /// Ödeme sırasında kilidin uzatıldığı süre. Kullanıcı Stripe sayfasındayken
    /// 5 dakikalık normal kilit dolarsa koltuk başkasına satılabilirdi.
    /// </summary>
    private const int OdemeKilidiDakika = 15;

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
            .AsNoTracking()
            .Include(e => e.Biletler)
            .FirstOrDefaultAsync(e => e.Id == etkinlikId);

        if (etkinlik == null) return NotFound();

        return View(KoltukHaritasiOlustur(etkinlik));
    }

    /// <summary>
    /// Biletleri koltuk numarasının önekine göre bloklara ayırır ("A-01" → A blok).
    /// Kategori sırası fiyata göre belirlenir: en pahalı blok 1. Kategori ve sahneye en yakın.
    /// </summary>
    private static KoltukHaritasiVm KoltukHaritasiOlustur(Etkinlik etkinlik)
    {
        var bloklar = etkinlik.Biletler
            .GroupBy(b => BlokKodu(b.KoltukNo))
            .Select(g => new BlokVm
            {
                Kod = g.Key,
                Ad = $"{g.Key} Blok",
                EnDusukFiyat = g.Min(b => b.Fiyat),
                ToplamKoltuk = g.Count(),
                MusaitKoltuk = g.Count(b => b.Durum == BiletDurumu.Satista),
                Koltuklar = g
                    .OrderBy(b => b.KoltukNo, StringComparer.OrdinalIgnoreCase)
                    .Select(b => new KoltukVm
                    {
                        BiletId = b.Id,
                        KoltukNo = b.KoltukNo,
                        KisaNo = KisaKoltukNo(b.KoltukNo),
                        Fiyat = b.Fiyat,
                        Durum = b.Durum
                    })
                    .ToList()
            })
            .OrderBy(b => b.Kod, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fiyatSirasi = bloklar
            .Select(b => b.EnDusukFiyat)
            .Distinct()
            .OrderByDescending(f => f)
            .ToList();

        foreach (var blok in bloklar)
        {
            blok.Kategori = $"{fiyatSirasi.IndexOf(blok.EnDusukFiyat) + 1}. Kategori";
            blok.OnSira = blok.EnDusukFiyat == fiyatSirasi.FirstOrDefault();
        }

        return new KoltukHaritasiVm
        {
            EtkinlikId = etkinlik.Id,
            EtkinlikAdi = etkinlik.Ad,
            Mekan = etkinlik.Mekan,
            AfisUrl = etkinlik.AfisUrl,
            Tarih = etkinlik.Tarih,
            Bloklar = bloklar
        };
    }

    private static string BlokKodu(string koltukNo)
    {
        var ayirac = koltukNo.IndexOf('-');
        var kod = ayirac > 0 ? koltukNo[..ayirac] : koltukNo;
        return kod.Trim().ToUpperInvariant();
    }

    private static string KisaKoltukNo(string koltukNo)
    {
        var ayirac = koltukNo.IndexOf('-');
        return ayirac > 0 && ayirac < koltukNo.Length - 1
            ? koltukNo[(ayirac + 1)..]
            : koltukNo;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SepeteEkle(int etkinlikId, int[] biletIds)
    {
        var idler = (biletIds ?? Array.Empty<int>()).Distinct().ToArray();

        if (idler.Length == 0)
        {
            TempData["Hata"] = "Önce salon haritasından koltuk seçin.";
            return RedirectToAction(nameof(Index), new { etkinlikId });
        }

        if (idler.Length > MaxKoltuk)
        {
            TempData["Hata"] = $"Tek seferde en fazla {MaxKoltuk} koltuk seçebilirsiniz.";
            return RedirectToAction(nameof(Index), new { etkinlikId });
        }

        // Formdan gelen numaralar istemciden geliyor: hepsinin gerçekten bu etkinliğe
        // ait olduğunu doğrulamadan rezervasyon denemesi yapmıyoruz.
        var gecerliSayisi = await _db.Biletler
            .AsNoTracking()
            .CountAsync(b => idler.Contains(b.Id) && b.EtkinlikId == etkinlikId);

        if (gecerliSayisi != idler.Length)
        {
            TempData["Hata"] = "Seçim geçersiz — lütfen koltukları yeniden seçin.";
            return RedirectToAction(nameof(Index), new { etkinlikId });
        }

        var kullaniciId = _currentUser.GetKullaniciId();
        var sonuc = await _rezervasyon.TryAddManyToCartAsync(idler, kullaniciId);

        if (sonuc.Basarili)
        {
            return RedirectToAction(nameof(Sepetim));
        }

        TempData["Hata"] = sonuc.AlinamayanKoltuklar.Count > 0
            ? $"{string.Join(", ", sonuc.AlinamayanKoltuklar)} koltuğu az önce başkası tarafından alındı. " +
              "Seçiminizin tamamı iptal edildi — diğer koltuklar hâlâ müsait, yeniden seçebilirsiniz."
            : "Seçtiğiniz koltuklar alınamadı, lütfen tekrar deneyin.";

        return RedirectToAction(nameof(Index), new { etkinlikId });
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SepetiOde()
    {
        var kullaniciId = _currentUser.GetKullaniciId();

        var biletler = await _db.Biletler
            .AsNoTracking()
            .Include(b => b.Etkinlik)
            .Where(b => b.RezerveEdenKullaniciId == kullaniciId && b.Durum == BiletDurumu.Sepette)
            .OrderBy(b => b.KoltukNo)
            .ToListAsync();

        if (biletler.Count == 0)
        {
            TempData["Hata"] = "Sepetinizde ödeme bekleyen bilet yok.";
            return RedirectToAction(nameof(Sepetim));
        }

        var idler = biletler.Select(b => b.Id).ToArray();

        // Stripe sayfasında geçirilen süre normal kilit süresinden uzun olabilir.
        // Kilidi uzatmazsak kullanıcı kartını girerken koltuk başkasına satılabilir
        // ve parası alınmış olmasına rağmen bilet elinden gitmiş olurdu.
        await _rezervasyon.ExtendCartHoldsAsync(idler, kullaniciId, OdemeKilidiDakika);

        var domain = $"{Request.Scheme}://{Request.Host}";
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = biletler.Select(bilet => new SessionLineItemOptions
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
            }).ToList(),
            Mode = "payment",
            SuccessUrl = $"{domain}/Biletler/OdemeBasarili?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl = $"{domain}/Biletler/Sepetim",
            Metadata = new Dictionary<string, string>
            {
                { "BiletIdleri", string.Join(',', idler) },
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
            _logger.LogError(ex, "Stripe ödeme oturumu oluşturulamadı: BiletSayisi={BiletSayisi}", biletler.Count);
            TempData["Hata"] = "Ödeme sayfası açılamadı, lütfen tekrar deneyin.";
            return RedirectToAction(nameof(Sepetim));
        }
    }

    [HttpGet]
    public async Task<IActionResult> OdemeBasarili(string session_id)
    {
        Stripe.Checkout.Session session;
        try
        {
            var sessionService = new SessionService();
            session = await sessionService.GetAsync(session_id);
        }
        catch (Stripe.StripeException ex)
        {
            _logger.LogError(ex, "Stripe session doğrulanamadı: SessionId={SessionId}", session_id);
            TempData["Hata"] = "Ödeme doğrulanamadı.";
            return RedirectToAction(nameof(Sepetim));
        }

        var kullaniciId = _currentUser.GetKullaniciId();

        // Hangi biletlerin ödendiğini istemciden değil, Stripe'ın bize geri verdiği
        // metadata'dan okuyoruz — adres çubuğundaki numara değiştirilerek başka bir
        // bilet "ödenmiş" gösterilemesin.
        var idler = session.Metadata.TryGetValue("BiletIdleri", out var metaIdler)
            ? metaIdler.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray()
            : Array.Empty<int>();

        var sahibiUyusuyor = session.Metadata.TryGetValue("KullaniciId", out var metaKullaniciId)
            && metaKullaniciId == kullaniciId;

        if (session.PaymentStatus != "paid" || !sahibiUyusuyor || idler.Length == 0)
        {
            TempData["Hata"] = "Ödeme tamamlanamadı.";
            return RedirectToAction(nameof(Sepetim));
        }

        var tamamlanan = await _rezervasyon.CompletePaymentManyAsync(idler, kullaniciId);

        if (tamamlanan > 0)
        {
            await KuyrukHaklariniKapatAsync(idler, kullaniciId);
        }

        if (tamamlanan == idler.Length)
        {
            TempData["Bilgi"] = tamamlanan == 1
                ? "Ödeme tamamlandı, biletiniz size ait."
                : $"Ödeme tamamlandı, {tamamlanan} biletiniz size ait.";
        }
        else
        {
            // Para alındı ama biletlerin bir kısmı işaretlenemedi. Otomatik iade akışı
            // henüz yok; bu yüzden en azından iz bırakıyoruz ve kullanıcıyı uyarıyoruz.
            _logger.LogError(
                "Ödeme sonrası eksik bilet: KullaniciId={KullaniciId} SessionId={SessionId} İstenen={Istenen} Tamamlanan={Tamamlanan}",
                kullaniciId, session_id, idler.Length, tamamlanan);

            TempData["Hata"] = $"Ödemeniz alındı ancak {idler.Length - tamamlanan} koltuk için rezervasyon süresi dolmuş. " +
                               "Lütfen bizimle iletişime geçin.";
        }

        return RedirectToAction(nameof(Biletlerim));
    }

    /// <summary>
    /// Satın alma tamamlandığında, bu biletlerin etkinlikleri için kullanıcıya tanınmış
    /// kuyruk haklarını "kullanıldı" olarak kapatır.
    /// </summary>
    private async Task KuyrukHaklariniKapatAsync(int[] biletIdleri, string kullaniciId)
    {
        var etkinlikIdleri = await _db.Biletler
            .AsNoTracking()
            .Where(b => biletIdleri.Contains(b.Id))
            .Select(b => b.EtkinlikId)
            .Distinct()
            .ToListAsync();

        foreach (var etkinlikId in etkinlikIdleri)
        {
            var haktanindi = await _db.RezervasyonKuyrugu
                .AsNoTracking()
                .Where(k => k.EtkinlikId == etkinlikId && k.KullaniciId == kullaniciId && k.Durum == KuyrukDurumu.HakTanindi)
                .OrderByDescending(k => k.SiraNo)
                .FirstOrDefaultAsync();

            if (haktanindi != null)
            {
                await _kuyruk.CompleteQueueEntryAsync(haktanindi.SiraNo, kullaniciId);
            }
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IptalEt(int biletId)
    {
        var kullaniciId = _currentUser.GetKullaniciId();
        await _rezervasyon.CancelReservationAsync(biletId, kullaniciId);

        return RedirectToAction(nameof(Sepetim));
    }
}

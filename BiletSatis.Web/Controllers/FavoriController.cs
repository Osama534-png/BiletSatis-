using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Favoriler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Controllers;

public class FavoriController : Controller
{
    private readonly BiletSatisDbContext _db;
    private readonly IFavoriServisi _favori;
    private readonly ICurrentUserService _currentUser;

    public FavoriController(BiletSatisDbContext db, IFavoriServisi favori, ICurrentUserService currentUser)
    {
        _db = db;
        _favori = favori;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index()
    {
        var kullaniciId = _currentUser.GetKullaniciId();

        var etkinlikler = await _db.Favoriler
            .AsNoTracking()
            .Where(f => f.KullaniciId == kullaniciId)
            .OrderByDescending(f => f.EklenmeZamani)
            .Select(f => new EtkinlikKartVm
            {
                Id = f.Etkinlik!.Id,
                Ad = f.Etkinlik.Ad,
                Mekan = f.Etkinlik.Mekan,
                AfisUrl = f.Etkinlik.AfisUrl,
                Kategori = f.Etkinlik.Kategori,
                Tarih = f.Etkinlik.Tarih,
                MusaitKoltukSayisi = f.Etkinlik.Biletler.Count(b => b.Durum == BiletDurumu.Satista),
                EnDusukFiyat = f.Etkinlik.Biletler
                    .Where(b => b.Durum == BiletDurumu.Satista)
                    .Select(b => (decimal?)b.Fiyat)
                    .Min()
            })
            .ToListAsync();

        return View(etkinlikler);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Degistir(int etkinlikId, string? donusAdresi)
    {
        var kullaniciId = _currentUser.GetKullaniciId();
        var durum = await _favori.DegistirAsync(etkinlikId, kullaniciId);

        // JavaScript'ten geldiyse sayfayı yeniden çizdirmeye gerek yok: tek satırlık
        // bir cevap yeterli. Tam sayfa yenileme, kalbe her basışta ana sayfanın
        // bütün sorgularını (etkinlikler, koltuk sayıları, favori listesi) baştan
        // çalıştırıyordu — yapılan iş ise tek bir veritabanı yazması.
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return Json(new { favoride = durum == FavoriDurumu.Eklendi });
        }

        TempData["Bilgi"] = durum == FavoriDurumu.Eklendi
            ? "Etkinlik favorilerinize eklendi."
            : "Etkinlik favorilerinizden çıkarıldı.";

        // Kullanıcı hangi sayfadan bastıysa oraya dönmeli. Adres yalnızca kendi
        // sitemize aitse kabul ediliyor; aksi halde açık yönlendirme açığı olurdu.
        if (!string.IsNullOrWhiteSpace(donusAdresi) && Url.IsLocalUrl(donusAdresi))
        {
            return Redirect(donusAdresi);
        }

        return RedirectToAction(nameof(Index));
    }
}

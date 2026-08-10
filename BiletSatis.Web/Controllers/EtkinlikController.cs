using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Degerlendirmeler;
using BiletSatis.Web.Services.Favoriler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Controllers;

public class EtkinlikController : Controller
{
    private readonly BiletSatisDbContext _db;
    private readonly IDegerlendirmeServisi _degerlendirme;
    private readonly IFavoriServisi _favori;
    private readonly ICurrentUserService _currentUser;

    public EtkinlikController(
        BiletSatisDbContext db,
        IDegerlendirmeServisi degerlendirme,
        IFavoriServisi favori,
        ICurrentUserService currentUser)
    {
        _db = db;
        _degerlendirme = degerlendirme;
        _favori = favori;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Detay(int id)
    {
        var etkinlik = await _db.Etkinlikler
            .AsNoTracking()
            .Include(e => e.Biletler)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (etkinlik == null) return NotFound();

        var satistakiler = etkinlik.Biletler.Where(b => b.Durum == BiletDurumu.Satista).ToList();

        // Blok bazlı fiyat listesi — koltuk numarası öneki blok kodunu verir (A-01 -> A).
        var fiyatlar = etkinlik.Biletler
            .GroupBy(b => BlokKodu(b.KoltukNo))
            .Select(g => new FiyatSatiriVm
            {
                Blok = g.Key,
                Fiyat = g.Min(b => b.Fiyat),
                MusaitKoltuk = g.Count(b => b.Durum == BiletDurumu.Satista)
            })
            .OrderByDescending(f => f.Fiyat)
            .ToList();

        for (var i = 0; i < fiyatlar.Count; i++)
        {
            fiyatlar[i].Kategori = $"{i + 1}. Kategori";
        }

        var benzerler = await _db.Etkinlikler
            .AsNoTracking()
            .Where(e => e.Kategori == etkinlik.Kategori && e.Id != etkinlik.Id)
            .OrderBy(e => e.Tarih)
            .Take(4)
            .Select(e => new EtkinlikKartVm
            {
                Id = e.Id,
                Ad = e.Ad,
                Mekan = e.Mekan,
                AfisUrl = e.AfisUrl,
                Kategori = e.Kategori,
                Tarih = e.Tarih,
                MusaitKoltukSayisi = e.Biletler.Count(b => b.Durum == BiletDurumu.Satista),
                EnDusukFiyat = e.Biletler
                    .Where(b => b.Durum == BiletDurumu.Satista)
                    .Select(b => (decimal?)b.Fiyat)
                    .Min()
            })
            .ToListAsync();

        var vm = new EtkinlikDetayVm
        {
            Id = etkinlik.Id,
            Ad = etkinlik.Ad,
            Mekan = etkinlik.Mekan,
            AfisUrl = etkinlik.AfisUrl,
            Aciklama = etkinlik.Aciklama,
            YasSiniri = etkinlik.YasSiniri,
            Kategori = etkinlik.Kategori,
            Tarih = etkinlik.Tarih,
            ToplamKoltuk = etkinlik.Biletler.Count,
            MusaitKoltuk = satistakiler.Count,
            EnDusukFiyat = satistakiler.Count == 0 ? null : satistakiler.Min(b => b.Fiyat),
            EnYuksekFiyat = satistakiler.Count == 0 ? null : satistakiler.Max(b => b.Fiyat),
            Fiyatlar = fiyatlar,
            BenzerEtkinlikler = benzerler,
            Degerlendirmeler = await _degerlendirme.OzetAsync(id)
        };

        var kullaniciId = _currentUser.GetKullaniciId();
        vm.DegerlendirebilirMi = await _degerlendirme.DegerlendirebilirMiAsync(id, kullaniciId);
        vm.KendiDegerlendirmesi = await _degerlendirme.KendiDegerlendirmesiAsync(id, kullaniciId);
        vm.Favoride = await _favori.FavorideMiAsync(id, kullaniciId);

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Degerlendir(int etkinlikId, int puan, string? yorum)
    {
        var kullaniciId = _currentUser.GetKullaniciId();
        var sonuc = await _degerlendirme.KaydetAsync(etkinlikId, kullaniciId, puan, yorum);

        switch (sonuc)
        {
            case DegerlendirmeSonucu.Kaydedildi:
                TempData["Bilgi"] = "Değerlendirmeniz için teşekkürler.";
                break;
            case DegerlendirmeSonucu.Guncellendi:
                TempData["Bilgi"] = "Değerlendirmeniz güncellendi.";
                break;
            case DegerlendirmeSonucu.GecersizPuan:
                TempData["Hata"] = "Lütfen 1 ile 5 arasında bir puan seçin.";
                break;
            case DegerlendirmeSonucu.KatilimYok:
                TempData["Hata"] = "Yalnızca etkinliğe katılan misafirler değerlendirme bırakabilir.";
                break;
        }

        return RedirectToAction(nameof(Detay), new { id = etkinlikId });
    }

    private static string BlokKodu(string koltukNo)
    {
        var ayirac = koltukNo.IndexOf('-');
        var kod = ayirac > 0 ? koltukNo[..ayirac] : koltukNo;
        return kod.Trim().ToUpperInvariant();
    }
}

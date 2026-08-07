using System.Diagnostics;
using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BiletSatis.Web.Models;

namespace BiletSatis.Web.Controllers;

public class HomeController : Controller
{
    private readonly BiletSatisDbContext _db;

    public HomeController(BiletSatisDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var etkinlikler = await _db.Etkinlikler
            .OrderBy(e => e.Tarih)
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

        var kuyruktaBekleyen = await _db.RezervasyonKuyrugu
            .CountAsync(k => k.Durum == KuyrukDurumu.Beklemede);

        var vm = new AnaSayfaVm
        {
            Etkinlikler = etkinlikler,
            ToplamEtkinlik = etkinlikler.Count,
            ToplamSatistaBilet = etkinlikler.Sum(e => e.MusaitKoltukSayisi),
            ToplamKuyruktaBekleyen = kuyruktaBekleyen
        };
        return View(vm);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

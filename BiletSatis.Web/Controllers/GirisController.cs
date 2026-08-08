using BiletSatis.Web.Services.Giris;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiletSatis.Web.Controllers;

/// <summary>
/// Kapı kontrolü. Yalnızca yöneticiler erişebilir: sayfa herkese açık olsaydı
/// biletini okutan herkes kendi girişini "kullanıldı" yapabilir ya da başkasının
/// biletini yakabilirdi.
/// </summary>
[Authorize(Roles = "Admin")]
public class GirisController : Controller
{
    private readonly IGirisServisi _giris;

    public GirisController(IGirisServisi giris)
    {
        _giris = giris;
    }

    /// <summary>QR okutulunca açılan sayfa. Bileti değiştirmez, yalnızca durumu gösterir.</summary>
    [HttpGet]
    public async Task<IActionResult> Dogrula(string? kod)
    {
        var sonuc = await _giris.DurumSorgulaAsync(kod);
        ViewData["Kod"] = kod;
        return View(sonuc);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Onayla(string? kod)
    {
        var sonuc = await _giris.GirisiOnaylaAsync(kod);
        ViewData["Kod"] = kod;
        ViewData["Onaylandi"] = sonuc.Durum == GirisDurumu.GirisOnaylandi;
        return View(nameof(Dogrula), sonuc);
    }
}

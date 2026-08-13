using BiletSatis.Web.Models;
using BiletSatis.Web.Services.Populerlik;
using Microsoft.AspNetCore.Mvc;

namespace BiletSatis.Web.Controllers;

/// <summary>
/// En çok satanlar / trend listesi. Dönem adres çubuğunda taşınır
/// (<c>/Populer?donem=hafta</c>), böylece liste paylaşılabilir ve geri düğmesi
/// beklendiği gibi çalışır — projedeki diğer filtrelerle aynı davranış.
/// </summary>
public class PopulerController : Controller
{
    /// <summary>Listede gösterilen etkinlik sayısı.</summary>
    private const int ListeBoyutu = 12;

    private readonly IPopulerlikServisi _populerlik;

    public PopulerController(IPopulerlikServisi populerlik)
    {
        _populerlik = populerlik;
    }

    public async Task<IActionResult> Index(string? donem)
    {
        var secilen = PopulerlikDonemleri.Coz(donem);

        return View(new PopulerListeVm
        {
            Donem = secilen,
            Etkinlikler = await _populerlik.EnCokSatanlarAsync(secilen, ListeBoyutu)
        });
    }
}

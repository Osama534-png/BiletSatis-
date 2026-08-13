using BiletSatis.Web.Models;
using BiletSatis.Web.Services.Mekanlar;
using Microsoft.AspNetCore.Mvc;

namespace BiletSatis.Web.Controllers;

/// <summary>
/// Mekan sayfası: "bu mekanda başka ne var" sorusunun cevabı.
///
/// Mekan adresi sorgu dizesiyle taşınır (<c>/Mekan/Detay?mekan=...</c>), yol
/// parçasıyla değil: mekan metni virgül ve boşluk içeriyor ve ileride eğik çizgi
/// de içerebilir. Sorgu dizesi bunların hepsini kodlayarak taşır, hem de projedeki
/// diğer bağlantılarla (<c>?etkinlikId=</c>, <c>?kategori=</c>) aynı biçimde kalır.
/// Adres yine paylaşılabilir ve geri düğmesi beklendiği gibi çalışır.
/// </summary>
public class MekanController : Controller
{
    private readonly IMekanSorguServisi _mekan;

    public MekanController(IMekanSorguServisi mekan)
    {
        _mekan = mekan;
    }

    public async Task<IActionResult> Detay(string? mekan, bool gecmis = false, int sayfa = 1)
    {
        var ozet = await _mekan.OzetAsync(mekan ?? "");
        if (ozet == null) return NotFound();

        // Yaklaşan etkinliği kalmamış bir mekana gelindiğinde boş liste göstermek
        // yerine doğrudan geçmiş sekmesini açıyoruz; aksi halde sayfa, arşivi
        // doluyken "hiç etkinlik yok" diyordu.
        if (!gecmis && ozet.YaklasanEtkinlik == 0 && ozet.GecmisEtkinlik > 0)
        {
            gecmis = true;
        }

        var etkinlikler = await _mekan.EtkinliklerAsync(
            ozet.Mekan, gecmis, sayfa, EtkinlikFiltresi.VarsayilanSayfaBoyutu);

        return View(new MekanDetayVm
        {
            Ozet = ozet,
            Etkinlikler = etkinlikler,
            Gecmis = gecmis
        });
    }
}

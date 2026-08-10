using System.Diagnostics;
using BiletSatis.Web.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Etkinlikler;
using BiletSatis.Web.Services.Favoriler;

namespace BiletSatis.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>Üstteki "öne çıkanlar" şeridinde gösterilen etkinlik sayısı.</summary>
    private const int OneCikanSayisi = 8;

    private readonly IEtkinlikSorguServisi _sorgu;
    private readonly IFavoriServisi _favori;
    private readonly ICurrentUserService _currentUser;

    public HomeController(
        IEtkinlikSorguServisi sorgu,
        IFavoriServisi favori,
        ICurrentUserService currentUser)
    {
        _sorgu = sorgu;
        _favori = favori;
        _currentUser = currentUser;
    }

    /// <summary>
    /// Filtreleme, sıralama ve sayfalama adres çubuğundan okunur ve veritabanında
    /// uygulanır. Böylece sunucu her istekte yalnızca görüntülenen sayfayı okur;
    /// filtrelenmiş liste paylaşılabilir ve geri düğmesi beklendiği gibi çalışır.
    /// </summary>
    public async Task<IActionResult> Index(
        string? arama,
        EtkinlikKategorisi? kategori,
        string? sehir,
        string tarih = "tumu",
        decimal? enYuksekFiyat = null,
        bool tukenenleriGoster = false,
        string siralama = "tarih",
        int sayfa = 1)
    {
        var filtre = new EtkinlikFiltresi
        {
            Arama = arama,
            Kategori = kategori,
            Sehir = sehir,
            Tarih = tarih,
            EnYuksekFiyat = enYuksekFiyat,
            TukenenleriGoster = tukenenleriGoster,
            Siralama = siralama,
            Sayfa = sayfa
        };

        var sonuc = await _sorgu.AraAsync(filtre);

        // İstenen sayfa son sayfanın ötesindeyse (ör. filtre daraldıktan sonra eski
        // bağlantıya dönüldüyse) boş liste göstermek yerine son sayfaya götürüyoruz.
        if (sonuc.ToplamKayit > 0 && filtre.GecerliSayfa > sonuc.ToplamSayfa)
        {
            var degerler = filtre.BaglantiDegerleri(sonuc.ToplamSayfa);
            return RedirectToAction(nameof(Index), degerler);
        }

        // Filtre değiştiğinde tarayıcı yalnızca sonuç listesini ister. Sayfanın
        // geri kalanını (başlık, sayaçlar, öne çıkanlar, menü) yeniden üretmenin
        // anlamı yok — hem sunucuda hem ağda gereksiz iş olurdu.
        if (Request.Headers.XRequestedWith == "XMLHttpRequest")
        {
            return PartialView("_EtkinlikSonuclari", new AnaSayfaVm
            {
                Sayfa = sonuc,
                Filtre = filtre,
                FavoriEtkinlikIdleri = await _favori.FavoriIdleriAsync(_currentUser.GetKullaniciId())
            });
        }

        var istatistik = await _sorgu.IstatistikAsync();

        var vm = new AnaSayfaVm
        {
            Sayfa = sonuc,
            Filtre = filtre,
            ToplamEtkinlik = istatistik.ToplamEtkinlik,
            ToplamSatistaBilet = istatistik.SatistakiBilet,
            ToplamKuyruktaBekleyen = istatistik.KuyruktaBekleyen,
            Sehirler = await _sorgu.SehirlerAsync(),
            FiyatTavani = await _sorgu.FiyatTavaniAsync(),
            FavoriEtkinlikIdleri = await _favori.FavoriIdleriAsync(_currentUser.GetKullaniciId())
        };

        // Öne çıkanlar şeridi filtreden bağımsızdır ve yalnızca ilk sayfada gösterilir;
        // her sayfada tekrar sorgulamanın anlamı yok.
        if (filtre.GecerliSayfa == 1 && !filtre.FiltreVarMi)
        {
            var oneCikanFiltre = new EtkinlikFiltresi { Sayfa = 1, SayfaBoyutu = OneCikanSayisi };
            vm.OneCikanlar = (await _sorgu.AraAsync(oneCikanFiltre)).Ogeler;
        }

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

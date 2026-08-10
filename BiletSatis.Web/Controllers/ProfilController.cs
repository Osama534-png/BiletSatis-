using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using BiletSatis.Web.Services;
using BiletSatis.Web.Services.Eposta;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Controllers;

[Authorize]
public class ProfilController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IKimlikEpostaKuyrugu _epostaKuyrugu;
    private readonly BiletSatisDbContext _db;
    private readonly ILogger<ProfilController> _logger;

    public ProfilController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IKimlikEpostaKuyrugu epostaKuyrugu,
        BiletSatisDbContext db,
        ILogger<ProfilController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _epostaKuyrugu = epostaKuyrugu;
        _db = db;
        _logger = logger;
    }

    public async Task<IActionResult> Index()
    {
        var kullanici = await _userManager.GetUserAsync(User);
        if (kullanici == null) return Challenge();

        return View(await ProfilOlusturAsync(kullanici));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BilgileriGuncelle(
        [Bind(Prefix = nameof(ProfilVm.Bilgiler))] ProfilBilgiFormu form)
    {
        var kullanici = await _userManager.GetUserAsync(User);
        if (kullanici == null) return Challenge();

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await ProfilOlusturAsync(kullanici, form));
        }

        if (kullanici.Ad != form.Ad)
        {
            kullanici.Ad = form.Ad;
            var adSonucu = await _userManager.UpdateAsync(kullanici);
            if (!adSonucu.Succeeded)
            {
                HatalariEkle(adSonucu);
                return View(nameof(Index), await ProfilOlusturAsync(kullanici, form));
            }
        }

        var epostaDegisti = !string.Equals(kullanici.Email, form.Email, StringComparison.OrdinalIgnoreCase);
        if (epostaDegisti)
        {
            // Aynı e-posta başka bir hesapta kayıtlıysa Identity'nin genel hatası yerine
            // alana bağlı, anlaşılır bir mesaj göster.
            var sahip = await _userManager.FindByEmailAsync(form.Email);
            if (sahip != null && sahip.Id != kullanici.Id)
            {
                ModelState.AddModelError($"{nameof(ProfilVm.Bilgiler)}.{nameof(form.Email)}",
                    "Bu e-posta adresi başka bir hesapta kullanılıyor.");
                return View(nameof(Index), await ProfilOlusturAsync(kullanici, form));
            }

            // Adres burada DEĞİŞTİRİLMİYOR; yalnızca yeni adrese onay bağlantısı gidiyor.
            //
            // Önceden SetEmailAsync doğrudan çağrılıyordu. Identity bu metotta adresi
            // değiştirirken doğrulama bayrağını da sıfırlar; e-posta doğrulaması zorunlu
            // olduğu için kullanıcı çıkış yaptığı anda hesabına bir daha giremiyordu.
            // Üstelik yanlış yazılan bir adres hesabı kalıcı olarak erişilemez yapıyordu:
            // eski adres gitmiş, yeni adrese ulaşılamıyor.
            //
            // Doğru sıra: önce yeni adresin kullanıcıya ait olduğunu kanıtla, sonra değiştir.
            await DegisiklikOnayiGonderAsync(kullanici, form.Email);

            TempData["Bilgi"] = $"{form.Email} adresine bir onay bağlantısı gönderdik. " +
                                "Bağlantıya tıklayana kadar hesabınızın adresi değişmez ve " +
                                "mevcut adresinizle giriş yapmaya devam edersiniz.";

            return RedirectToAction(nameof(Index));
        }

        TempData["Bilgi"] = "Bilgileriniz güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Yeni adrese gönderilen onay bağlantısının açtığı sayfa. Jeton yalnızca bu
    /// kullanıcı ve bu adres için üretildiğinden, bağlantıyı ele geçiren biri onu
    /// başka bir adrese çeviremez.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> EpostaDegisikliginiOnayla(string? kullaniciId, string? yeniEposta, string? jeton)
    {
        var kullanici = await _userManager.GetUserAsync(User);
        if (kullanici == null) return Challenge();

        // Bağlantı başka bir hesap için üretilmişse kabul edilmez: oturumu açık olan
        // kullanıcı, eline geçen bir bağlantıyla başkasının adresini değiştiremesin.
        if (string.IsNullOrEmpty(kullaniciId) || string.IsNullOrEmpty(yeniEposta) ||
            string.IsNullOrEmpty(jeton) || kullaniciId != kullanici.Id)
        {
            return View("EpostaDegisikligiGecersiz");
        }

        var sonuc = await _userManager.ChangeEmailAsync(kullanici, yeniEposta, JetonKodlayici.Coz(jeton));
        if (!sonuc.Succeeded)
        {
            _logger.LogWarning(
                "E-posta değişikliği onaylanamadı: KullaniciId={KullaniciId} Hatalar={Hatalar}",
                kullanici.Id, string.Join(", ", sonuc.Errors.Select(h => h.Description)));

            return View("EpostaDegisikligiGecersiz");
        }

        // Giriş kullanıcı adıyla yapılıyor ve kayıtta kullanıcı adı e-posta olarak
        // atanıyor; adres değişince kullanıcı adı da değişmeli.
        var kullaniciAdiSonucu = await _userManager.SetUserNameAsync(kullanici, yeniEposta);
        if (!kullaniciAdiSonucu.Succeeded)
        {
            HatalariEkle(kullaniciAdiSonucu);
            return View("EpostaDegisikligiGecersiz");
        }

        // Kullanıcı adı ve adres değişince güvenlik damgası yenilenir; çerez
        // tazelenmezse kullanıcı bir sonraki istekte oturumdan düşer.
        await _signInManager.RefreshSignInAsync(kullanici);

        _logger.LogInformation("E-posta adresi değiştirildi: KullaniciId={KullaniciId}", kullanici.Id);

        ViewData["YeniEposta"] = yeniEposta;
        return View("EpostaDegistirildi");
    }

    private async Task DegisiklikOnayiGonderAsync(ApplicationUser kullanici, string yeniEposta)
    {
        var jeton = await _userManager.GenerateChangeEmailTokenAsync(kullanici, yeniEposta);

        var adres = Url.Action(nameof(EpostaDegisikliginiOnayla), "Profil",
            new { kullaniciId = kullanici.Id, yeniEposta, jeton = JetonKodlayici.Kodla(jeton) },
            protocol: Request.Scheme)!;

        // Gönderim isteğin dışında yapılıyor; kullanıcı SMTP sunucusunu beklemesin.
        // Hata olursa yalnızca loglanır — hesapta hiçbir şey değişmediği için kullanıcı
        // formu tekrar gönderip yeni bir bağlantı isteyebilir.
        _epostaKuyrugu.Kuyruklat(new KimlikEpostaIsi(
            KimlikEpostaTuru.AdresDegisikligi, yeniEposta, kullanici.Ad, adres));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SifreDegistir(
        [Bind(Prefix = nameof(ProfilVm.Sifre))] SifreDegistirFormu form)
    {
        var kullanici = await _userManager.GetUserAsync(User);
        if (kullanici == null) return Challenge();

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await ProfilOlusturAsync(kullanici, sifre: form));
        }

        var sonuc = await _userManager.ChangePasswordAsync(kullanici, form.MevcutSifre, form.YeniSifre);
        if (!sonuc.Succeeded)
        {
            HatalariEkle(sonuc);
            return View(nameof(Index), await ProfilOlusturAsync(kullanici, sifre: form));
        }

        // Şifre değişince güvenlik damgası yenilenir; oturumun kopmaması için tazele.
        await _signInManager.RefreshSignInAsync(kullanici);

        TempData["Bilgi"] = "Şifreniz değiştirildi.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ProfilVm> ProfilOlusturAsync(
        ApplicationUser kullanici,
        ProfilBilgiFormu? bilgiler = null,
        SifreDegistirFormu? sifre = null)
    {
        var biletler = await _db.Biletler
            .AsNoTracking()
            .Where(b => b.RezerveEdenKullaniciId == kullanici.Id)
            .Select(b => new { b.Durum, b.Fiyat })
            .ToListAsync();

        var satilanlar = biletler.Where(b => b.Durum == BiletDurumu.Satildi).ToList();

        return new ProfilVm
        {
            Ad = kullanici.Ad,
            Email = kullanici.Email ?? "",
            AdminMi = await _userManager.IsInRoleAsync(kullanici, "Admin"),
            SatinAlinanBilet = satilanlar.Count,
            ToplamHarcama = satilanlar.Sum(b => b.Fiyat),
            SepettekiBilet = biletler.Count(b => b.Durum == BiletDurumu.Sepette),
            Bilgiler = bilgiler ?? new ProfilBilgiFormu { Ad = kullanici.Ad, Email = kullanici.Email ?? "" },
            Sifre = sifre ?? new SifreDegistirFormu()
        };
    }

    private void HatalariEkle(IdentityResult sonuc)
    {
        foreach (var hata in sonuc.Errors)
        {
            ModelState.AddModelError(string.Empty, hata.Description);
        }
    }
}

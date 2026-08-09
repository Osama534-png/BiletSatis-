using BiletSatis.Web.Data;
using BiletSatis.Web.Models;
using Microsoft.AspNetCore.Authorization;
using BiletSatis.Web.Services.Eposta;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace BiletSatis.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    /// <summary>Program.cs'te tanımlı hız sınırı politikasının adı.</summary>
    public const string HizSiniri = "giris";

    private readonly UserManager<ApplicationUser> _userManager;//kullanıcı OLUŞTURMA/yönetme
	private readonly SignInManager<ApplicationUser> _signInManager;//giriş/çıkış YAPTIRMA cookıs
	private readonly IKimlikEpostaServisi _kimlikEposta;
    private readonly ILogger<AccountController> _logger;

	public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IKimlikEpostaServisi kimlikEposta,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _kimlikEposta = kimlikEposta;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult KayitOl(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new KayitOlViewModel());
    }

    // Hız sınırı yalnızca formu gönderen POST'lara uygulanır. Sayfa açılışları (GET)
    // da sayılsaydı her deneme iki isteğe mal olur, kullanıcı hesap kilidi mesajını
    // görmeden sınıra takılırdı.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(HizSiniri)]
    public async Task<IActionResult> KayitOl(KayitOlViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);
        //hayit olma
        var user = new ApplicationUser { UserName = model.Email, Email = model.Email, Ad = model.Ad };
        var sonuc = await _userManager.CreateAsync(user, model.Sifre); //kullanıcıyı DB'ye yaz (şifre otomatik hash'lenir)

        if (sonuc.Succeeded)
        {
            // Artık kayıttan sonra otomatik giriş yapılmıyor: adresin gerçekten
            // kullanıcıya ait olduğu doğrulanana kadar hesap kullanılamaz.
            await DogrulamaEpostasiGonderAsync(user);

            TempData["Bilgi"] = "Hesabınız oluşturuldu. Girişe geçmeden önce e-posta adresinize " +
                                "gönderdiğimiz doğrulama bağlantısına tıklayın.";

            return RedirectToAction(nameof(EpostaDogrulamaBekleniyor), new { eposta = user.Email });
        }

        foreach (var hata in sonuc.Errors)
        {
            ModelState.AddModelError(string.Empty, hata.Description);
        }
        return View(model);
    }

    [HttpGet]
    public IActionResult GirisYap(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new GirisYapViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(HizSiniri)]
    public async Task<IActionResult> GirisYap(GirisYapViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        // lockoutOnFailure: art arda hatalı denemeler hesabı geçici olarak kilitler.
        // Kapalıyken şifre sınırsız kez denenebiliyordu — kaba kuvvet saldırısına açıktı.
        var sonuc = await _signInManager.PasswordSignInAsync(model.Email, model.Sifre, model.BeniHatirla, lockoutOnFailure: true);

        if (sonuc.Succeeded)
        {
            return RedirectToLocal(returnUrl);
        }

        if (sonuc.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty,
                "Çok fazla hatalı deneme yapıldı. Hesabınız güvenlik için 5 dakika kilitlendi.");
            return View(model);
        }

        if (sonuc.IsNotAllowed)
        {
            // Şifre doğru ama e-posta doğrulanmamış. Kullanıcıyı bağlantıyı tekrar
            // isteyebileceği sayfaya yönlendiriyoruz.
            return RedirectToAction(nameof(EpostaDogrulamaBekleniyor), new { eposta = model.Email });
        }

        // Hangi adresin kayıtlı olduğunu ele vermemek için mesaj tek: "e-posta veya şifre".
        ModelState.AddModelError(string.Empty, "E-posta veya şifre hatalı.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CikisYap()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult ErisimEngellendi() => View();

    /// <summary>
    /// Hız sınırına takılan istekler buraya yönlendirilir. Bu sayfanın kendisi
    /// sınırlanmaz, aksi halde yönlendirme döngüye girerdi.
    /// </summary>
    [HttpGet]
    public IActionResult CokFazlaDeneme() => View();

    // ---------- E-posta doğrulama ----------

    [HttpGet]
    public IActionResult EpostaDogrulamaBekleniyor(string? eposta)
    {
        ViewData["Eposta"] = eposta;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(HizSiniri)]
    public async Task<IActionResult> DogrulamaTekrarGonder(string eposta)
    {
        var kullanici = await _userManager.FindByEmailAsync(eposta ?? "");

        // Hesabın var olup olmadığını ele vermiyoruz; her durumda aynı mesaj.
        if (kullanici != null && !await _userManager.IsEmailConfirmedAsync(kullanici))
        {
            await DogrulamaEpostasiGonderAsync(kullanici);
        }

        TempData["Bilgi"] = "Doğrulama bağlantısı yeniden gönderildi. Gelen kutunuzu kontrol edin.";
        return RedirectToAction(nameof(EpostaDogrulamaBekleniyor), new { eposta });
    }

    [HttpGet]
    public async Task<IActionResult> EpostaDogrula(string? kullaniciId, string? jeton)
    {
        if (string.IsNullOrEmpty(kullaniciId) || string.IsNullOrEmpty(jeton))
        {
            return View("EpostaDogrulanamadi");
        }

        var kullanici = await _userManager.FindByIdAsync(kullaniciId);
        if (kullanici == null) return View("EpostaDogrulanamadi");

        var sonuc = await _userManager.ConfirmEmailAsync(kullanici, JetonuCoz(jeton));

        return sonuc.Succeeded ? View("EpostaDogrulandi") : View("EpostaDogrulanamadi");
    }

    // ---------- Şifre sıfırlama ----------

    [HttpGet]
    public IActionResult SifremiUnuttum() => View(new SifremiUnuttumViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(HizSiniri)]
    public async Task<IActionResult> SifremiUnuttum(SifremiUnuttumViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var kullanici = await _userManager.FindByEmailAsync(model.Email);

        // Hesabın varlığını ele vermemek için sonuç ne olursa olsun aynı sayfaya
        // gidiyoruz. Aksi halde bu form, kayıtlı adresleri tarayan bir araca dönerdi.
        if (kullanici != null && await _userManager.IsEmailConfirmedAsync(kullanici))
        {
            var jeton = await _userManager.GeneratePasswordResetTokenAsync(kullanici);
            var adres = Url.Action(nameof(SifreSifirla), "Account",
                new { eposta = kullanici.Email, jeton = JetonuKodla(jeton) },
                protocol: Request.Scheme)!;

            await EpostaDeneAsync(
                () => _kimlikEposta.SifirlamaGonderAsync(kullanici.Email!, kullanici.Ad, adres),
                "Şifre sıfırlama e-postası gönderilemedi: {Alici}", kullanici.Email!);
        }

        return RedirectToAction(nameof(SifirlamaGonderildi));
    }

    [HttpGet]
    public IActionResult SifirlamaGonderildi() => View();

    [HttpGet]
    public IActionResult SifreSifirla(string? eposta, string? jeton)
    {
        if (string.IsNullOrEmpty(eposta) || string.IsNullOrEmpty(jeton))
        {
            return View("SifirlamaBaglantisiGecersiz");
        }

        return View(new SifreSifirlaViewModel { Email = eposta, Jeton = jeton });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(HizSiniri)]
    public async Task<IActionResult> SifreSifirla(SifreSifirlaViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var kullanici = await _userManager.FindByEmailAsync(model.Email);
        if (kullanici == null)
        {
            // Var olmayan hesap için de "başarılı" gösteriyoruz; adres taraması yapılamasın.
            return RedirectToAction(nameof(SifreSifirlandi));
        }

        var sonuc = await _userManager.ResetPasswordAsync(kullanici, JetonuCoz(model.Jeton), model.YeniSifre);

        if (sonuc.Succeeded)
        {
            // Şifre sıfırlandıysa hesabın kilidi de açılmalı; aksi halde kullanıcı
            // yeni şifresiyle bile kilit süresi dolana kadar giremezdi.
            await _userManager.SetLockoutEndDateAsync(kullanici, null);
            await _userManager.ResetAccessFailedCountAsync(kullanici);

            _logger.LogInformation("Şifre sıfırlandı: KullaniciId={KullaniciId}", kullanici.Id);
            return RedirectToAction(nameof(SifreSifirlandi));
        }

        HatalariEkle(sonuc);
        return View(model);
    }

    [HttpGet]
    public IActionResult SifreSifirlandi() => View();

    // ---------- Yardımcılar ----------

    private async Task DogrulamaEpostasiGonderAsync(ApplicationUser kullanici)
    {
        var jeton = await _userManager.GenerateEmailConfirmationTokenAsync(kullanici);
        var adres = Url.Action(nameof(EpostaDogrula), "Account",
            new { kullaniciId = kullanici.Id, jeton = JetonuKodla(jeton) },
            protocol: Request.Scheme)!;

        await EpostaDeneAsync(
            () => _kimlikEposta.DogrulamaGonderAsync(kullanici.Email!, kullanici.Ad, adres),
            "Doğrulama e-postası gönderilemedi: {Alici}", kullanici.Email!);
    }

    /// <summary>
    /// E-posta gönderimi başarısız olursa akış kesilmemeli: hesap zaten oluşturuldu,
    /// kullanıcı bağlantıyı tekrar isteyebilir. Hata yalnızca loglanır.
    /// </summary>
    private async Task EpostaDeneAsync(Func<Task> gonderim, string hataMesaji, string alici)
    {
        try
        {
            await gonderim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, hataMesaji, alici);
        }
    }

    // Identity jetonları "+" ve "/" içerebilir; adres satırında bozulmamaları için
    // Base64Url ile kodlanıp öyle taşınır.
    private static string JetonuKodla(string jeton) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(jeton));

    private static string JetonuCoz(string kodlanmis)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(kodlanmis));
        }
        catch (FormatException)
        {
            // Bozuk bağlantı: geçersiz jeton olarak ele alınır, doğrulama başarısız olur.
            return "";
        }
    }

    private void HatalariEkle(IdentityResult sonuc)
    {
        foreach (var hata in sonuc.Errors)
        {
            ModelState.AddModelError(string.Empty, hata.Description);
        }
    }

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}

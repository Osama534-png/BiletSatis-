using BiletSatis.Web.Data;
using BiletSatis.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BiletSatis.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
    /// <summary>Program.cs'te tanımlı hız sınırı politikasının adı.</summary>
    public const string HizSiniri = "giris";

    private readonly UserManager<ApplicationUser> _userManager;//kullanıcı OLUŞTURMA/yönetme
	private readonly SignInManager<ApplicationUser> _signInManager;//giriş/çıkış YAPTIRMA cookıs

	public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
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
            await _signInManager.SignInAsync(user, isPersistent: false);//tarayıcıya "giriş yapıldı" çerezi ver
			return RedirectToLocal(returnUrl);
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

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}

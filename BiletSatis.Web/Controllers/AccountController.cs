using BiletSatis.Web.Data;
using BiletSatis.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BiletSatis.Web.Controllers;

[AllowAnonymous]
public class AccountController : Controller
{
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

    [HttpPost]
    [ValidateAntiForgeryToken]
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
    public async Task<IActionResult> GirisYap(GirisYapViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);

        var sonuc = await _signInManager.PasswordSignInAsync(model.Email, model.Sifre, model.BeniHatirla, lockoutOnFailure: false);

        if (sonuc.Succeeded)
        {
            return RedirectToLocal(returnUrl);
        }

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

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}

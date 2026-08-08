using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
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
    private readonly BiletSatisDbContext _db;

    public ProfilController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        BiletSatisDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
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

            var epostaSonucu = await _userManager.SetEmailAsync(kullanici, form.Email);
            if (!epostaSonucu.Succeeded)
            {
                HatalariEkle(epostaSonucu);
                return View(nameof(Index), await ProfilOlusturAsync(kullanici, form));
            }

            // Kayıt sırasında kullanıcı adı e-posta olarak atanıyor ve giriş bununla
            // yapılıyor; e-posta değişince kullanıcı adı da güncellenmeli.
            var kullaniciAdiSonucu = await _userManager.SetUserNameAsync(kullanici, form.Email);
            if (!kullaniciAdiSonucu.Succeeded)
            {
                HatalariEkle(kullaniciAdiSonucu);
                return View(nameof(Index), await ProfilOlusturAsync(kullanici, form));
            }

            // Kullanıcı adı değişince güvenlik damgası yenilenir; çerez tazelenmezse
            // kullanıcı bir sonraki istekte oturumdan düşer.
            await _signInManager.RefreshSignInAsync(kullanici);
        }

        TempData["Bilgi"] = epostaDegisti
            ? "Bilgileriniz güncellendi. Bundan sonra yeni e-posta adresinizle giriş yapacaksınız."
            : "Bilgileriniz güncellendi.";

        return RedirectToAction(nameof(Index));
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

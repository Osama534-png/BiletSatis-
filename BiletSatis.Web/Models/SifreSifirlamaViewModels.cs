using System.ComponentModel.DataAnnotations;

namespace BiletSatis.Web.Models;

public class SifremiUnuttumViewModel
{
    [Required(ErrorMessage = "E-posta adresi gerekli.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = "";
}

public class SifreSifirlaViewModel
{
    [Required]
    public string Email { get; set; } = "";

    /// <summary>E-postadaki bağlantıdan gelen, Base64Url ile kodlanmış Identity jetonu.</summary>
    [Required]
    public string Jeton { get; set; } = "";

    [Required(ErrorMessage = "Yeni şifre gerekli.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az {2} karakter olmalı.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni şifre")]
    public string YeniSifre { get; set; } = "";

    [DataType(DataType.Password)]
    [Display(Name = "Yeni şifre (tekrar)")]
    [Compare(nameof(YeniSifre), ErrorMessage = "Şifreler eşleşmiyor.")]
    public string YeniSifreTekrar { get; set; } = "";
}

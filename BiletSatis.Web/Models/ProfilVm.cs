using System.ComponentModel.DataAnnotations;

namespace BiletSatis.Web.Models;

public class ProfilVm
{
    public string Ad { get; set; } = "";
    public string Email { get; set; } = "";
    public bool AdminMi { get; set; }

    public int SatinAlinanBilet { get; set; }
    public decimal ToplamHarcama { get; set; }
    public int SepettekiBilet { get; set; }

    public ProfilBilgiFormu Bilgiler { get; set; } = new();
    public SifreDegistirFormu Sifre { get; set; } = new();

    /// <summary>Avatar için ad ve soyadın baş harfleri.</summary>
    public string BasHarfler
    {
        get
        {
            var parcalar = Ad.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parcalar.Length == 0)
            {
                return Email.Length > 0 ? Email[..1].ToUpperInvariant() : "?";
            }

            var ilk = parcalar[0][..1];
            var son = parcalar.Length > 1 ? parcalar[^1][..1] : "";
            return (ilk + son).ToUpperInvariant();
        }
    }
}

public class ProfilBilgiFormu
{
    [Required(ErrorMessage = "Ad zorunludur.")]
    [StringLength(100)]
    [Display(Name = "Ad Soyad")]
    public string Ad { get; set; } = "";

    [Required(ErrorMessage = "E-posta zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta girin.")]
    [Display(Name = "E-posta")]
    public string Email { get; set; } = "";
}

public class SifreDegistirFormu
{
    [Required(ErrorMessage = "Mevcut şifrenizi girin.")]
    [DataType(DataType.Password)]
    [Display(Name = "Mevcut Şifre")]
    public string MevcutSifre { get; set; } = "";

    [Required(ErrorMessage = "Yeni şifre zorunludur.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni Şifre")]
    public string YeniSifre { get; set; } = "";

    [Compare(nameof(YeniSifre), ErrorMessage = "Şifreler eşleşmiyor.")]
    [DataType(DataType.Password)]
    [Display(Name = "Yeni Şifre (Tekrar)")]
    public string YeniSifreTekrar { get; set; } = "";
}

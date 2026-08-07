using System.ComponentModel.DataAnnotations;
using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Models;

public class EtkinlikEkleViewModel
{
    [Required(ErrorMessage = "Etkinlik adı zorunludur.")]
    [StringLength(200)]
    [Display(Name = "Etkinlik Adı")]
    public string Ad { get; set; } = "";

    [Required(ErrorMessage = "Mekan zorunludur.")]
    [StringLength(200)]
    [Display(Name = "Mekan")]
    public string Mekan { get; set; } = "";

    [Display(Name = "Kategori")]
    public EtkinlikKategorisi Kategori { get; set; } = EtkinlikKategorisi.Konser;

    [Display(Name = "Afiş Görseli (isteğe bağlı)")]
    public IFormFile? AfisDosyasi { get; set; }

    [Required(ErrorMessage = "Tarih zorunludur.")]
    [Display(Name = "Tarih")]
    [DataType(DataType.DateTime)]
    public DateTime Tarih { get; set; } = DateTime.Today.AddDays(30);
}

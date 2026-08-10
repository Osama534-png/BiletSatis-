using System.ComponentModel.DataAnnotations;
using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Models;

public class EtkinlikDuzenleViewModel
{
    public int Id { get; set; }

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

    [Display(Name = "Bilet modeli")]
    public BiletModeli BiletModeli { get; set; } = BiletModeli.KoltukSecmeli;

    [StringLength(2000)]
    [Display(Name = "Açıklama")]
    public string Aciklama { get; set; } = "";

    [Range(0, 21, ErrorMessage = "Yaş sınırı 0 ile 21 arasında olmalıdır.")]
    [Display(Name = "Yaş Sınırı (0 = sınır yok)")]
    public int YasSiniri { get; set; }

    [Required(ErrorMessage = "Tarih zorunludur.")]
    [Display(Name = "Tarih")]
    [DataType(DataType.DateTime)]
    public DateTime Tarih { get; set; }

    [Display(Name = "Afişi Değiştir (isteğe bağlı)")]
    public IFormFile? AfisDosyasi { get; set; }

    /// <summary>Formda önizleme için gösterilen mevcut afiş.</summary>
    public string MevcutAfisUrl { get; set; } = "";
}

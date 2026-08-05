using System.ComponentModel.DataAnnotations;

namespace BiletSatis.Web.Models;

public class EtkinlikEkleViewModel
{
    [Required(ErrorMessage = "Etkinlik adı zorunludur.")]
    [StringLength(200)]
    [Display(Name = "Etkinlik Adı")]
    public string Ad { get; set; } = "";

    [Required(ErrorMessage = "Tarih zorunludur.")]
    [Display(Name = "Tarih")]
    [DataType(DataType.DateTime)]
    public DateTime Tarih { get; set; } = DateTime.Today.AddDays(30);
}

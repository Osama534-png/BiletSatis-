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

    // Bkz. EtkinlikEkleViewModel: null kabul etmeyen string, ASP.NET tarafından
    // kendiliğinden zorunlu sayılıyor ve boş açıklama reddediliyordu.
    [StringLength(2000)]
    [Display(Name = "Açıklama (isteğe bağlı)")]
    public string? Aciklama { get; set; }

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

    /// <summary>
    /// Form açılırken okunan satır sürümü. Kayıp güncelleme koruması buna dayanır:
    /// kaydetme sırasında bu değer satırın güncel sürümüyle karşılaştırılır ve
    /// arada başkası kaydettiyse güncelleme reddedilir.
    ///
    /// Formda taşınması şart. Sunucu satırı POST'ta yeniden okuyup üstüne yazsaydı
    /// karşılaştırılan sürüm "az önce okuduğum" sürüm olur, çakışma hiç oluşmazdı —
    /// koruma şemada durur ama gerçek akışta hiçbir şey yapmazdı.
    /// </summary>
    public byte[]? SatirSurumu { get; set; }
}

namespace BiletSatis.Web.Domain;

public class RezervasyonKuyrugu
{
    public int SiraNo { get; set; }
    public int EtkinlikId { get; set; }
    public string KullaniciId { get; set; } = "";
    public KuyrukDurumu Durum { get; set; } = KuyrukDurumu.Beklemede;
    public DateTime? HakBitisZamani { get; set; }
    public DateTime OlusturmaZamani { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// "Sıran geldi" e-postası gönderildi mi. Bildirim, hak tanıma işleminden
    /// ayrı bir arka plan görevinde gönderilir; bu bayrak hem tekrar gönderimi
    /// hem de gönderim hatasında kaybı önler (bayrak false kalırsa tekrar denenir).
    /// </summary>
    public bool BildirimGonderildi { get; set; }

    /// <summary>
    /// Bildirim görevinin bu kaydı sahiplendiği an (UTC). Bkz. <see cref="Bilet.BildirimKilitZamani"/>.
    /// </summary>
    public DateTime? BildirimKilitZamani { get; set; }
}

namespace BiletSatis.Web.Domain;

public class RezervasyonKuyrugu
{
    public int SiraNo { get; set; }
    public int EtkinlikId { get; set; }
    public string KullaniciId { get; set; } = "";
    public KuyrukDurumu Durum { get; set; } = KuyrukDurumu.Beklemede;
    public DateTime? HakBitisZamani { get; set; }
    public DateTime OlusturmaZamani { get; set; } = DateTime.UtcNow;
}

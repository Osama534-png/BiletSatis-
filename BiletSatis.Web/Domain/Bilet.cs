namespace BiletSatis.Web.Domain;

public class Bilet
{
    public int Id { get; set; }
    public int EtkinlikId { get; set; }
    public string KoltukNo { get; set; } = "";
    public decimal Fiyat { get; set; }
    public BiletDurumu Durum { get; set; } = BiletDurumu.Satista;
    public DateTime? KilitBitisZamani { get; set; }
    public string? RezerveEdenKullaniciId { get; set; }

    public Etkinlik? Etkinlik { get; set; }
}

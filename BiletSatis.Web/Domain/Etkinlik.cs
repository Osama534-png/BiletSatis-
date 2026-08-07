namespace BiletSatis.Web.Domain;

public class Etkinlik
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";
    public EtkinlikKategorisi Kategori { get; set; } = EtkinlikKategorisi.Konser;

    /// <summary>Afiş görselinin yolu (örn. "/img/afis/duman.svg"). Boşsa gradyan + emoji gösterilir.</summary>
    public string AfisUrl { get; set; } = "";

    public DateTime Tarih { get; set; }

    public List<Bilet> Biletler { get; set; } = new();
}

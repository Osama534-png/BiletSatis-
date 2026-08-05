namespace BiletSatis.Web.Domain;

public class Etkinlik
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public DateTime Tarih { get; set; }

    public List<Bilet> Biletler { get; set; } = new();
}

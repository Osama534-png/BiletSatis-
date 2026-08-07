namespace BiletSatis.Web.Models;

public class EtkinlikKartVm
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";
    public string AfisUrl { get; set; } = "";
    public BiletSatis.Web.Domain.EtkinlikKategorisi Kategori { get; set; }
    public DateTime Tarih { get; set; }
    public decimal? EnDusukFiyat { get; set; }
    public int MusaitKoltukSayisi { get; set; }
}

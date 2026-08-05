namespace BiletSatis.Web.Models;

public class AdminEtkinlikOzeti
{
    public int EtkinlikId { get; set; }
    public string Ad { get; set; } = "";
    public int SatistaSayisi { get; set; }
    public int SepetteSayisi { get; set; }
    public int SatildiSayisi { get; set; }
    public int KuyrukBeklemede { get; set; }
    public int KuyrukHakTanindi { get; set; }
}

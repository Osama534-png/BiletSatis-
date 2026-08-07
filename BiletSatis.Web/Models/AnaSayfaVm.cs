namespace BiletSatis.Web.Models;

public class AnaSayfaVm
{
    public List<EtkinlikKartVm> Etkinlikler { get; set; } = new();
    public int ToplamEtkinlik { get; set; }
    public int ToplamSatistaBilet { get; set; }
    public int ToplamKuyruktaBekleyen { get; set; }
}

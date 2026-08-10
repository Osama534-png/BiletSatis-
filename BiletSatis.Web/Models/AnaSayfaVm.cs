namespace BiletSatis.Web.Models;

public class AnaSayfaVm
{
    public List<EtkinlikKartVm> Etkinlikler { get; set; } = new();
    public int ToplamEtkinlik { get; set; }
    public int ToplamSatistaBilet { get; set; }
    public int ToplamKuyruktaBekleyen { get; set; }

    /// <summary>Şehir seçicide listelenen, en az bir etkinliği olan şehirler.</summary>
    public List<string> Sehirler { get; set; } = new();

    /// <summary>Giriş yapan kullanıcının favori etkinlik numaraları; kalbin dolu mu boş mu olacağını belirler.</summary>
    public HashSet<int> FavoriEtkinlikIdleri { get; set; } = new();
}

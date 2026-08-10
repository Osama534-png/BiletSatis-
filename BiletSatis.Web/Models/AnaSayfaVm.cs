namespace BiletSatis.Web.Models;

public class AnaSayfaVm
{
    /// <summary>Yalnızca görüntülenen sayfadaki etkinlikler.</summary>
    public SayfaliListe<EtkinlikKartVm> Sayfa { get; set; } = new();

    /// <summary>Adres çubuğundan okunan filtre seçimleri; formda ve bağlantılarda korunur.</summary>
    public EtkinlikFiltresi Filtre { get; set; } = new();

    public int ToplamEtkinlik { get; set; }
    public int ToplamSatistaBilet { get; set; }
    public int ToplamKuyruktaBekleyen { get; set; }

    /// <summary>Şehir seçicide listelenen, en az bir etkinliği olan şehirler.</summary>
    public List<string> Sehirler { get; set; } = new();

    /// <summary>Fiyat kaydırıcısının üst sınırı.</summary>
    public decimal FiyatTavani { get; set; } = 1000m;

    /// <summary>Giriş yapan kullanıcının favori etkinlik numaraları; kalbin dolu mu boş mu olacağını belirler.</summary>
    public HashSet<int> FavoriEtkinlikIdleri { get; set; } = new();

    /// <summary>Üstteki "öne çıkanlar" şeridi — ilk sayfada, en yakın tarihli etkinlikler.</summary>
    public List<EtkinlikKartVm> OneCikanlar { get; set; } = new();
}

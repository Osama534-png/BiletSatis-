using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Models;

public class AdminPanelVm
{
    public List<AdminEtkinlikOzeti> Etkinlikler { get; set; } = new();

    public int ToplamEtkinlik => Etkinlikler.Count;
    public int ToplamSatilan => Etkinlikler.Sum(e => e.SatildiSayisi);
    public int ToplamSatista => Etkinlikler.Sum(e => e.SatistaSayisi);
    public decimal ToplamGelir => Etkinlikler.Sum(e => e.Gelir);
    public int ToplamKuyrukta => Etkinlikler.Sum(e => e.KuyrukBeklemede);

    public int ToplamKoltuk => Etkinlikler.Sum(e => e.ToplamKoltuk);

    /// <summary>Tüm etkinliklerdeki koltukların yüzde kaçı satıldı.</summary>
    public int DolulukYuzdesi => ToplamKoltuk == 0
        ? 0
        : (int)Math.Round(ToplamSatilan * 100.0 / ToplamKoltuk);
}

public class AdminEtkinlikOzeti
{
    public int EtkinlikId { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";
    public EtkinlikKategorisi Kategori { get; set; }
    public DateTime Tarih { get; set; }

    public int SatistaSayisi { get; set; }
    public int SepetteSayisi { get; set; }
    public int SatildiSayisi { get; set; }
    public int KuyrukBeklemede { get; set; }
    public int KuyrukHakTanindi { get; set; }

    /// <summary>Satılan biletlerin toplam tutarı.</summary>
    public decimal Gelir { get; set; }

    public int ToplamKoltuk => SatistaSayisi + SepetteSayisi + SatildiSayisi;

    public int DolulukYuzdesi => ToplamKoltuk == 0
        ? 0
        : (int)Math.Round(SatildiSayisi * 100.0 / ToplamKoltuk);

    /// <summary>Satılmış bilet varsa etkinlik silinemez.</summary>
    public bool Silinebilir => SatildiSayisi == 0;
}

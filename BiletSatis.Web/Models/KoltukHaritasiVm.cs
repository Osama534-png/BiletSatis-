using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Models;

public class KoltukHaritasiVm
{
    public int EtkinlikId { get; set; }
    public string EtkinlikAdi { get; set; } = "";
    public string Mekan { get; set; } = "";
    public string AfisUrl { get; set; } = "";
    public DateTime Tarih { get; set; }
    public List<BlokVm> Bloklar { get; set; } = new();

    public int ToplamKoltuk => Bloklar.Sum(b => b.ToplamKoltuk);
    public int ToplamMusait => Bloklar.Sum(b => b.MusaitKoltuk);
    public decimal? EnDusukFiyat => Bloklar.Count == 0 ? null : Bloklar.Min(b => b.EnDusukFiyat);
    public decimal? EnYuksekFiyat => Bloklar.Count == 0 ? null : Bloklar.Max(b => b.EnDusukFiyat);
}

public class BlokVm
{
    public string Kod { get; set; } = "";
    public string Ad { get; set; } = "";
    public string Kategori { get; set; } = "";
    public decimal EnDusukFiyat { get; set; }
    public int ToplamKoltuk { get; set; }
    public int MusaitKoltuk { get; set; }

    /// <summary>Sahneye en yakın, en pahalı blok — haritada öne yerleşir.</summary>
    public bool OnSira { get; set; }

    public List<KoltukVm> Koltuklar { get; set; } = new();

    public bool Tukendi => MusaitKoltuk == 0;

    /// <summary>Doluluğa göre harita rengi: bol / az / tükendi.</summary>
    public string DolulukSinifi => MusaitKoltuk == 0
        ? "doluluk-yok"
        : MusaitKoltuk <= ToplamKoltuk * 0.25m ? "doluluk-az" : "doluluk-bol";
}

public class KoltukVm
{
    public int BiletId { get; set; }
    public string KoltukNo { get; set; } = "";

    /// <summary>Izgarada gösterilen kısa numara ("A-01" → "01").</summary>
    public string KisaNo { get; set; } = "";

    public decimal Fiyat { get; set; }
    public BiletDurumu Durum { get; set; }
}

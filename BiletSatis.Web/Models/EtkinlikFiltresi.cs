using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Models;

/// <summary>
/// Ana sayfadaki filtre ve sıralama seçimleri. Adres çubuğundan okunur, böylece
/// filtrelenmiş bir liste paylaşılabilir ve tarayıcının geri düğmesi çalışır.
/// </summary>
public class EtkinlikFiltresi
{
    public const int VarsayilanSayfaBoyutu = 12;

    /// <summary>Sayfa boyutu istemciden gelse bile bu sınırın üstüne çıkamaz.</summary>
    public const int AzamiSayfaBoyutu = 48;

    public string? Arama { get; set; }
    public EtkinlikKategorisi? Kategori { get; set; }
    public string? Sehir { get; set; }

    /// <summary>"tumu" | "hafta" | "ay"</summary>
    public string Tarih { get; set; } = "tumu";

    public decimal? EnYuksekFiyat { get; set; }

    /// <summary>Varsayılan olarak tükenen etkinlikler gizlenir.</summary>
    public bool TukenenleriGoster { get; set; }

    /// <summary>"tarih" | "fiyat-artan" | "fiyat-azalan" | "isim"</summary>
    public string Siralama { get; set; } = "tarih";

    public int Sayfa { get; set; } = 1;

    private int _sayfaBoyutu = VarsayilanSayfaBoyutu;
    public int SayfaBoyutu
    {
        get => _sayfaBoyutu;
        set => _sayfaBoyutu = value is > 0 and <= AzamiSayfaBoyutu ? value : VarsayilanSayfaBoyutu;
    }

    public int GecerliSayfa => Sayfa < 1 ? 1 : Sayfa;

    public bool FiltreVarMi =>
        !string.IsNullOrWhiteSpace(Arama)
        || Kategori.HasValue
        || !string.IsNullOrWhiteSpace(Sehir)
        || Tarih != "tumu"
        || EnYuksekFiyat.HasValue
        || TukenenleriGoster;

    /// <summary>Sayfalama bağlantıları filtreleri koruyarak üretilir.</summary>
    public Dictionary<string, string> BaglantiDegerleri(int sayfa)
    {
        var degerler = new Dictionary<string, string> { ["sayfa"] = sayfa.ToString() };

        if (!string.IsNullOrWhiteSpace(Arama)) degerler["arama"] = Arama;
        if (Kategori.HasValue) degerler["kategori"] = Kategori.Value.ToString();
        if (!string.IsNullOrWhiteSpace(Sehir)) degerler["sehir"] = Sehir;
        if (Tarih != "tumu") degerler["tarih"] = Tarih;
        if (EnYuksekFiyat.HasValue) degerler["enYuksekFiyat"] = EnYuksekFiyat.Value.ToString("0");
        if (TukenenleriGoster) degerler["tukenenleriGoster"] = "true";
        if (Siralama != "tarih") degerler["siralama"] = Siralama;

        return degerler;
    }
}

/// <summary>Sayfalanmış sonuç kümesi.</summary>
public class SayfaliListe<T>
{
    public List<T> Ogeler { get; init; } = new();
    public int Sayfa { get; init; } = 1;
    public int SayfaBoyutu { get; init; } = EtkinlikFiltresi.VarsayilanSayfaBoyutu;
    public int ToplamKayit { get; init; }

    public int ToplamSayfa => SayfaBoyutu <= 0 ? 1 : (int)Math.Ceiling(ToplamKayit / (double)SayfaBoyutu);
    public bool OncekiVarMi => Sayfa > 1;
    public bool SonrakiVarMi => Sayfa < ToplamSayfa;

    public int IlkSira => ToplamKayit == 0 ? 0 : ((Sayfa - 1) * SayfaBoyutu) + 1;
    public int SonSira => Math.Min(Sayfa * SayfaBoyutu, ToplamKayit);
}

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

    /// <summary>
    /// Fiyat tavanı. Bilet fiyatı <c>decimal(10,2)</c> olduğu için sütuna sığmayan
    /// bir değerle karşılaştırma SQL Server'da taşma hatası veriyor ve istek 500 ile
    /// düşüyordu — adres çubuğuna <c>?enYuksekFiyat=99999999999999999999</c> yazan
    /// herkes sunucu hatası üretebiliyordu. Değer sütunun taşıyabileceği aralığa
    /// sıkıştırılıyor; sınırın üstü zaten "hepsi" demek.
    /// </summary>
    public const decimal AzamiFiyat = 99_999_999.99m;

    private decimal? _enYuksekFiyat;
    public decimal? EnYuksekFiyat
    {
        get => _enYuksekFiyat;
        set => _enYuksekFiyat = value is null ? null : Math.Clamp(value.Value, 0m, AzamiFiyat);
    }

    /// <summary>Varsayılan olarak tükenen etkinlikler gizlenir.</summary>
    public bool TukenenleriGoster { get; set; }

    /// <summary>"tarih" | "fiyat-artan" | "fiyat-azalan" | "isim"</summary>
    public string Siralama { get; set; } = "tarih";

    /// <summary>
    /// Sayfa numarasının üst sınırı. Sayfa adres çubuğundan geldiği için sınırsız
    /// olamaz: <c>(sayfa - 1) * sayfaBoyutu</c> çarpımı int sınırını aşınca sessizce
    /// negatife dönüyor ve SQL Server "OFFSET negatif olamaz" diyerek isteği
    /// düşürüyordu. Sınır, en büyük sayfa boyutuyla çarpıldığında bile taşmayacak
    /// kadar küçük; gerçek bir listede bu kadar sayfa zaten olmaz.
    /// </summary>
    public const int AzamiSayfa = 1_000_000;

    public int Sayfa { get; set; } = 1;

    private int _sayfaBoyutu = VarsayilanSayfaBoyutu;
    public int SayfaBoyutu
    {
        get => _sayfaBoyutu;
        set => _sayfaBoyutu = value is > 0 and <= AzamiSayfaBoyutu ? value : VarsayilanSayfaBoyutu;
    }

    public int GecerliSayfa => Math.Clamp(Sayfa, 1, AzamiSayfa);

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

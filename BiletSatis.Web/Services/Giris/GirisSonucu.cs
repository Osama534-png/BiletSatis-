namespace BiletSatis.Web.Services.Giris;

public enum GirisDurumu
{
    /// <summary>İmza tutmuyor ya da bilet bulunamadı.</summary>
    GecersizKod,

    /// <summary>Bilet satılmamış (hâlâ satışta ya da sepette).</summary>
    SatilmamisBilet,

    /// <summary>Giriş bu okutmada onaylandı.</summary>
    GirisOnaylandi,

    /// <summary>Bilet daha önce okutulmuş.</summary>
    ZatenKullanildi
}

public class GirisSonucu
{
    public GirisDurumu Durum { get; init; }

    public int BiletId { get; init; }
    public string KoltukNo { get; init; } = "";
    public decimal Fiyat { get; init; }

    public string EtkinlikAdi { get; init; } = "";
    public string Mekan { get; init; } = "";
    public DateTime EtkinlikTarihi { get; init; }
    public int YasSiniri { get; init; }

    public string SahibiAdi { get; init; } = "";

    /// <summary>Girişin yapıldığı an; "zaten kullanıldı" durumunda önceki okutmanın zamanı.</summary>
    public DateTime? GirisZamani { get; init; }

    public bool BiletBulundu => Durum != GirisDurumu.GecersizKod;
}

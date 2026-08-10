namespace BiletSatis.Web.Services.Devir;

public enum DevirSonucu
{
    Basarili,

    /// <summary>Girilen adrese kayıtlı, doğrulanmış bir hesap yok.</summary>
    AliciBulunamadi,

    /// <summary>Kullanıcı bileti kendisine devretmeye çalıştı.</summary>
    KendinizeDevredemezsiniz,

    /// <summary>Bilet bu kullanıcıya ait değil ya da satın alınmış durumda değil.</summary>
    BiletSizinDegil,

    /// <summary>Bilet kapıda okutulmuş; devredilemez.</summary>
    GirisYapilmis,

    /// <summary>Etkinlik başlamış ya da geçmiş.</summary>
    EtkinlikGecmis
}

public interface IBiletDevirServisi
{
    /// <summary>
    /// Satın alınmış bir bileti başka bir kullanıcıya devreder. Devir sonrası biletin
    /// QR kod sürümü artar; eski sahibin elindeki kod kapıda geçersiz olur ve yeni
    /// sahibe yeni QR'lı bilet e-postası gönderilir.
    /// </summary>
    Task<DevirSonucu> DevretAsync(int biletId, string devredenKullaniciId, string aliciEposta, CancellationToken ct = default);
}

namespace BiletSatis.Web.Models;

/// <summary>Popülerlik listesindeki bir satır: etkinlik kartı + satış sayıları.</summary>
public class PopulerEtkinlikVm
{
    public EtkinlikKartVm Kart { get; set; } = new();

    /// <summary>
    /// Sıralamayı belirleyen sayı: seçili dönemde satılan bilet.
    /// "Tüm zamanlar" döneminde <see cref="ToplamSatilan"/> ile aynı olur.
    /// </summary>
    public int SatilanBilet { get; set; }

    /// <summary>
    /// Etkinliğin bugüne kadar satılmış toplam bileti — dönemden bağımsız.
    /// Doluluk bundan hesaplanır; dönem satışını kapasiteye bölmek yanıltıcı olurdu
    /// ("son 7 günde 10 bilet sattı" ile "kapasitesinin %10'u dolu" aynı şey değil).
    /// </summary>
    public int ToplamSatilan { get; set; }

    /// <summary>Etkinliğin toplam bilet sayısı (kapasite).</summary>
    public int ToplamBilet { get; set; }

    /// <summary>
    /// Etkinliğin ne kadarının satıldığı. Satış sayısıyla birlikte okunmalı:
    /// 40 koltuklu bir salonun tamamını satmak, 5000 kişilik arenanın yarısını
    /// satmaktan farklı bir başarıdır.
    /// </summary>
    public int DolulukYuzdesi => ToplamBilet <= 0
        ? 0
        : (int)Math.Round(ToplamSatilan * 100.0 / ToplamBilet);
}

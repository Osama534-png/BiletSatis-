using BiletSatis.Web.Models;

namespace BiletSatis.Web.Services.Etkinlikler;

/// <summary>Ana sayfadaki sayaçlar. Nadiren değiştiği için kısa süreli önbelleklenir.</summary>
public sealed record AnaSayfaIstatistigi(int ToplamEtkinlik, int SatistakiBilet, int KuyruktaBekleyen);

public interface IEtkinlikSorguServisi
{
    /// <summary>Filtrelenmiş, sıralanmış ve sayfalanmış etkinlik listesi.</summary>
    Task<SayfaliListe<EtkinlikKartVm>> AraAsync(EtkinlikFiltresi filtre, CancellationToken ct = default);

    /// <summary>Şehir seçicide listelenecek şehirler.</summary>
    Task<List<string>> SehirlerAsync(CancellationToken ct = default);

    Task<AnaSayfaIstatistigi> IstatistikAsync(CancellationToken ct = default);

    /// <summary>Fiyat kaydırıcısının üst sınırı.</summary>
    Task<decimal> FiyatTavaniAsync(CancellationToken ct = default);
}

namespace BiletSatis.Web.Services.Favoriler;

/// <summary>Kalp düğmesine basıldığında ne olduğu.</summary>
public enum FavoriDurumu { Eklendi, Cikarildi }

public interface IFavoriServisi
{
    /// <summary>
    /// Etkinlik favorideyse çıkarır, değilse ekler. Tek bir düğme hem ekleme hem
    /// çıkarma yaptığı için işlem bu şekilde birleşik.
    /// </summary>
    Task<FavoriDurumu> DegistirAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default);

    /// <summary>Kullanıcının favori etkinlik numaraları — kartlarda kalbin dolu mu boş mu olacağını belirler.</summary>
    Task<HashSet<int>> FavoriIdleriAsync(string kullaniciId, CancellationToken ct = default);

    Task<bool> FavorideMiAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default);
}

namespace BiletSatis.Web.Services;

public enum SepeteEklemeSonucu { Basarili, ZatenAlinmis }

/// <summary>
/// Çoklu koltuk rezervasyonunun sonucu. Başarısızsa <see cref="AlinamayanKoltuklar"/>
/// hangi koltukların araya girildiğini söyler; hiçbir koltuk sepete eklenmemiştir.
/// </summary>
public sealed record CokluSepeteEklemeSonucu(bool Basarili, IReadOnlyList<string> AlinamayanKoltuklar)
{
    public static CokluSepeteEklemeSonucu Olumlu() => new(true, Array.Empty<string>());
    public static CokluSepeteEklemeSonucu Olumsuz(IReadOnlyList<string> alinamayanlar) => new(false, alinamayanlar);
}

public interface IBiletRezervasyonServisi
{
    Task<SepeteEklemeSonucu> TryAddToCartAsync(int biletId, string kullaniciId, CancellationToken ct = default);

    /// <summary>
    /// Birden çok koltuğu tek işlemde rezerve eder. Koltuklardan biri bile alınamazsa
    /// hiçbiri alınmaz — yan yana oturmak isteyen kullanıcı yarım bir sepetle kalmaz.
    /// </summary>
    Task<CokluSepeteEklemeSonucu> TryAddManyToCartAsync(IReadOnlyCollection<int> biletIdleri, string kullaniciId, CancellationToken ct = default);

    Task<int> ReleaseExpiredCartHoldsAsync(CancellationToken ct = default);

    /// <summary>
    /// Sepetteki kilitlerin süresini uzatır. Kullanıcı Stripe sayfasındayken kilidin
    /// düşüp koltuğun başkasına satılmasını engeller.
    /// </summary>
    Task<int> ExtendCartHoldsAsync(IReadOnlyCollection<int> biletIdleri, string kullaniciId, int dakika, CancellationToken ct = default);

    Task<bool> CompletePaymentAsync(int biletId, string kullaniciId, CancellationToken ct = default);

    /// <summary>Ödemesi alınan biletleri satıldı olarak işaretler, kaç tanesinin işaretlendiğini döner.</summary>
    Task<int> CompletePaymentManyAsync(IReadOnlyCollection<int> biletIdleri, string kullaniciId, CancellationToken ct = default);

    Task<bool> CancelReservationAsync(int biletId, string kullaniciId, CancellationToken ct = default);
}

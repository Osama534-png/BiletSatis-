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

/// <summary>
/// Ödeme tamamlama sonucu. <paramref name="SahipOlunan"/> kullanıcının gerçekten
/// sahip olduğu bilet sayısı; <paramref name="CifteOdenenKoltuklar"/> daha önce başka
/// bir ödeme oturumuyla satın alınmış, yani ikinci kez parası alınmış koltuklar.
/// </summary>
public sealed record OdemeTamamlamaSonucu(int SahipOlunan, IReadOnlyList<string> CifteOdenenKoltuklar);

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

    /// <summary>
    /// Ödemesi alınan biletleri satıldı olarak işaretler. <paramref name="odemeReferansi"/>
    /// biletle birlikte saklanır; aynı koltuk daha önce başka bir ödeme oturumuyla
    /// satılmışsa çifte ödeme olarak raporlanır.
    /// </summary>
    Task<OdemeTamamlamaSonucu> CompletePaymentManyAsync(
        IReadOnlyCollection<int> biletIdleri, string kullaniciId, string? odemeReferansi, CancellationToken ct = default);

    Task<bool> CancelReservationAsync(int biletId, string kullaniciId, CancellationToken ct = default);
}

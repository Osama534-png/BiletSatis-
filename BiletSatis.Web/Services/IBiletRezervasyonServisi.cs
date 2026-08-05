namespace BiletSatis.Web.Services;

public enum SepeteEklemeSonucu { Basarili, ZatenAlinmis }

public interface IBiletRezervasyonServisi
{
    Task<SepeteEklemeSonucu> TryAddToCartAsync(int biletId, string kullaniciId, CancellationToken ct = default);
    Task<int> ReleaseExpiredCartHoldsAsync(CancellationToken ct = default);
    Task<bool> CompletePaymentAsync(int biletId, string kullaniciId, CancellationToken ct = default);
    Task<bool> CancelReservationAsync(int biletId, string kullaniciId, CancellationToken ct = default);
}

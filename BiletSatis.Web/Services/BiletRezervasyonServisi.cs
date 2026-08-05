using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services;

public class BiletRezervasyonServisi : IBiletRezervasyonServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly ILogger<BiletRezervasyonServisi> _logger;

    public BiletRezervasyonServisi(BiletSatisDbContext db, ILogger<BiletRezervasyonServisi> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<SepeteEklemeSonucu> TryAddToCartAsync(int biletId, string kullaniciId, CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Sepette},
                KilitBitisZamani = DATEADD(MINUTE, 5, GETUTCDATE()),
                RezerveEdenKullaniciId = {kullaniciId}
            WHERE Id = {biletId} AND Durum = {BiletDurumMetni.Satista}
            """, ct);

        var sonuc = etkilenen == 1 ? SepeteEklemeSonucu.Basarili : SepeteEklemeSonucu.ZatenAlinmis;

        _logger.LogInformation(
            "Sepete ekleme denemesi: BiletId={BiletId} KullaniciId={KullaniciId} Sonuc={Sonuc}",
            biletId, kullaniciId, sonuc);

        return sonuc;
    }

    public async Task<int> ReleaseExpiredCartHoldsAsync(CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Satista},
                RezerveEdenKullaniciId = NULL,
                KilitBitisZamani = NULL
            WHERE Durum = {BiletDurumMetni.Sepette} AND KilitBitisZamani < GETUTCDATE()
            """, ct);

        return etkilenen;
    }

    public async Task<bool> CompletePaymentAsync(int biletId, string kullaniciId, CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Satildi},
                KilitBitisZamani = NULL
            WHERE Id = {biletId} AND Durum = {BiletDurumMetni.Sepette} AND RezerveEdenKullaniciId = {kullaniciId}
            """, ct);

        var basarili = etkilenen == 1;
        _logger.LogInformation(
            "Ödeme sonucu: BiletId={BiletId} KullaniciId={KullaniciId} Basarili={Basarili}",
            biletId, kullaniciId, basarili);

        return basarili;
    }

    public async Task<bool> CancelReservationAsync(int biletId, string kullaniciId, CancellationToken ct = default)
    {
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET Durum = {BiletDurumMetni.Satista},
                RezerveEdenKullaniciId = NULL,
                KilitBitisZamani = NULL
            WHERE Id = {biletId} AND Durum = {BiletDurumMetni.Sepette} AND RezerveEdenKullaniciId = {kullaniciId}
            """, ct);

        return etkilenen == 1;
    }
}

using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services.Favoriler;

public class FavoriServisi : IFavoriServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly ILogger<FavoriServisi> _logger;

    public FavoriServisi(BiletSatisDbContext db, ILogger<FavoriServisi> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<FavoriDurumu> DegistirAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default)
    {
        // Önce silmeyi deniyoruz: kayıt varsa çıkarılır ve iş biter. "Var mı" diye
        // sorup sonra silmek gerekmiyor, silmenin kendisi zaten koşullu.
        var silinen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM Favoriler
            WHERE EtkinlikId = {etkinlikId} AND KullaniciId = {kullaniciId}
            """, ct);

        if (silinen > 0) return FavoriDurumu.Cikarildi;

        // Kayıt yoktu, ekliyoruz. Çift tıklama ya da iki sekmeden eşzamanlı istek
        // gelirse bileşik birincil anahtar ikinci eklemeyi reddeder; bu bir hata
        // değil, "zaten favoride" demektir.
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Favoriler (KullaniciId, EtkinlikId, EklenmeZamani)
                SELECT {kullaniciId}, {etkinlikId}, GETUTCDATE()
                WHERE NOT EXISTS (
                    SELECT 1 FROM Favoriler WITH (UPDLOCK, HOLDLOCK)
                    WHERE EtkinlikId = {etkinlikId} AND KullaniciId = {kullaniciId}
                )
                """, ct);
        }
        // 2627/2601: birincil anahtar ya da benzersiz dizin ihlali. Ham SQL çalıştığı
        // için hata SqlException olarak gelir — DbUpdateException yalnızca SaveChanges
        // yolunda fırlar, o yüzden buradaki eski catch hiçbir zaman devreye girmiyordu.
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            _logger.LogWarning(ex, "Favori zaten ekliydi: EtkinlikId={EtkinlikId}", etkinlikId);
        }

        return FavoriDurumu.Eklendi;
    }

    public async Task<HashSet<int>> FavoriIdleriAsync(string kullaniciId, CancellationToken ct = default) =>
        (await _db.Favoriler
            .AsNoTracking()
            .Where(f => f.KullaniciId == kullaniciId)
            .Select(f => f.EtkinlikId)
            .ToListAsync(ct))
        .ToHashSet();

    public Task<bool> FavorideMiAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default) =>
        _db.Favoriler
            .AsNoTracking()
            .AnyAsync(f => f.EtkinlikId == etkinlikId && f.KullaniciId == kullaniciId, ct);
}

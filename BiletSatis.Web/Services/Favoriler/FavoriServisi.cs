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

        // Kayıt yoktu, ekliyoruz.
        //
        // İki koşul da INSERT'ün kendi içinde:
        //  - Etkinlik gerçekten var mı? Yoksa foreign key ihlali fırlıyor ve istek
        //    500 ile düşüyordu. Adres çubuğuna olmayan bir numara yazan herkes
        //    sunucu hatası üretebiliyordu.
        //  - Zaten favoride mi? Çift tıklama ya da iki sekmeden eşzamanlı istek
        //    gelirse ikinci ekleme sessizce atlanır; bu bir hata değil.
        //
        // Kontrolleri ayrı sorgulara bölmek yarış durumu açardı: "etkinlik var" deyip
        // araya silme girdiğinde INSERT yine patlardı.
        int eklenen;
        try
        {
            eklenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Favoriler (KullaniciId, EtkinlikId, EklenmeZamani)
                SELECT {kullaniciId}, {etkinlikId}, GETUTCDATE()
                WHERE EXISTS (SELECT 1 FROM Etkinlikler WHERE Id = {etkinlikId})
                  AND NOT EXISTS (
                    SELECT 1 FROM Favoriler WITH (UPDLOCK, HOLDLOCK)
                    WHERE EtkinlikId = {etkinlikId} AND KullaniciId = {kullaniciId}
                )
                """, ct);
        }
        // 2627/2601: birincil anahtar / benzersiz dizin, 547: foreign key. Ham SQL
        // çalıştığı için hata SqlException olarak gelir — DbUpdateException yalnızca
        // SaveChanges yolunda fırlar. Yukarıdaki koşullar bu üçünü de karşılıyor;
        // buradaki yakalama, etkinlik tam o anda silinirse diye son savunma.
        catch (SqlException ex) when (ex.Number is 2627 or 2601 or 547)
        {
            _logger.LogWarning(ex, "Favori eklenemedi: EtkinlikId={EtkinlikId}", etkinlikId);
            return FavoriDurumu.Cikarildi;
        }

        // Hiçbir satır eklenmediyse etkinlik yok demektir (zaten favorideyse yukarıdaki
        // DELETE onu çıkarmış olurdu). Kullanıcıya "eklendi" demek yanlış olurdu.
        return eklenen > 0 ? FavoriDurumu.Eklendi : FavoriDurumu.Cikarildi;
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

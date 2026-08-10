using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services.Devir;

public class BiletDevirServisi : IBiletDevirServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly ILogger<BiletDevirServisi> _logger;

    public BiletDevirServisi(BiletSatisDbContext db, ILogger<BiletDevirServisi> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<DevirSonucu> DevretAsync(
        int biletId, string devredenKullaniciId, string aliciEposta, CancellationToken ct = default)
    {
        var alici = await _db.Users
            .AsNoTracking()
            .Where(u => u.NormalizedEmail == (aliciEposta ?? "").Trim().ToUpperInvariant() && u.EmailConfirmed)
            .Select(u => new { u.Id, u.Ad })
            .FirstOrDefaultAsync(ct);

        if (alici == null) return DevirSonucu.AliciBulunamadi;
        if (alici.Id == devredenKullaniciId) return DevirSonucu.KendinizeDevredemezsiniz;

        var bilet = await _db.Biletler
            .AsNoTracking()
            .Where(b => b.Id == biletId)
            .Select(b => new { b.Durum, b.RezerveEdenKullaniciId, b.GirisYapildi, EtkinlikTarihi = b.Etkinlik!.Tarih })
            .FirstOrDefaultAsync(ct);

        if (bilet == null || bilet.Durum != BiletDurumu.Satildi || bilet.RezerveEdenKullaniciId != devredenKullaniciId)
        {
            return DevirSonucu.BiletSizinDegil;
        }

        if (bilet.GirisYapildi) return DevirSonucu.GirisYapilmis;

        // Etkinlik tarihi takvim saatidir (yönetici "20:00" yazar, kullanıcı "20:00"
        // görür), bir an değil — bu yüzden yerel saatle karşılaştırılır. UtcNow ile
        // karşılaştırıldığında Türkiye'de 20:00 başlayan bir etkinlik saat 23:00'e
        // kadar "henüz başlamadı" sayılıyordu; devir üç saat fazladan açık kalıyordu.
        // Sepet kilidi, giriş zamanı gibi gerçek anlar UTC'dir; ikisi farklı türde değer.
        if (bilet.EtkinlikTarihi <= DateTime.Now) return DevirSonucu.EtkinlikGecmis;

        // Asıl devir tek atomik UPDATE. Yukarıdaki kontroller kullanıcıya anlaşılır
        // mesaj vermek için; karar burada veriliyor. Böylece iki sekmeden aynı anda
        // devir denenirse yalnızca biri tutar, ve bilet tam o anda kapıda okutulursa
        // (GirisYapildi = 1) devir gerçekleşmez.
        //
        // KodSurumu artırılıyor: eski sahibin elindeki QR imzası artık tutmaz.
        // BildirimGonderildi sıfırlanıyor: arka plan görevi yeni sahibe yeni QR'lı
        // bilet e-postasını gönderir.
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET RezerveEdenKullaniciId = {alici.Id},
                KodSurumu = KodSurumu + 1,
                BildirimGonderildi = 0,
                BildirimKilitZamani = NULL
            WHERE Id = {biletId}
              AND Durum = {BiletDurumMetni.Satildi}
              AND RezerveEdenKullaniciId = {devredenKullaniciId}
              AND GirisYapildi = 0
            """, ct);

        if (etkilenen != 1)
        {
            _logger.LogWarning(
                "Bilet devri tutmadı: BiletId={BiletId} Devreden={Devreden}", biletId, devredenKullaniciId);

            return DevirSonucu.BiletSizinDegil;
        }

        _logger.LogInformation(
            "Bilet devredildi: BiletId={BiletId} Devreden={Devreden} Alici={Alici}",
            biletId, devredenKullaniciId, alici.Id);

        return DevirSonucu.Basarili;
    }
}

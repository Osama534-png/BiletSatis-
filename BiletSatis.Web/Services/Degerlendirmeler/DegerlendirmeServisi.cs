using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services.Degerlendirmeler;

public class DegerlendirmeServisi : IDegerlendirmeServisi
{
    /// <summary>Etkinlik sayfasında gösterilen en fazla yorum sayısı.</summary>
    private const int GosterilecekYorumSayisi = 20;

    private readonly BiletSatisDbContext _db;
    private readonly ILogger<DegerlendirmeServisi> _logger;

    public DegerlendirmeServisi(BiletSatisDbContext db, ILogger<DegerlendirmeServisi> logger)
    {
        _db = db;
        _logger = logger;
    }

    public Task<bool> DegerlendirebilirMiAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default) =>
        _db.Biletler
            .AsNoTracking()
            .AnyAsync(b => b.EtkinlikId == etkinlikId
                        && b.RezerveEdenKullaniciId == kullaniciId
                        && b.Durum == BiletDurumu.Satildi
                        && b.GirisYapildi, ct);

    public async Task<DegerlendirmeSonucu> KaydetAsync(
        int etkinlikId, string kullaniciId, int puan, string? yorum, CancellationToken ct = default)
    {
        if (!Degerlendirme.PuanGecerli(puan)) return DegerlendirmeSonucu.GecersizPuan;

        // Hak kontrolü arayüzde de yapılıyor ama asıl kontrol burada: butonu gizlemek
        // yeterli değil, form doğrudan da gönderilebilir.
        if (!await DegerlendirebilirMiAsync(etkinlikId, kullaniciId, ct))
        {
            _logger.LogWarning(
                "Değerlendirme hakkı olmayan kullanıcı denedi: EtkinlikId={EtkinlikId} KullaniciId={KullaniciId}",
                etkinlikId, kullaniciId);

            return DegerlendirmeSonucu.KatilimYok;
        }

        var temizYorum = (yorum ?? "").Trim();
        if (temizYorum.Length > Degerlendirme.EnUzunYorum)
        {
            temizYorum = temizYorum[..Degerlendirme.EnUzunYorum];
        }

        var mevcut = await _db.Degerlendirmeler
            .FirstOrDefaultAsync(d => d.EtkinlikId == etkinlikId && d.KullaniciId == kullaniciId, ct);

        if (mevcut != null)
        {
            mevcut.Puan = puan;
            mevcut.Yorum = temizYorum;
            mevcut.GuncellemeZamani = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Değerlendirme güncellendi: EtkinlikId={EtkinlikId} KullaniciId={KullaniciId} Puan={Puan}",
                etkinlikId, kullaniciId, puan);

            return DegerlendirmeSonucu.Guncellendi;
        }

        _db.Degerlendirmeler.Add(new Degerlendirme
        {
            EtkinlikId = etkinlikId,
            KullaniciId = kullaniciId,
            Puan = puan,
            Yorum = temizYorum,
            OlusturmaZamani = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Aynı kullanıcının iki isteği aynı anda gelmiş olabilir; benzersiz dizin
            // ikincisini reddeder. Kayıt gerçekten duruyorsa bunu güncelleme sayıyoruz,
            // durmuyorsa hata bizim beklediğimiz durum değildir ve yukarı taşınmalı.
            _db.ChangeTracker.Clear();

            if (!await ZatenVarMiAsync(etkinlikId, kullaniciId, ct)) throw;

            return DegerlendirmeSonucu.Guncellendi;
        }

        _logger.LogInformation(
            "Değerlendirme eklendi: EtkinlikId={EtkinlikId} KullaniciId={KullaniciId} Puan={Puan}",
            etkinlikId, kullaniciId, puan);

        return DegerlendirmeSonucu.Kaydedildi;
    }

    private Task<bool> ZatenVarMiAsync(int etkinlikId, string kullaniciId, CancellationToken ct) =>
        _db.Degerlendirmeler
            .AsNoTracking()
            .AnyAsync(d => d.EtkinlikId == etkinlikId && d.KullaniciId == kullaniciId, ct);

    public async Task<DegerlendirmeOzeti> OzetAsync(int etkinlikId, CancellationToken ct = default)
    {
        var puanlar = await _db.Degerlendirmeler
            .AsNoTracking()
            .Where(d => d.EtkinlikId == etkinlikId)
            .Select(d => d.Puan)
            .ToListAsync(ct);

        if (puanlar.Count == 0)
        {
            return new DegerlendirmeOzeti { Adet = 0, Ortalama = null };
        }

        var satirlar = await _db.Degerlendirmeler
            .AsNoTracking()
            .Where(d => d.EtkinlikId == etkinlikId)
            .OrderByDescending(d => d.GuncellemeZamani ?? d.OlusturmaZamani)
            .Take(GosterilecekYorumSayisi)
            .Select(d => new DegerlendirmeSatiri
            {
                KullaniciId = d.KullaniciId,
                KullaniciAdi = _db.Users
                    .Where(u => u.Id == d.KullaniciId)
                    .Select(u => u.Ad)
                    .FirstOrDefault() ?? "",
                Puan = d.Puan,
                Yorum = d.Yorum,
                Zaman = d.GuncellemeZamani ?? d.OlusturmaZamani,
                Duzenlendi = d.GuncellemeZamani != null
            })
            .ToListAsync(ct);

        var dagilim = Enumerable
            .Range(Degerlendirme.EnDusukPuan, Degerlendirme.EnYuksekPuan)
            .ToDictionary(puan => puan, puan => puanlar.Count(p => p == puan));

        return new DegerlendirmeOzeti
        {
            Adet = puanlar.Count,
            Ortalama = Math.Round((decimal)puanlar.Average(), 1),
            Dagilim = dagilim,
            Satirlar = satirlar
        };
    }

    public async Task<DegerlendirmeSatiri?> KendiDegerlendirmesiAsync(
        int etkinlikId, string kullaniciId, CancellationToken ct = default) =>
        await _db.Degerlendirmeler
            .AsNoTracking()
            .Where(d => d.EtkinlikId == etkinlikId && d.KullaniciId == kullaniciId)
            .Select(d => new DegerlendirmeSatiri
            {
                KullaniciId = d.KullaniciId,
                Puan = d.Puan,
                Yorum = d.Yorum,
                Zaman = d.GuncellemeZamani ?? d.OlusturmaZamani,
                Duzenlendi = d.GuncellemeZamani != null
            })
            .FirstOrDefaultAsync(ct);
}

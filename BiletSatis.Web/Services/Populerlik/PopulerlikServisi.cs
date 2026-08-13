using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace BiletSatis.Web.Services.Populerlik;

/// <summary>
/// "En çok satanlar" ve trend listeleri.
///
/// <para><b>Sıralama neden veritabanında?</b> Ana sayfa sayfalamasındaki dersin
/// aynısı: bütün biletleri çekip C# tarafında gruplamak, bilet sayısı büyüdükçe
/// (ölçümde 202.367 bilet) doğrudan yanıt süresine yansırdı. Gruplama ve sıralama
/// SQL'de yapılıp yalnızca ilk N etkinlik okunuyor.</para>
///
/// <para><b>Neden önbellek?</b> Liste ana sayfada her görüntülemede gösteriliyor ve
/// bir satış olduğunda 30 saniye bayat kalması kimseye bir şey kaybettirmiyor —
/// aynı gerekçeyle ana sayfa sayaçları da önbellekleniyor.</para>
/// </summary>
public class PopulerlikServisi : IPopulerlikServisi
{
    private static readonly TimeSpan OnbellekSuresi = TimeSpan.FromSeconds(30);

    private readonly BiletSatisDbContext _db;
    private readonly IMemoryCache _onbellek;

    public PopulerlikServisi(BiletSatisDbContext db, IMemoryCache onbellek)
    {
        _db = db;
        _onbellek = onbellek;
    }

    public Task<List<PopulerEtkinlikVm>> EnCokSatanlarAsync(
        PopulerlikDonemi donem, int adet, CancellationToken ct = default)
    {
        if (adet <= 0) return Task.FromResult(new List<PopulerEtkinlikVm>());

        return _onbellek.GetOrCreateAsync($"populer:{donem.Anahtar()}:{adet}", async giris =>
        {
            giris.AbsoluteExpirationRelativeToNow = OnbellekSuresi;
            return await HesaplaAsync(donem, adet, ct);
        })!;
    }

    private async Task<List<PopulerEtkinlikVm>> HesaplaAsync(
        PopulerlikDonemi donem, int adet, CancellationToken ct)
    {
        // Etkinlik tarihi takvim saatidir (Now), satış zamanı ise gerçek bir andır
        // (UtcNow). İkisi farklı türde; karıştırılırsa hata sessiz gelir.
        // Bkz. README → Zamanın iki türü.
        var simdi = DateTime.Now;

        var satislar = _db.Biletler
            .AsNoTracking()
            .Where(b => b.Durum == BiletDurumu.Satildi && b.Etkinlik!.Tarih > simdi);

        var gun = donem.GunSayisi();
        if (gun.HasValue)
        {
            // Satış zamanı bilinmeyen eski kayıtlar (sütun eklenmeden önce satılmış
            // biletler) dönem sıralamasına girmez — uydurulmuş bir tarih listeyi
            // olmayan bir satış hareketiyle doldururdu.
            var esik = DateTime.UtcNow.AddDays(-gun.Value);
            satislar = satislar.Where(b => b.SatisZamani != null && b.SatisZamani >= esik);
        }

        var siralama = await satislar
            .GroupBy(b => b.EtkinlikId)
            .Select(g => new { EtkinlikId = g.Key, Satilan = g.Count() })
            .OrderByDescending(x => x.Satilan)
            .ThenBy(x => x.EtkinlikId)
            .Take(adet)
            .ToListAsync(ct);

        if (siralama.Count == 0) return new List<PopulerEtkinlikVm>();

        var idler = siralama.Select(x => x.EtkinlikId).ToList();

        var kartlar = await _db.Etkinlikler
            .AsNoTracking()
            .Where(e => idler.Contains(e.Id))
            .Select(EtkinlikKartVm.Projeksiyon)
            .ToListAsync(ct);

        // Kapasite ve toplam satış: doluluk oranı dönemden bağımsız olmalı.
        var sayilar = await _db.Biletler
            .AsNoTracking()
            .Where(b => idler.Contains(b.EtkinlikId))
            .GroupBy(b => b.EtkinlikId)
            .Select(g => new
            {
                EtkinlikId = g.Key,
                Toplam = g.Count(),
                Satilan = g.Count(b => b.Durum == BiletDurumu.Satildi)
            })
            .ToListAsync(ct);

        var kartHaritasi = kartlar.ToDictionary(k => k.Id);
        var sayiHaritasi = sayilar.ToDictionary(s => s.EtkinlikId);

        // Sıra SQL'de belirlendi; sözlükten okurken o sırayı koruyoruz.
        var sonuc = new List<PopulerEtkinlikVm>(siralama.Count);
        foreach (var satir in siralama)
        {
            if (!kartHaritasi.TryGetValue(satir.EtkinlikId, out var kart)) continue;

            sayiHaritasi.TryGetValue(satir.EtkinlikId, out var sayi);

            sonuc.Add(new PopulerEtkinlikVm
            {
                Kart = kart,
                SatilanBilet = satir.Satilan,
                ToplamSatilan = sayi?.Satilan ?? satir.Satilan,
                ToplamBilet = sayi?.Toplam ?? 0
            });
        }

        return sonuc;
    }
}

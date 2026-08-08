using BiletSatis.Web.Data;
using BiletSatis.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Web.Services.Giris;

public class GirisServisi : IGirisServisi
{
    private readonly BiletSatisDbContext _db;
    private readonly IBiletKoduServisi _biletKodu;
    private readonly ILogger<GirisServisi> _logger;

    public GirisServisi(
        BiletSatisDbContext db,
        IBiletKoduServisi biletKodu,
        ILogger<GirisServisi> logger)
    {
        _db = db;
        _biletKodu = biletKodu;
        _logger = logger;
    }

    public async Task<GirisSonucu> DurumSorgulaAsync(string? kod, CancellationToken ct = default)
    {
        var biletId = _biletKodu.BiletIdCoz(kod);
        if (biletId == null) return Gecersiz();

        var bilgi = await BiletBilgisiAsync(biletId.Value, ct);
        if (bilgi == null) return Gecersiz();

        var durum = bilgi.Durum != BiletDurumu.Satildi
            ? GirisDurumu.SatilmamisBilet
            : bilgi.GirisYapildi ? GirisDurumu.ZatenKullanildi : GirisDurumu.GirisOnaylandi;

        // Sorgulama girişi onaylamaz; "onaylanabilir" durumu göstermek için
        // GirisOnaylandi kullanılır, kayıt değişmez.
        return SonucOlustur(bilgi, durum);
    }

    public async Task<GirisSonucu> GirisiOnaylaAsync(string? kod, CancellationToken ct = default)
    {
        var biletId = _biletKodu.BiletIdCoz(kod);
        if (biletId == null) return Gecersiz();

        // Tek atomik UPDATE: "henüz giriş yapılmamışsa işaretle". Aynı bileti iki
        // görevli aynı anda okutursa etkilenen satır sayısı yalnızca birinde 1 olur.
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET GirisYapildi = 1, GirisZamani = GETUTCDATE()
            WHERE Id = {biletId.Value}
              AND Durum = {BiletDurumMetni.Satildi}
              AND GirisYapildi = 0
            """, ct);

        var bilgi = await BiletBilgisiAsync(biletId.Value, ct);
        if (bilgi == null) return Gecersiz();

        if (etkilenen == 1)
        {
            _logger.LogInformation("Giriş onaylandı: BiletId={BiletId} Koltuk={Koltuk}", bilgi.Id, bilgi.KoltukNo);
            return SonucOlustur(bilgi, GirisDurumu.GirisOnaylandi);
        }

        // Güncelleme tutmadı: ya bilet satılmamış ya da giriş zaten yapılmış.
        var durum = bilgi.Durum != BiletDurumu.Satildi
            ? GirisDurumu.SatilmamisBilet
            : GirisDurumu.ZatenKullanildi;

        _logger.LogWarning("Giriş onaylanamadı: BiletId={BiletId} Durum={Durum}", bilgi.Id, durum);
        return SonucOlustur(bilgi, durum);
    }

    private async Task<BiletBilgisi?> BiletBilgisiAsync(int biletId, CancellationToken ct) =>
        await _db.Biletler
            .AsNoTracking()
            .Where(b => b.Id == biletId)
            .Select(b => new BiletBilgisi
            {
                Id = b.Id,
                KoltukNo = b.KoltukNo,
                Fiyat = b.Fiyat,
                Durum = b.Durum,
                GirisYapildi = b.GirisYapildi,
                GirisZamani = b.GirisZamani,
                EtkinlikAdi = b.Etkinlik!.Ad,
                Mekan = b.Etkinlik.Mekan,
                EtkinlikTarihi = b.Etkinlik.Tarih,
                YasSiniri = b.Etkinlik.YasSiniri,
                SahibiAdi = _db.Users
                    .Where(u => u.Id == b.RezerveEdenKullaniciId)
                    .Select(u => u.Ad)
                    .FirstOrDefault() ?? ""
            })
            .FirstOrDefaultAsync(ct);

    private static GirisSonucu Gecersiz() => new() { Durum = GirisDurumu.GecersizKod };

    private static GirisSonucu SonucOlustur(BiletBilgisi bilgi, GirisDurumu durum) => new()
    {
        Durum = durum,
        BiletId = bilgi.Id,
        KoltukNo = bilgi.KoltukNo,
        Fiyat = bilgi.Fiyat,
        EtkinlikAdi = bilgi.EtkinlikAdi,
        Mekan = bilgi.Mekan,
        EtkinlikTarihi = bilgi.EtkinlikTarihi,
        YasSiniri = bilgi.YasSiniri,
        SahibiAdi = bilgi.SahibiAdi,
        GirisZamani = bilgi.GirisZamani
    };

    private sealed class BiletBilgisi
    {
        public int Id { get; init; }
        public string KoltukNo { get; init; } = "";
        public decimal Fiyat { get; init; }
        public BiletDurumu Durum { get; init; }
        public bool GirisYapildi { get; init; }
        public DateTime? GirisZamani { get; init; }
        public string EtkinlikAdi { get; init; } = "";
        public string Mekan { get; init; } = "";
        public DateTime EtkinlikTarihi { get; init; }
        public int YasSiniri { get; init; }
        public string SahibiAdi { get; init; } = "";
    }
}

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
        var cozulen = _biletKodu.Coz(kod);
        if (cozulen == null) return Gecersiz();

        var bilgi = await BiletBilgisiAsync(cozulen.BiletId, ct);
        if (bilgi == null) return Gecersiz();

        // Bilet devredildiyse sürüm artmıştır; eski sahibin kodu artık geçerli değil.
        if (bilgi.KodSurumu != cozulen.KodSurumu) return Gecersiz();

        var durum = bilgi.Durum != BiletDurumu.Satildi
            ? GirisDurumu.SatilmamisBilet
            : bilgi.GirisYapildi ? GirisDurumu.ZatenKullanildi : GirisDurumu.GirisOnaylandi;

        // Sorgulama girişi onaylamaz; "onaylanabilir" durumu göstermek için
        // GirisOnaylandi kullanılır, kayıt değişmez.
        return SonucOlustur(bilgi, durum);
    }

    public async Task<GirisSonucu> GirisiOnaylaAsync(string? kod, CancellationToken ct = default)
    {
        var cozulen = _biletKodu.Coz(kod);
        if (cozulen == null) return Gecersiz();

        // Tek atomik UPDATE: "henüz giriş yapılmamışsa işaretle". Aynı bileti iki
        // görevli aynı anda okutursa etkilenen satır sayısı yalnızca birinde 1 olur.
        // Kod sürümü de koşula dahil: bilet okutma anında devredilmişse sürüm artar
        // ve elindeki eski kodla giriş yapılamaz.
        var etkilenen = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE Biletler
            SET GirisYapildi = 1, GirisZamani = GETUTCDATE()
            WHERE Id = {cozulen.BiletId}
              AND Durum = {BiletDurumMetni.Satildi}
              AND GirisYapildi = 0
              AND KodSurumu = {cozulen.KodSurumu}
            """, ct);

        var bilgi = await BiletBilgisiAsync(cozulen.BiletId, ct);
        if (bilgi == null) return Gecersiz();

        if (bilgi.KodSurumu != cozulen.KodSurumu) return Gecersiz();

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
                KodSurumu = b.KodSurumu,
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
        public int KodSurumu { get; init; }
        public bool GirisYapildi { get; init; }
        public DateTime? GirisZamani { get; init; }
        public string EtkinlikAdi { get; init; } = "";
        public string Mekan { get; init; } = "";
        public DateTime EtkinlikTarihi { get; init; }
        public int YasSiniri { get; init; }
        public string SahibiAdi { get; init; } = "";
    }
}

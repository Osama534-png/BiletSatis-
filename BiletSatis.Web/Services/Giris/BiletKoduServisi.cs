using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BiletSatis.Web.Services.Giris;

/// <summary>
/// Bilet QR kodunu HMAC-SHA256 ile imzalar.
///
/// İmza olmasaydı QR'daki adres "/Bilet/Dogrula?kod=1399" olurdu ve herkes
/// numarayı artırarak başkasının biletini "kullanıldı" işaretleyebilirdi.
/// İmza gizli anahtarla üretildiğinden anahtarı bilmeyen geçerli kod üretemez.
///
/// Koda ayrıca bir <b>sürüm</b> girer. Bilet başkasına devredildiğinde sürüm artar;
/// böylece eski sahibin elindeki QR geçersizleşir. Sürüm imzanın içinde olduğu için
/// kullanıcı kendi kodundaki sürümü değiştirip yenisini üretemez.
/// </summary>
public class BiletKoduServisi : IBiletKoduServisi
{
    private readonly byte[] _anahtar;

    public BiletKoduServisi(IOptions<GirisAyarlari> ayarlar)
    {
        _anahtar = Encoding.UTF8.GetBytes(ayarlar.Value.ImzaAnahtari);
    }

    public string KodUret(int biletId, int kodSurumu) =>
        $"{biletId}.{kodSurumu}.{Imzala(biletId, kodSurumu)}";

    public BiletKodu? Coz(string? kod)
    {
        if (string.IsNullOrWhiteSpace(kod)) return null;

        var parcalar = kod.Split('.');

        // İki parçalı kodlar, sürüm eklenmeden önce gönderilmiş biletlere aittir.
        // Onları geçersiz saymak, o e-postalardaki QR'ları çalışmaz hâle getirirdi;
        // bu yüzden sürüm 1 kabul edilip eski imza biçimiyle doğrulanıyorlar.
        return parcalar.Length switch
        {
            2 => Dogrula(parcalar[0], surumMetni: null, parcalar[1]),
            3 => Dogrula(parcalar[0], parcalar[1], parcalar[2]),
            _ => null
        };
    }

    private BiletKodu? Dogrula(string idMetni, string? surumMetni, string gelenImza)
    {
        if (!int.TryParse(idMetni, out var biletId) || biletId <= 0) return null;

        var kodSurumu = 1;
        if (surumMetni != null && (!int.TryParse(surumMetni, out kodSurumu) || kodSurumu <= 0)) return null;

        var beklenenImza = surumMetni == null ? EskiImzala(biletId) : Imzala(biletId, kodSurumu);

        // Sabit süreli karşılaştırma: imzayı karakter karakter deneyerek
        // çözmeye çalışan zamanlama saldırılarını engeller.
        var gelen = Encoding.UTF8.GetBytes(gelenImza);
        var beklenen = Encoding.UTF8.GetBytes(beklenenImza);

        return CryptographicOperations.FixedTimeEquals(gelen, beklenen)
            ? new BiletKodu(biletId, kodSurumu)
            : null;
    }

    private string Imzala(int biletId, int kodSurumu) => Ozet($"bilet:{biletId}:{kodSurumu}");

    /// <summary>Sürüm eklenmeden önce kullanılan imza biçimi; eski QR'lar için korunuyor.</summary>
    private string EskiImzala(int biletId) => Ozet($"bilet:{biletId}");

    private string Ozet(string veri)
    {
        var hash = HMACSHA256.HashData(_anahtar, Encoding.UTF8.GetBytes(veri));

        // İlk 8 bayt (16 karakter) yeterli: QR'ı küçük tutar, kaba kuvvetle
        // tahmin edilmesi pratikte imkânsız.
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}

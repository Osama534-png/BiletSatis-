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
/// </summary>
public class BiletKoduServisi : IBiletKoduServisi
{
    private readonly byte[] _anahtar;

    public BiletKoduServisi(IOptions<GirisAyarlari> ayarlar)
    {
        _anahtar = Encoding.UTF8.GetBytes(ayarlar.Value.ImzaAnahtari);
    }

    public string KodUret(int biletId) => $"{biletId}.{Imzala(biletId)}";

    public int? BiletIdCoz(string? kod)
    {
        if (string.IsNullOrWhiteSpace(kod)) return null;

        var ayirac = kod.IndexOf('.');
        if (ayirac <= 0 || ayirac == kod.Length - 1) return null;

        if (!int.TryParse(kod[..ayirac], out var biletId) || biletId <= 0) return null;

        var gelenImza = kod[(ayirac + 1)..];
        var beklenenImza = Imzala(biletId);

        // Sabit süreli karşılaştırma: imzayı karakter karakter deneyerek
        // çözmeye çalışan zamanlama saldırılarını engeller.
        var gelen = Encoding.UTF8.GetBytes(gelenImza);
        var beklenen = Encoding.UTF8.GetBytes(beklenenImza);

        return CryptographicOperations.FixedTimeEquals(gelen, beklenen) ? biletId : null;
    }

    private string Imzala(int biletId)
    {
        var veri = Encoding.UTF8.GetBytes($"bilet:{biletId}");
        var hash = HMACSHA256.HashData(_anahtar, veri);

        // İlk 8 bayt (16 karakter) yeterli: QR'ı küçük tutar, kaba kuvvetle
        // tahmin edilmesi pratikte imkânsız.
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}

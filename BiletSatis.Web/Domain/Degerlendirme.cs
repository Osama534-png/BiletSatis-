namespace BiletSatis.Web.Domain;

/// <summary>
/// Bir kullanıcının etkinliğe verdiği puan ve yorum. Yalnızca kapıda QR'ı okutulup
/// girişi onaylanmış kullanıcılar değerlendirme bırakabilir — bu yüzden her yorum
/// "gerçekten oradaydı" bilgisiyle birlikte gösterilebilir.
/// </summary>
public class Degerlendirme
{
    public const int EnDusukPuan = 1;
    public const int EnYuksekPuan = 5;
    public const int EnUzunYorum = 1000;

    public int Id { get; set; }
    public int EtkinlikId { get; set; }
    public string KullaniciId { get; set; } = "";

    /// <summary>1–5 arası yıldız.</summary>
    public int Puan { get; set; }

    /// <summary>İsteğe bağlı serbest metin; boş bırakılırsa yalnızca puan sayılır.</summary>
    public string Yorum { get; set; } = "";

    public DateTime OlusturmaZamani { get; set; }

    /// <summary>Kullanıcı değerlendirmesini sonradan değiştirdiyse dolu olur.</summary>
    public DateTime? GuncellemeZamani { get; set; }

    public Etkinlik? Etkinlik { get; set; }

    public static bool PuanGecerli(int puan) => puan is >= EnDusukPuan and <= EnYuksekPuan;
}

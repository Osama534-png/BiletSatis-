namespace BiletSatis.Web.Domain;

/// <summary>
/// Kullanıcı ile etkinlik arasındaki çoka-çok ilişkinin taşıyıcısı: bir kullanıcı
/// birden çok etkinliği favoriye alabilir, bir etkinlik birden çok kullanıcının
/// favorisinde olabilir.
///
/// EF Core bu ilişkiyi gizli bir ara tabloyla da kurabilirdi, ama o zaman
/// "ne zaman eklendi" gibi bir alan tutulamazdı. Ara tablo açıkça modellenince
/// hem bu bilgi saklanabiliyor hem de sorgular doğrudan yazılabiliyor.
/// </summary>
public class Favori
{
    public string KullaniciId { get; set; } = "";
    public int EtkinlikId { get; set; }

    public DateTime EklenmeZamani { get; set; }

    public Etkinlik? Etkinlik { get; set; }
}

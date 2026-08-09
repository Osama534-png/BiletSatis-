namespace BiletSatis.Web.Domain;

public class Etkinlik
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";
    public EtkinlikKategorisi Kategori { get; set; } = EtkinlikKategorisi.Konser;

    /// <summary>Afiş görselinin yolu (örn. "/img/afis/duman.svg"). Boşsa gradyan + emoji gösterilir.</summary>
    public string AfisUrl { get; set; } = "";

    /// <summary>Detay sayfasında gösterilen tanıtım metni.</summary>
    public string Aciklama { get; set; } = "";

    /// <summary>Etkinliğe giriş için asgari yaş. 0 = yaş sınırı yok.</summary>
    public int YasSiniri { get; set; }

    public DateTime Tarih { get; set; }

    public List<Bilet> Biletler { get; set; } = new();

    /// <summary>
    /// Satır sürümü (optimistic concurrency). Etkinlik düzenleme ekranı oku-değiştir-kaydet
    /// akışıyla çalışır; iki yönetici aynı etkinliği aynı anda düzenlerse ikincisi
    /// birincinin değişikliğini sessizce ezerdi. SQL Server bu sütunu her güncellemede
    /// kendisi değiştirir; kayıt sırasında değer tutmazsa EF hata verir ve kullanıcı
    /// uyarılır. Biletlerde buna gerek yok: orada okuma-sonra-yazma zaten yok.
    /// </summary>
    public byte[]? SatirSurumu { get; set; }
}

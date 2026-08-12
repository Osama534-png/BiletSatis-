namespace BiletSatis.Web.Domain;

public class Etkinlik
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";

    /// <summary>
    /// <see cref="Mekan"/> alanının şehir kısmı ("Volkswagen Arena, İstanbul" → "İstanbul").
    /// Ayrı sütun olarak tutuluyor çünkü metnin içinden ayrıştırılan bir değerle ne
    /// filtreleme ne de indeksleme yapılabilir; şehir seçici de her istekte bütün
    /// etkinlikleri okumak zorunda kalırdı. Değer <c>SaveChanges</c> sırasında
    /// Mekan'dan türetilir, elle set edilmez.
    /// </summary>
    public string Sehir { get; set; } = "";
    public EtkinlikKategorisi Kategori { get; set; } = EtkinlikKategorisi.Konser;

    /// <summary>Afiş görselinin yolu (örn. "/img/afis/duman.svg"). Boşsa gradyan + emoji gösterilir.</summary>
    public string AfisUrl { get; set; } = "";

    /// <summary>Detay sayfasında gösterilen tanıtım metni.</summary>
    public string Aciklama { get; set; } = "";

    /// <summary>Etkinliğe giriş için asgari yaş. 0 = yaş sınırı yok.</summary>
    public int YasSiniri { get; set; }

    public DateTime Tarih { get; set; }

    /// <summary>
    /// Etkinliğin zamanı geçti mi. Geçmiş etkinliğe bilet satılamaz: kullanıcı
    /// olmamış bir konserin biletine para öder, karşılığında kapıda kullanamayacağı
    /// bir QR alırdı — üstelik projede otomatik iade akışı yok.
    ///
    /// Sayfası kapanmaz: değerlendirmeler okunmaya devam eder ve etkinliğe katılmış
    /// kullanıcılar yorum bırakabilir. Kapanan yalnızca satın alma.
    ///
    /// Karşılaştırma yerel saatle: <see cref="Tarih"/> bir "an" değil takvim saatidir
    /// (bkz. README → Zamanın iki türü). Bu özellik yalnızca bellekte kullanılır;
    /// sorgularda tarih doğrudan karşılaştırılır, çünkü EF bunu SQL'e çeviremez.
    /// </summary>
    public bool SonaErdi => Tarih <= DateTime.Now;

    /// <summary>
    /// Biletlerin nasıl satıldığı. Salonlu etkinliklerde kullanıcı koltuk seçer;
    /// genel girişte yalnızca adet seçer ve sistem müsait biletlerden o kadarını verir.
    /// </summary>
    public BiletModeli BiletModeli { get; set; } = BiletModeli.KoltukSecmeli;

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

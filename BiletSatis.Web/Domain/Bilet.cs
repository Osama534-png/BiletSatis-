namespace BiletSatis.Web.Domain;

public class Bilet
{
    public int Id { get; set; }
    public int EtkinlikId { get; set; }
    public string KoltukNo { get; set; } = "";
    public decimal Fiyat { get; set; }
    public BiletDurumu Durum { get; set; } = BiletDurumu.Satista;
    public DateTime? KilitBitisZamani { get; set; }
    public string? RezerveEdenKullaniciId { get; set; }

    /// <summary>
    /// Satın alma bildirimi gönderildi mi. Ödeme tamamlanınca false'a çekilir;
    /// bildirimi arka plan görevi gönderip işaretler. Ödeme akışı e-posta
    /// sunucusunu beklemez, gönderim hatası ödemeyi etkilemez.
    /// İptal edilip tekrar satılan bilette yeni alıcıya bildirim gitsin diye
    /// bayrak her ödeme tamamlanışında sıfırlanır.
    /// </summary>
    public bool BildirimGonderildi { get; set; }

    /// <summary>
    /// Bildirim görevinin bu kaydı "ben gönderiyorum" diye sahiplendiği an (UTC).
    /// Uygulamanın iki kopyası çalıştığında ikisi de aynı bileti görüp aynı e-postayı
    /// gönderebilirdi; sahiplenme tek atomik UPDATE ile yapıldığı için kaydı yalnızca
    /// biri alır. Sahiplenen süreç çökerse kira süresi dolar ve kayıt yeniden denenir.
    /// </summary>
    public DateTime? BildirimKilitZamani { get; set; }

    /// <summary>
    /// Bu bileti satın alan Stripe ödeme oturumunun kimliği. İki amacı var:
    /// satışın hangi ödemeye karşılık geldiğini izlemek ve aynı koltuklar için
    /// ikinci bir ödeme yapıldığını (kullanıcı iki sekmede ödemeye geçtiyse)
    /// fark edip kayda geçmek.
    /// </summary>
    public string? OdemeReferansi { get; set; }

    /// <summary>
    /// QR kodunun sürümü. Bilet başkasına devredildiğinde artar ve eski sahibin
    /// elindeki QR geçersizleşir. Sürüm imzanın içinde olduğu için kullanıcı kendi
    /// kodundaki sürümü değiştirip yenisini üretemez.
    /// </summary>
    public int KodSurumu { get; set; } = 1;

    /// <summary>Kapıda QR okutulup giriş onaylandı mı. Bir bilet yalnızca bir kez giriş sağlar.</summary>
    public bool GirisYapildi { get; set; }

    /// <summary>Girişin onaylandığı an (UTC).</summary>
    public DateTime? GirisZamani { get; set; }

    public Etkinlik? Etkinlik { get; set; }
}

using BiletSatis.Web.Domain;

namespace BiletSatis.Web.Models;

public class AdminPanelVm
{
    public List<AdminEtkinlikOzeti> Etkinlikler { get; set; } = new();

    public int ToplamEtkinlik => Etkinlikler.Count;
    public int ToplamSatilan => Etkinlikler.Sum(e => e.SatildiSayisi);
    public int ToplamSatista => Etkinlikler.Sum(e => e.SatistaSayisi);
    public decimal ToplamGelir => Etkinlikler.Sum(e => e.Gelir);
    public int ToplamKuyrukta => Etkinlikler.Sum(e => e.KuyrukBeklemede);
    public int ToplamGirisYapan => Etkinlikler.Sum(e => e.GirisYapan);

    public int ToplamKoltuk => Etkinlikler.Sum(e => e.ToplamKoltuk);

    /// <summary>Tüm etkinliklerdeki koltukların yüzde kaçı satıldı.</summary>
    public int DolulukYuzdesi => ToplamKoltuk == 0
        ? 0
        : (int)Math.Round(ToplamSatilan * 100.0 / ToplamKoltuk);
}

public class AdminEtkinlikOzeti
{
    public int EtkinlikId { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";
    public EtkinlikKategorisi Kategori { get; set; }
    public DateTime Tarih { get; set; }

    public int SatistaSayisi { get; set; }
    public int SepetteSayisi { get; set; }
    public int SatildiSayisi { get; set; }
    public int KuyrukBeklemede { get; set; }
    public int KuyrukHakTanindi { get; set; }

    /// <summary>Satılan biletlerin toplam tutarı.</summary>
    public decimal Gelir { get; set; }

    /// <summary>Kapıda QR okutularak girişi onaylanan bilet sayısı.</summary>
    public int GirisYapan { get; set; }

    /// <summary>Satılan biletlerin yüzde kaçı kapıdan geçti.</summary>
    public int GirisYuzdesi => SatildiSayisi == 0
        ? 0
        : (int)Math.Round(GirisYapan * 100.0 / SatildiSayisi);

    public int ToplamKoltuk => SatistaSayisi + SepetteSayisi + SatildiSayisi;

    public int DolulukYuzdesi => ToplamKoltuk == 0
        ? 0
        : (int)Math.Round(SatildiSayisi * 100.0 / ToplamKoltuk);

    /// <summary>Etkinliğin zamanı geçti mi.</summary>
    public bool SonaErdi => Tarih <= DateTime.Now;

    /// <summary>
    /// Etkinlik silinebilir mi.
    ///
    /// Satılmış bileti olan <b>gelecek</b> etkinlikler silinemez: insanların elinde
    /// kullanacakları geçerli bilet var, etkinliği silmek onları yok ederdi.
    ///
    /// <b>Sona ermiş</b> etkinlikler silinebilir — biletler artık kullanılamaz,
    /// arşiv temizliği yöneticinin kararıdır. Silme, satış kayıtlarını ve
    /// değerlendirmeleri de götürür; bu yüzden arayüzde ne silineceği açıkça yazılır.
    ///
    /// (Asıl kural sunucuda, DELETE'in koşulunda; bu yalnızca düğmenin gösterimi için.)
    /// </summary>
    public bool Silinebilir => SatildiSayisi == 0 || SonaErdi;

    /// <summary>Silme onayında gösterilecek uyarı; sona ermiş ve satışı olan etkinlikte ağırlaşır.</summary>
    public string SilmeUyarisi => SatildiSayisi > 0
        ? $"{Ad} etkinliği sona erdi. Silerseniz {SatildiSayisi} satış kaydı ve bu etkinliğe " +
          "bırakılmış değerlendirmeler de kalıcı olarak silinecek. Emin misiniz?"
        : $"{Ad} etkinliği ve tüm biletleri silinecek. Emin misiniz?";
}

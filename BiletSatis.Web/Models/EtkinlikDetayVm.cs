using BiletSatis.Web.Domain;
using BiletSatis.Web.Services.Degerlendirmeler;

namespace BiletSatis.Web.Models;

public class EtkinlikDetayVm
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";
    public string AfisUrl { get; set; } = "";
    public string Aciklama { get; set; } = "";
    public int YasSiniri { get; set; }
    public EtkinlikKategorisi Kategori { get; set; }
    public DateTime Tarih { get; set; }

    public int ToplamKoltuk { get; set; }
    public int MusaitKoltuk { get; set; }
    public decimal? EnDusukFiyat { get; set; }
    public decimal? EnYuksekFiyat { get; set; }

    /// <summary>Blok bazlı fiyat listesi (kod, kategori adı, fiyat, müsait koltuk).</summary>
    public List<FiyatSatiriVm> Fiyatlar { get; set; } = new();

    /// <summary>Aynı kategorideki diğer etkinlikler.</summary>
    public List<EtkinlikKartVm> BenzerEtkinlikler { get; set; } = new();

    /// <summary>Puan ortalaması, dağılımı ve yorumlar.</summary>
    public DegerlendirmeOzeti Degerlendirmeler { get; set; } = new();

    /// <summary>Etkinlik giriş yapan kullanıcının favorilerinde mi.</summary>
    public bool Favoride { get; set; }

    /// <summary>Giriş yapan kullanıcının bu etkinliğe okutulmuş bileti var mı.</summary>
    public bool DegerlendirebilirMi { get; set; }

    /// <summary>Kullanıcı daha önce değerlendirdiyse formu doldurmak için kullanılır.</summary>
    public DegerlendirmeSatiri? KendiDegerlendirmesi { get; set; }

    public bool Tukendi => MusaitKoltuk == 0;
    // Bkz. EtkinlikKartVm.KalanGun: etkinlik tarihi takvim saatidir, an değil.
    public int KalanGun => (Tarih.Date - DateTime.Now.Date).Days;

    public string Sehir => MekanBilgisi.Sehir(Mekan);

    public string MekanAdi => MekanBilgisi.SalonAdi(Mekan);
}

public class FiyatSatiriVm
{
    public string Blok { get; set; } = "";
    public string Kategori { get; set; } = "";
    public decimal Fiyat { get; set; }
    public int MusaitKoltuk { get; set; }
}

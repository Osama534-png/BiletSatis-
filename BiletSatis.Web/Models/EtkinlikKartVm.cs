namespace BiletSatis.Web.Models;

public class EtkinlikKartVm
{
    public int Id { get; set; }
    public string Ad { get; set; } = "";
    public string Mekan { get; set; } = "";

    public string Sehir => BiletSatis.Web.Domain.MekanBilgisi.Sehir(Mekan);

    /// <summary>Etkinliğe kalan gün; geçmiş etkinliklerde negatif.</summary>
    public int KalanGun => (Tarih.Date - DateTime.Now.Date).Days;

    /// <summary>Bir haftadan az kaldıysa geri sayım vurgulanır.</summary>
    public bool YaklasanEtkinlik => KalanGun >= 0 && KalanGun <= 7;

    /// <summary>Son koltuklar kaldıysa kıtlık uyarısı gösterilir.</summary>
    public bool SonBiletler => MusaitKoltukSayisi > 0 && MusaitKoltukSayisi <= 10;

    public string GeriSayimMetni => KalanGun switch
    {
        < 0 => "Sona erdi",
        0 => "Bugün!",
        1 => "Yarın",
        _ => $"{KalanGun} gün kaldı"
    };
    public string AfisUrl { get; set; } = "";
    public BiletSatis.Web.Domain.EtkinlikKategorisi Kategori { get; set; }
    public DateTime Tarih { get; set; }
    public decimal? EnDusukFiyat { get; set; }
    public int MusaitKoltukSayisi { get; set; }
}

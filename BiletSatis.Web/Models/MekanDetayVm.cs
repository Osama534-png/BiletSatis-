using BiletSatis.Web.Services.Mekanlar;

namespace BiletSatis.Web.Models;

public class MekanDetayVm
{
    public MekanOzeti Ozet { get; set; } = null!;

    /// <summary>Seçili sekmedeki (yaklaşan / geçmiş) etkinliklerin görüntülenen sayfası.</summary>
    public SayfaliListe<EtkinlikKartVm> Etkinlikler { get; set; } = new();

    /// <summary>Geçmiş etkinlikler sekmesi mi açık.</summary>
    public bool Gecmis { get; set; }
}

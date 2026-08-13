using BiletSatis.Web.Domain;
using BiletSatis.Web.Models;

namespace BiletSatis.Web.Services.Mekanlar;

/// <summary>
/// Mekan sayfasının başlık bilgileri. Mekan ayrı bir tablo değil; kimliği
/// <see cref="Etkinlik.Mekan"/> metninin kendisidir (bkz. <see cref="MekanSorguServisi"/>).
/// </summary>
public sealed record MekanOzeti(
    string Mekan,
    int YaklasanEtkinlik,
    int GecmisEtkinlik,
    decimal? EnDusukFiyat,
    double? PuanOrtalamasi,
    int DegerlendirmeAdedi)
{
    public int ToplamEtkinlik => YaklasanEtkinlik + GecmisEtkinlik;

    public string SalonAdi => MekanBilgisi.SalonAdi(Mekan);

    public string Sehir => MekanBilgisi.Sehir(Mekan);
}

public interface IMekanSorguServisi
{
    /// <summary>
    /// Mekanın özeti. Mekana ait hiç etkinlik yoksa <c>null</c> döner — çağıran
    /// bunu 404'e çevirir, çünkü var olmayan bir mekan için boş sayfa göstermek
    /// adres çubuğuna ne yazılırsa yazılsın "geçerli sayfa" izlenimi verirdi.
    /// </summary>
    Task<MekanOzeti?> OzetAsync(string mekan, CancellationToken ct = default);

    /// <summary>
    /// Mekandaki etkinlikler. <paramref name="gecmis"/> yanlışsa yaklaşan etkinlikler
    /// tarihe göre artan, doğruysa geçmiş etkinlikler azalan sırada döner.
    /// </summary>
    Task<SayfaliListe<EtkinlikKartVm>> EtkinliklerAsync(
        string mekan, bool gecmis, int sayfa, int sayfaBoyutu, CancellationToken ct = default);

    /// <summary>
    /// Etkinlik detayındaki mekan kartında gösterilen "bu mekandaki diğer etkinlikler"
    /// listesi. Görüntülenen etkinliğin kendisi listeye girmez.
    /// </summary>
    Task<List<EtkinlikKartVm>> DigerEtkinliklerAsync(
        string mekan, int haricEtkinlikId, int adet, CancellationToken ct = default);
}

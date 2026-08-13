using BiletSatis.Web.Services.Populerlik;

namespace BiletSatis.Web.Models;

public class PopulerListeVm
{
    public PopulerlikDonemi Donem { get; set; } = PopulerlikDonemi.TumZamanlar;

    public List<PopulerEtkinlikVm> Etkinlikler { get; set; } = new();

    /// <summary>
    /// Dönem daraltıldığında liste boş kalabilir: o aralıkta hiç satış olmamış
    /// olabilir ya da satışların tamamı satış zamanı sütunu eklenmeden önce yapılmış
    /// olabilir. Kullanıcıya "veri yok" demek yerine nedenini söylüyoruz.
    /// </summary>
    public bool DonemBos => Etkinlikler.Count == 0 && Donem != PopulerlikDonemi.TumZamanlar;
}

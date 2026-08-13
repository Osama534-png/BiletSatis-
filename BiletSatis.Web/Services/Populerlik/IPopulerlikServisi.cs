using BiletSatis.Web.Models;

namespace BiletSatis.Web.Services.Populerlik;

/// <summary>
/// Sıralamanın hangi zaman aralığına baktığı.
///
/// <para><b>Neden dönem seçimi var?</b> "En çok satan" tek başına zaman bilgisi
/// taşımaz: bir yıldır satışta olan etkinlik, iki gündür satışta olan ve çok daha
/// hızlı satan etkinliği toplam sayıyla her zaman geçer. Dönem daraltıldığında
/// liste "en çok satan"dan "şu anda trend olan"a dönüşür.</para>
/// </summary>
public enum PopulerlikDonemi
{
    /// <summary>Son 7 günde satılanlar.</summary>
    Hafta,

    /// <summary>Son 30 günde satılanlar.</summary>
    Ay,

    /// <summary>Bütün satışlar — satış zamanı bilinmeyen eski kayıtlar dahil.</summary>
    TumZamanlar
}

public static class PopulerlikDonemleri
{
    /// <summary>Adres çubuğundaki değer ("hafta" | "ay" | "tumu").</summary>
    public static string Anahtar(this PopulerlikDonemi donem) => donem switch
    {
        PopulerlikDonemi.Hafta => "hafta",
        PopulerlikDonemi.Ay => "ay",
        _ => "tumu"
    };

    public static string Ad(this PopulerlikDonemi donem) => donem switch
    {
        PopulerlikDonemi.Hafta => "Son 7 gün",
        PopulerlikDonemi.Ay => "Son 30 gün",
        _ => "Tüm zamanlar"
    };

    /// <summary>Geriye kaç gün bakılacağı; <c>null</c> ise sınır yok.</summary>
    public static int? GunSayisi(this PopulerlikDonemi donem) => donem switch
    {
        PopulerlikDonemi.Hafta => 7,
        PopulerlikDonemi.Ay => 30,
        _ => null
    };

    /// <summary>Tanınmayan değer varsayılana ("tüm zamanlar") düşer.</summary>
    public static PopulerlikDonemi Coz(string? anahtar) => anahtar switch
    {
        "hafta" => PopulerlikDonemi.Hafta,
        "ay" => PopulerlikDonemi.Ay,
        _ => PopulerlikDonemi.TumZamanlar
    };

    public static readonly PopulerlikDonemi[] Tumu =
    {
        PopulerlikDonemi.Hafta, PopulerlikDonemi.Ay, PopulerlikDonemi.TumZamanlar
    };
}

public interface IPopulerlikServisi
{
    /// <summary>
    /// Satılan bilet sayısına göre sıralanmış yaklaşan etkinlikler.
    /// Sona ermiş etkinlikler listeye girmez: kullanıcının artık bilet alamayacağı
    /// bir etkinliği "en çok satan" diye önermenin karşılığı yok.
    /// </summary>
    Task<List<PopulerEtkinlikVm>> EnCokSatanlarAsync(
        PopulerlikDonemi donem, int adet, CancellationToken ct = default);
}

namespace BiletSatis.Web.Services.Giris;

/// <summary>
/// QR koddan çözülen bilgi: hangi bilet ve kodun kaçıncı sürümü.
/// Sürüm, biletin o anki sürümüyle karşılaştırılır; devredilmiş biletin
/// eski kodu böylece reddedilir.
/// </summary>
public sealed record BiletKodu(int BiletId, int KodSurumu);

public interface IBiletKoduServisi
{
    /// <summary>QR koda yazılacak imzalı kodu üretir ("1399.2.a7f3c9e2" biçiminde).</summary>
    string KodUret(int biletId, int kodSurumu);

    /// <summary>
    /// Kodun imzasını doğrular. Geçerliyse bilet numarası ve kod sürümünü döner,
    /// değilse null. İmza tutmayan kod hiç veritabanına sorulmaz.
    /// </summary>
    BiletKodu? Coz(string? kod);
}

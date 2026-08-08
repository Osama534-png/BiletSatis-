namespace BiletSatis.Web.Services.Giris;

public interface IBiletKoduServisi
{
    /// <summary>QR koda yazılacak imzalı kodu üretir ("1399.a7f3c9e2" biçiminde).</summary>
    string KodUret(int biletId);

    /// <summary>
    /// Kodun imzasını doğrular. Geçerliyse bilet numarasını döner, değilse null.
    /// İmza tutmayan kod hiç veritabanına sorulmaz.
    /// </summary>
    int? BiletIdCoz(string? kod);
}

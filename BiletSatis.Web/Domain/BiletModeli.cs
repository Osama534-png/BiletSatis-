namespace BiletSatis.Web.Domain;

/// <summary>
/// Etkinliğin bilet satış biçimi. Salonlu etkinliklerde kullanıcı koltuk seçer;
/// festival, ayakta konser gibi etkinliklerde koltuk yoktur, yalnızca adet vardır.
/// </summary>
public enum BiletModeli
{
    /// <summary>Kullanıcı salon haritasından belirli koltukları seçer.</summary>
    KoltukSecmeli,

    /// <summary>Koltuk numarası yoktur; kullanıcı yalnızca kaç bilet istediğini söyler.</summary>
    GenelGiris
}

public static class BiletModeliMetni
{
    public const string KoltukSecmeli = "KoltukSecmeli";
    public const string GenelGiris = "GenelGiris";

    public static string Ad(BiletModeli model) =>
        model == BiletModeli.GenelGiris ? "Genel giriş" : "Koltuk seçmeli";
}

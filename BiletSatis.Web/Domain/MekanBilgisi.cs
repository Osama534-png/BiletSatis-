namespace BiletSatis.Web.Domain;

/// <summary>
/// Mekan alanı "Salon Adı, Şehir" biçiminde tutulur (örn. "Volkswagen Arena, İstanbul").
/// Şehir ayrı bir sütun olmadığı için ayrıştırma tek yerden yapılır.
/// </summary>
public static class MekanBilgisi
{
    /// <summary>Virgülden sonraki şehir kısmı; virgül yoksa boş dizi.</summary>
    public static string Sehir(string? mekan)
    {
        if (string.IsNullOrWhiteSpace(mekan)) return "";

        var ayirac = mekan.LastIndexOf(',');
        return ayirac >= 0 ? mekan[(ayirac + 1)..].Trim() : "";
    }

    /// <summary>Virgülden önceki salon adı; virgül yoksa metnin tamamı.</summary>
    public static string SalonAdi(string? mekan)
    {
        if (string.IsNullOrWhiteSpace(mekan)) return "";

        var ayirac = mekan.LastIndexOf(',');
        return ayirac >= 0 ? mekan[..ayirac].Trim() : mekan.Trim();
    }
}

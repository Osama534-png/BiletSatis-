using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace BiletSatis.Web.Services;

/// <summary>
/// Identity jetonlarını adres satırında taşınabilir hâle getirir.
///
/// Jetonlar Base64 üretildiği için içlerinde <c>+</c> ve <c>/</c> bulunabilir;
/// bunlar sorgu dizesinde ayrı anlam taşır ve jeton sessizce bozulur. Base64Url
/// biçimi bu iki karakteri kullanmaz.
///
/// Doğrulama, şifre sıfırlama ve e-posta değişikliği aynı kodlamayı kullanmak
/// zorunda: biri kodlayıp diğeri çözmeye kalkarsa jeton geçersiz görünür.
/// </summary>
public static class JetonKodlayici
{
    public static string Kodla(string jeton) =>
        WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(jeton));

    /// <summary>Bozuk bağlantı geçersiz jeton sayılır; çözme hatası dışarı taşmaz.</summary>
    public static string Coz(string? kodlanmis)
    {
        if (string.IsNullOrEmpty(kodlanmis)) return "";

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(kodlanmis));
        }
        catch (FormatException)
        {
            return "";
        }
    }
}

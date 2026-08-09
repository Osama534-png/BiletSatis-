using System.Security.Cryptography;

namespace BiletSatis.Web.Services;

/// <summary>
/// İçerik Güvenlik Politikası (CSP) ve diğer tarayıcı savunma başlıkları.
///
/// CSP, sayfayla birlikte gönderilen ve tarayıcının uyguladığı bir kuraldır:
/// "bu sayfada yalnızca şu kaynaklardan gelen script'leri çalıştır". Amacı XSS'i
/// önlemek değil (o Razor'ın metni kaçırmasıyla önleniyor), bir gün kaçırma
/// atlanırsa açığın işe yaramaz olmasını sağlamaktır.
///
/// Script'ler için nonce kullanılır: her istekte rastgele bir değer üretilip hem
/// kendi script etiketlerimize hem de başlığa yazılır. Saldırganın enjekte ettiği
/// script bu değeri bilemez, çünkü her sayfa yüklemesinde değişir.
///
/// Stiller için de 'unsafe-inline' kaldırıldı. style="..." öznitelikleri nonce ile
/// beyaz listeye alınamaz (nonce yalnızca &lt;style&gt; etiketlerinde çalışır), bu yüzden
/// sabit olanlar CSS sınıflarına taşındı; değere göre değişenler data-* özniteliğinden
/// okunup JS ile atanıyor. CSSOM üzerinden stil yazmak CSP tarafından engellenmez,
/// çünkü sayfaya metin enjekte edilmiyor.
/// </summary>
public static class GuvenlikBasliklari
{
    public const string NonceAnahtari = "csp-nonce";

    /// <summary>Razor görünümlerinden bu isteğe ait nonce değerini okur.</summary>
    public static string CspNonce(this HttpContext context) =>
        context.Items[NonceAnahtari] as string ?? "";

    public static IApplicationBuilder UseGuvenlikBasliklari(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var nonce = NonceUret();
            context.Items[NonceAnahtari] = nonce;

            var headers = context.Response.Headers;

            headers["Content-Security-Policy"] = string.Join("; ",
                "default-src 'self'",
                "base-uri 'self'",
                "object-src 'none'",
                "frame-ancestors 'none'",
                $"script-src 'self' 'nonce-{nonce}'",

                // Google Fonts stil dosyası dışarıdan gelir. Satır içi stiller tamamen
                // kaldırıldığı için 'unsafe-inline' artık yok.
                "style-src 'self' https://fonts.googleapis.com",
                "font-src 'self' https://fonts.gstatic.com",

                // Afiş adresleri dışarıyı gösterebilir; görsel kod çalıştıramaz.
                "img-src 'self' data: https:",

                "connect-src 'self'",
                "form-action 'self'");

            // Sunucunun bildirdiği içerik türünü tarayıcı "tahmin ederek" değiştirmesin.
            headers["X-Content-Type-Options"] = "nosniff";

            // Site başka bir sayfaya iframe ile gömülüp tıklama hırsızlığında kullanılmasın.
            // (frame-ancestors bunu zaten karşılar; eski tarayıcılar için burada da duruyor.)
            headers["X-Frame-Options"] = "DENY";

            // Dış sitelere giderken tam adres (ve içindeki parametreler) sızmasın.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            await next();
        });

    private static string NonceUret()
    {
        Span<byte> bayt = stackalloc byte[16];
        RandomNumberGenerator.Fill(bayt);
        return Convert.ToBase64String(bayt);
    }
}

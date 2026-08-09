namespace BiletSatis.Web.Services.Eposta;

/// <summary>
/// Hesap işlemleriyle ilgili e-postalar: adres doğrulama ve şifre sıfırlama.
/// Bilet/kuyruk bildirimlerinden ayrı tutulur; onlar arka planda kuyruklanır,
/// bunlar kullanıcının o anki isteğine cevaptır.
/// </summary>
public interface IKimlikEpostaServisi
{
    Task DogrulamaGonderAsync(string alici, string ad, string dogrulamaAdresi, CancellationToken ct = default);

    Task SifirlamaGonderAsync(string alici, string ad, string sifirlamaAdresi, CancellationToken ct = default);
}

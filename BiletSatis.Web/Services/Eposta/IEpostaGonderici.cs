namespace BiletSatis.Web.Services.Eposta;

public interface IEpostaGonderici
{
    /// <summary>
    /// Tek bir e-posta gönderir. Gönderim başarısızsa istisna fırlatır;
    /// çağıran taraf yeniden deneme kararını kendisi verir.
    /// </summary>
    Task GonderAsync(string aliciAdresi, string konu, string htmlGovde, CancellationToken ct = default);
}

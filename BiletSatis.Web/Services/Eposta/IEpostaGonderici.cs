namespace BiletSatis.Web.Services.Eposta;

public interface IEpostaGonderici
{
    /// <summary>
    /// Tek bir e-posta gönderir. Gönderim başarısızsa istisna fırlatır;
    /// çağıran taraf yeniden deneme kararını kendisi verir.
    /// </summary>
    /// <param name="gorseller">
    /// Gövdeye gömülecek görseller. HTML içinde "cid:{ContentId}" ile referans verilir.
    /// </param>
    Task GonderAsync(
        string aliciAdresi,
        string konu,
        string htmlGovde,
        IReadOnlyList<GomuluGorsel>? gorseller = null,
        CancellationToken ct = default);
}

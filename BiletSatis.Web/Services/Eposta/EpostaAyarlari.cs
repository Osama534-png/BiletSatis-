namespace BiletSatis.Web.Services.Eposta;

public class EpostaAyarlari
{
    public const string BolumAdi = "Eposta";

    public string GondericiAdi { get; set; } = "BiletSatış";
    public string GondericiAdresi { get; set; } = "bildirim@biletsatis.local";

    /// <summary>
    /// E-postadaki bağlantılar için sitenin tam adresi. E-posta istemcisinde
    /// göreli adres çalışmadığından zorunludur.
    /// </summary>
    public string SiteAdresi { get; set; } = "https://localhost:5052";

    public string SmtpSunucu { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string KullaniciAdi { get; set; } = "";
    public string Sifre { get; set; } = "";
    public bool SslKullan { get; set; } = true;

    /// <summary>
    /// SMTP sunucusu tanımlı değilse gerçek gönderim yapılamaz; bu durumda
    /// e-postalar diske yazılır (geliştirme modu).
    /// </summary>
    public bool SmtpYapilandirilmisMi => !string.IsNullOrWhiteSpace(SmtpSunucu);

    /// <summary>Geliştirme modunda e-postaların yazılacağı klasör (içerik köküne göre).</summary>
    public string GelistirmeKlasoru { get; set; } = "logs/eposta";
}

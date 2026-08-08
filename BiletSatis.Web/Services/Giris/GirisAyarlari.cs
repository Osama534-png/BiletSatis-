namespace BiletSatis.Web.Services.Giris;

public class GirisAyarlari
{
    public const string BolumAdi = "Giris";

    /// <summary>
    /// Bilet QR kodlarını imzalamakta kullanılan gizli anahtar.
    /// Gerçek dağıtımda user-secrets ya da ortam değişkeniyle verilmelidir;
    /// anahtar sızarsa sahte bilet üretilebilir.
    /// </summary>
    public string ImzaAnahtari { get; set; } = "";
}

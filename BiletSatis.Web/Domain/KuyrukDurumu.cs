namespace BiletSatis.Web.Domain;

public enum KuyrukDurumu { Beklemede, HakTanindi, Tamamlandi, SuresiDoldu }

public static class KuyrukDurumMetni
{
    public const string Beklemede = "Beklemede";
    public const string HakTanindi = "HakTanindi";
    public const string Tamamlandi = "Tamamlandi";
    public const string SuresiDoldu = "SuresiDoldu";
}

namespace BiletSatis.Web.Domain;

public enum EtkinlikKategorisi
{
    Konser,
    Tiyatro,
    Sinema,
    Festival,
    StandUp,
    ElektronikMuzik,
    CocukAktiviteleri,
    Eglence
}

public static class KategoriMetni
{
    /// <summary>Menüde ve kartlarda gösterilen Türkçe ad.</summary>
    public static string Ad(EtkinlikKategorisi kategori) => kategori switch
    {
        EtkinlikKategorisi.Konser => "Konser",
        EtkinlikKategorisi.Tiyatro => "Tiyatro",
        EtkinlikKategorisi.Sinema => "Sinema",
        EtkinlikKategorisi.Festival => "Festival",
        EtkinlikKategorisi.StandUp => "Stand Up",
        EtkinlikKategorisi.ElektronikMuzik => "Elektronik Müzik",
        EtkinlikKategorisi.CocukAktiviteleri => "Çocuk Aktiviteleri",
        EtkinlikKategorisi.Eglence => "Eğlence",
        _ => kategori.ToString()
    };

    public static IReadOnlyList<EtkinlikKategorisi> Tumu { get; } =
        Enum.GetValues<EtkinlikKategorisi>();
}

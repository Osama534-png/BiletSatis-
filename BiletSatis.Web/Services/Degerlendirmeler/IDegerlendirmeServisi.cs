namespace BiletSatis.Web.Services.Degerlendirmeler;

public enum DegerlendirmeSonucu
{
    Kaydedildi,
    Guncellendi,

    /// <summary>Kullanıcının bu etkinliğe okutulmuş bir bileti yok — değerlendirme hakkı doğmamış.</summary>
    KatilimYok,

    GecersizPuan
}

/// <summary>Etkinlik sayfasında gösterilen puan özeti.</summary>
public sealed class DegerlendirmeOzeti
{
    public int Adet { get; init; }
    public decimal? Ortalama { get; init; }

    /// <summary>1'den 5'e kadar her puandan kaç tane verildiği.</summary>
    public IReadOnlyDictionary<int, int> Dagilim { get; init; } = new Dictionary<int, int>();

    public IReadOnlyList<DegerlendirmeSatiri> Satirlar { get; init; } = Array.Empty<DegerlendirmeSatiri>();
}

public sealed class DegerlendirmeSatiri
{
    public string KullaniciId { get; init; } = "";
    public string KullaniciAdi { get; init; } = "";
    public int Puan { get; init; }
    public string Yorum { get; init; } = "";
    public DateTime Zaman { get; init; }
    public bool Duzenlendi { get; init; }
}

public interface IDegerlendirmeServisi
{
    /// <summary>
    /// Kullanıcının bu etkinliği değerlendirme hakkı var mı: satın aldığı biletlerden
    /// en az birinin kapıda okutulmuş olması gerekir.
    /// </summary>
    Task<bool> DegerlendirebilirMiAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default);

    Task<DegerlendirmeSonucu> KaydetAsync(int etkinlikId, string kullaniciId, int puan, string? yorum, CancellationToken ct = default);

    Task<DegerlendirmeOzeti> OzetAsync(int etkinlikId, CancellationToken ct = default);

    /// <summary>Kullanıcının bu etkinliğe daha önce bıraktığı değerlendirme (formu doldurmak için).</summary>
    Task<DegerlendirmeSatiri?> KendiDegerlendirmesiAsync(int etkinlikId, string kullaniciId, CancellationToken ct = default);
}

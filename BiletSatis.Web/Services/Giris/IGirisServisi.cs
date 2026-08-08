namespace BiletSatis.Web.Services.Giris;

public interface IGirisServisi
{
    /// <summary>Bileti değiştirmeden durumunu okur; görevli önce ne olduğunu görür.</summary>
    Task<GirisSonucu> DurumSorgulaAsync(string? kod, CancellationToken ct = default);

    /// <summary>
    /// Girişi onaylar. İki görevli aynı bileti aynı anda okutsa bile
    /// yalnızca biri GirisOnaylandi alır, diğeri ZatenKullanildi görür.
    /// </summary>
    Task<GirisSonucu> GirisiOnaylaAsync(string? kod, CancellationToken ct = default);
}

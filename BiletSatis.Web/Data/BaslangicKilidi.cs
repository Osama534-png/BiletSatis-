using Microsoft.Data.SqlClient;

namespace BiletSatis.Web.Data;

/// <summary>
/// Uygulama başlarken migration ve seed işlemlerini tek kopyanın yapmasını sağlayan
/// dağıtık kilit. Uygulamanın iki kopyası aynı anda ayağa kalktığında ikisi de
/// "etkinlik var mı" diye bakıp ikisi de örnek veriyi yazabilir, ya da migration'lar
/// birbirine girebilirdi. C# tarafındaki <c>lock</c> burada işe yaramaz; kilidin
/// süreçlerin dışında, veritabanında olması gerekir.
///
/// SQL Server'ın <c>sp_getapplock</c> yordamı tam olarak bunun içindir: kilit
/// bağlantı oturumuna bağlıdır, bu yüzden bağlantı iş bitene kadar açık tutulur.
/// Kilidi tutan süreç çökerse bağlantı düşer ve kilit kendiliğinden serbest kalır.
/// </summary>
public sealed class BaslangicKilidi : IAsyncDisposable
{
    private const string KaynakAdi = "BiletSatis_Baslangic";
    private const int ZamanAsimiMs = 60_000;

    private readonly SqlConnection _baglanti;
    private readonly ILogger _logger;

    private BaslangicKilidi(SqlConnection baglanti, ILogger logger)
    {
        _baglanti = baglanti;
        _logger = logger;
    }

    public static async Task<BaslangicKilidi> AlAsync(
        string baglantiDizesi, ILogger logger, CancellationToken ct = default)
    {
        var baglanti = new SqlConnection(baglantiDizesi);
        await baglanti.OpenAsync(ct);

        var komut = baglanti.CreateCommand();
        komut.CommandType = System.Data.CommandType.StoredProcedure;
        komut.CommandText = "sp_getapplock";
        komut.CommandTimeout = (ZamanAsimiMs / 1000) + 15;

        komut.Parameters.AddWithValue("@Resource", KaynakAdi);
        komut.Parameters.AddWithValue("@LockMode", "Exclusive");
        komut.Parameters.AddWithValue("@LockOwner", "Session");
        komut.Parameters.AddWithValue("@LockTimeout", ZamanAsimiMs);

        var sonuc = komut.Parameters.Add("@Sonuc", System.Data.SqlDbType.Int);
        sonuc.Direction = System.Data.ParameterDirection.ReturnValue;

        await komut.ExecuteNonQueryAsync(ct);

        // 0: hemen alındı, 1: bekledikten sonra alındı. Negatif değerler hatadır.
        var kod = (int)sonuc.Value;
        if (kod < 0)
        {
            await baglanti.DisposeAsync();
            throw new InvalidOperationException(
                $"Başlangıç kilidi alınamadı (sp_getapplock sonucu: {kod}). " +
                "Başka bir kopya migration/seed işlemini bitirmemiş olabilir.");
        }

        if (kod == 1)
        {
            logger.LogInformation("Başlangıç kilidi beklendikten sonra alındı; başka bir kopya hazırlık yapıyordu.");
        }

        return new BaslangicKilidi(baglanti, logger);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var komut = _baglanti.CreateCommand();
            komut.CommandType = System.Data.CommandType.StoredProcedure;
            komut.CommandText = "sp_releaseapplock";
            komut.Parameters.AddWithValue("@Resource", KaynakAdi);
            komut.Parameters.AddWithValue("@LockOwner", "Session");

            await komut.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // Bağlantı kapanınca kilit zaten serbest kalır; bu yüzden hata ölümcül değil.
            _logger.LogWarning(ex, "Başlangıç kilidi serbest bırakılırken hata oluştu.");
        }
        finally
        {
            await _baglanti.DisposeAsync();
        }
    }
}

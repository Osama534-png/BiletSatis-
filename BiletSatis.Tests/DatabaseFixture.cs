using BiletSatis.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Tests;

// Testler gerçek SQL Server semantiğine (DATEADD, GETUTCDATE(), atomik UPDATE...WHERE)
// dayandığı için InMemory/SQLite yerine ayrı bir test veritabanına karşı çalışır.
public class DatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// Yerel geliştirmede Windows kimlik doğrulamasıyla localhost'a bağlanır.
    ///
    /// CI'da bu işe yaramaz: sunucu Linux container'ında çalışır ve kullanıcı/şifre
    /// ile bağlanılır. Bu yüzden dize <c>TEST_CONNECTION_STRING</c> ortam
    /// değişkeniyle geçilebilir — tanımlıysa o kullanılır, değilse yerel varsayılan.
    /// </summary>
    public static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING")
        ?? "Server=localhost;Database=BiletSatisDb_Test;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<BiletSatisDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new BiletSatisDbContext(options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public static BiletSatisDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BiletSatisDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new BiletSatisDbContext(options);
    }

    /// <summary>
    /// Uygulamanın gerçek yapılandırmasındaki gibi geçici hata yeniden denemesi
    /// açık bir bağlam üretir.
    ///
    /// Fark önemli: yeniden deneme açıkken EF, kendi açtığın bir işlemi (transaction)
    /// yürütme stratejisinin dışında görürse çalışma anında hata verir. Servis
    /// testlerinin varsayılan bağlamı bu ayarı taşımadığı için o hatayı hiç
    /// göremiyordu — yani rezervasyon akışının üretimdeki gibi çalıştığını
    /// kanıtlamıyordu.
    /// </summary>
    public static BiletSatisDbContext CreateContextWithRetry()
    {
        var options = new DbContextOptionsBuilder<BiletSatisDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null))
            .Options;
        return new BiletSatisDbContext(options);
    }
}

[CollectionDefinition("Veritabanı")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

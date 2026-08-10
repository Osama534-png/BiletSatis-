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
}

[CollectionDefinition("Veritabanı")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }

using BiletSatis.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace BiletSatis.Tests;

// Testler gerçek SQL Server semantiğine (DATEADD, GETUTCDATE(), atomik UPDATE...WHERE)
// dayandığı için InMemory/SQLite yerine ayrı bir test veritabanına karşı çalışır.
public class DatabaseFixture : IAsyncLifetime
{
    public const string ConnectionString =
        "Server=localhost;Database=BiletSatisDb_Test;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

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

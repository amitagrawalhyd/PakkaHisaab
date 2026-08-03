using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Services;
using PakkaHisaab.Infrastructure.Data;

namespace PakkaHisaab.Api.Tests;

/// <summary>
/// Regression test for a production incident: AppDbContext.TranslationSettingsRow (the DbSet
/// property, named to avoid a C# identifier clash with the TranslationSettings type inside the
/// DbContext class body) must still map to the real "TranslationSettings" table. The other
/// tests in this project use Database.EnsureCreated(), which derives its schema FROM the EF
/// model — so a DbSet-name/table-name mismatch is invisible there; the model and the
/// EnsureCreated-generated schema always agree with each other by construction. Production's
/// schema is instead created independently via db/00N_*.sql, so this test creates the table by
/// raw SQL exactly as that migration does, without EnsureCreated, to catch the same drift.
/// </summary>
public sealed class TranslationSettingsStoreSchemaTests : IDisposable
{
    readonly SqliteConnection _connection;
    readonly AppDbContext _db;

    public TranslationSettingsStoreSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using (var create = _connection.CreateCommand())
        {
            // Mirrors db/005_translation_settings.sql's real column names/types, deliberately
            // NOT via EnsureCreated — this must be the table name production actually has.
            create.CommandText = """
                CREATE TABLE TranslationSettings (
                    Id INTEGER PRIMARY KEY,
                    Enabled INTEGER NOT NULL,
                    Provider TEXT NOT NULL
                );
                INSERT INTO TranslationSettings (Id, Enabled, Provider) VALUES (1, 0, 'GoogleFree');
                """;
            create.ExecuteNonQuery();
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetAsync_ReadsTheRealTranslationSettingsTable()
    {
        var store = new TranslationSettingsStore(_db);

        var (enabled, provider) = await store.GetAsync();

        Assert.False(enabled);
        Assert.Equal("GoogleFree", provider);
    }
}

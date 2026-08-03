using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Services;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using PakkaHisaab.Shared.Sync;

namespace PakkaHisaab.Api.Tests;

/// <summary>
/// Exercises the English-translation side effect of SyncService.PushAsync end-to-end against a
/// real (in-memory SQLite) EF Core provider — the ingestion point for every Helper/LedgerEntry
/// name and note a household user can type in any language.
/// </summary>
public sealed class SyncServiceTranslationTests : IDisposable
{
    readonly SqliteConnection _connection;
    readonly AppDbContext _db;
    readonly Guid _userId = Guid.NewGuid();

    public SyncServiceTranslationTests()
    {
        // A shared, kept-open in-memory SQLite connection — closing it (Dispose) is what
        // actually tears down the in-memory database.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _db.Users.Add(new User { Id = _userId, Email = "owner@test.local", DisplayName = "Owner" });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    static SyncPushRequest PushOneHelper(HelperDto helper) => new()
    {
        ClientBatchId = Guid.NewGuid(),
        DeviceId = "test-device",
        Helpers = { helper }
    };

    HelperDto NewHelper(string name) => new()
    {
        Id = Guid.NewGuid(), Name = name, WageType = WageType.MonthlySalary,
        MonthlyWage = 5000, ModifiedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public async Task NewHelper_TranslatesNameOnFirstSync()
    {
        var translator = new FakeTranslationService();
        var sut = new SyncService(_db, translator);
        var helper = NewHelper("राजू");

        await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        var saved = await _db.Helpers.SingleAsync(h => h.Id == helper.Id);
        Assert.Equal("[en] राजू", saved.NameEnglish);
        Assert.Equal(new[] { "राजू" }, translator.Requests);
    }

    [Fact]
    public async Task ReSyncingWithUnchangedName_DoesNotReTranslate()
    {
        var translator = new FakeTranslationService();
        var sut = new SyncService(_db, translator);
        var helper = NewHelper("Geeta");

        await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        // Same name, later timestamp, only the wage changed — a totally unrelated edit.
        helper.MonthlyWage = 6000;
        helper.ModifiedAtUtc = DateTime.UtcNow.AddSeconds(1);
        await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        Assert.Single(translator.Requests); // billed for the translation exactly once
        var saved = await _db.Helpers.SingleAsync(h => h.Id == helper.Id);
        Assert.Equal(6000, saved.MonthlyWage);
    }

    [Fact]
    public async Task ChangingName_ReTranslates()
    {
        var translator = new FakeTranslationService();
        var sut = new SyncService(_db, translator);
        var helper = NewHelper("Geeta");
        await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        helper.Name = "Geetanjali";
        helper.ModifiedAtUtc = DateTime.UtcNow.AddSeconds(1);
        await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        Assert.Equal(new[] { "Geeta", "Geetanjali" }, translator.Requests);
        var saved = await _db.Helpers.SingleAsync(h => h.Id == helper.Id);
        Assert.Equal("[en] Geetanjali", saved.NameEnglish);
    }

    [Fact]
    public async Task TranslationFailure_StillSavesTheHelper()
    {
        var translator = new FakeTranslationService { ShouldFail = true };
        var sut = new SyncService(_db, translator);
        var helper = NewHelper("Some Name");

        var response = await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        Assert.Empty(response.Conflicts);
        var saved = await _db.Helpers.SingleAsync(h => h.Id == helper.Id);
        Assert.Equal("Some Name", saved.Name); // the write itself must never be blocked
        Assert.Null(saved.NameEnglish);        // falls back to the original at display time
    }

    [Fact]
    public async Task LedgerNote_IsTranslated_AndNullNoteIsSkipped()
    {
        var translator = new FakeTranslationService();
        var sut = new SyncService(_db, translator);
        var helper = NewHelper("Geeta");
        await sut.PushAsync(_userId, PushOneHelper(helper), CancellationToken.None);

        var withNote = new LedgerEntryDto
        {
            Id = Guid.NewGuid(), HelperId = helper.Id, Type = LedgerEntryType.Advance, Amount = 500,
            Period = "2026-08", Note = "उधार दिया", ModifiedAtUtc = DateTime.UtcNow
        };
        var withoutNote = new LedgerEntryDto
        {
            Id = Guid.NewGuid(), HelperId = helper.Id, Type = LedgerEntryType.Bonus, Amount = 200,
            Period = "2026-08", Note = null, ModifiedAtUtc = DateTime.UtcNow
        };

        await sut.PushAsync(_userId, new SyncPushRequest
        {
            ClientBatchId = Guid.NewGuid(), DeviceId = "test-device",
            LedgerEntries = { withNote, withoutNote }
        }, CancellationToken.None);

        var savedWithNote = await _db.LedgerEntries.SingleAsync(l => l.Id == withNote.Id);
        var savedWithoutNote = await _db.LedgerEntries.SingleAsync(l => l.Id == withoutNote.Id);

        Assert.Equal("[en] उधार दिया", savedWithNote.NoteEnglish);
        Assert.Null(savedWithoutNote.NoteEnglish);
        Assert.DoesNotContain(translator.Requests, r => r is null);
    }
}

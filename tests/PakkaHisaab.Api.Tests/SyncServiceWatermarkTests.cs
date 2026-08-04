using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Services;
using PakkaHisaab.Infrastructure.Data;
using PakkaHisaab.Infrastructure.Services;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Enums;
using PakkaHisaab.Shared.Sync;

namespace PakkaHisaab.Api.Tests;

/// <summary>
/// Regression test: SyncPushResponse.ServerWatermark used to be computed before
/// SaveChangesAsync flushed the just-upserted rows' new RowVersions to the database, so it
/// always reported the pre-push watermark instead of the post-push one. Found while verifying
/// that settlements actually reach the server for Admin reporting — harmless for the current
/// MAUI client (it takes its watermark from the separate /sync/pull call instead), but a real
/// bug in the API contract that any other consumer of the push response would trip over.
/// </summary>
public sealed class SyncServiceWatermarkTests : IDisposable
{
    readonly SqliteConnection _connection;
    readonly AppDbContext _db;
    readonly Guid _userId = Guid.NewGuid();

    public SyncServiceWatermarkTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
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

    [Fact]
    public async Task ServerWatermark_ReflectsTheRowVersionsJustAccepted()
    {
        var sut = new SyncService(_db, new FakeTranslationService());
        var helper = new HelperDto
        {
            Id = Guid.NewGuid(), Name = "Geeta", WageType = WageType.MonthlySalary,
            MonthlyWage = 5000, ModifiedAtUtc = DateTime.UtcNow
        };

        var response = await sut.PushAsync(_userId, new SyncPushRequest
        {
            ClientBatchId = Guid.NewGuid(),
            DeviceId = "test-device",
            Helpers = { helper }
        }, CancellationToken.None);

        var acceptedVersion = Assert.Single(response.AcceptedRowVersions.Values);
        Assert.True(response.ServerWatermark >= acceptedVersion,
            $"ServerWatermark ({response.ServerWatermark}) should be at least the RowVersion " +
            $"just accepted ({acceptedVersion}) — it must reflect the post-push state.");
    }

    [Fact]
    public async Task ServerWatermark_AdvancesAcrossSuccessivePushes()
    {
        var sut = new SyncService(_db, new FakeTranslationService());
        var helper1 = new HelperDto { Id = Guid.NewGuid(), Name = "Geeta", ModifiedAtUtc = DateTime.UtcNow };
        var helper2 = new HelperDto { Id = Guid.NewGuid(), Name = "Raju", ModifiedAtUtc = DateTime.UtcNow };

        var first = await sut.PushAsync(_userId, new SyncPushRequest
        {
            ClientBatchId = Guid.NewGuid(), DeviceId = "d1", Helpers = { helper1 }
        }, CancellationToken.None);

        var second = await sut.PushAsync(_userId, new SyncPushRequest
        {
            ClientBatchId = Guid.NewGuid(), DeviceId = "d1", Helpers = { helper2 }
        }, CancellationToken.None);

        Assert.True(second.ServerWatermark > first.ServerWatermark);
    }
}

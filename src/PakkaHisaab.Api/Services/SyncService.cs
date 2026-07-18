using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PakkaHisaab.Api.Data;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Sync;

namespace PakkaHisaab.Api.Services;

public interface ISyncService
{
    Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request, CancellationToken ct);
    Task<SyncPullResponse> PullAsync(Guid userId, SyncPullRequest request, CancellationToken ct);
}

/// <summary>
/// Idempotent delta-sync.
///
/// PUSH — wrapped in one transaction:
///   1. If ClientBatchId was already processed, replay the stored response verbatim
///      (the client's retry after a lost response applies no double effects).
///   2. Per record: last-writer-wins on ModifiedAtUtc. Newer client copy → upsert + fresh
///      RowVersion from the global sequence. Older → reported in Conflicts (authoritative
///      copy reaches the client on its next pull).
///
/// PULL — every record whose RowVersion is greater than the client's watermark.
/// </summary>
public sealed class SyncService : ISyncService
{
    readonly AppDbContext _db;

    public SyncService(AppDbContext db) => _db = db;

    public async Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request, CancellationToken ct)
    {
        // Idempotency check
        var existing = await _db.SyncBatches.FindAsync(new object[] { request.ClientBatchId }, ct);
        if (existing is not null && existing.UserId == userId)
        {
            var replay = JsonSerializer.Deserialize<SyncPushResponse>(existing.ResponseJson)!;
            replay.AlreadyProcessed = true;
            return replay;
        }

        // EnableRetryOnFailure (Program.cs, for Azure SQL free-tier cold-start) installs a
        // retrying execution strategy, which refuses to run a user-initiated BeginTransactionAsync
        // unless the whole attempt — including the transaction — is retried as one unit via
        // CreateExecutionStrategy().ExecuteAsync; otherwise a mid-cold-start retry throws
        // InvalidOperationException instead of transparently retrying.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var response = new SyncPushResponse { ClientBatchId = request.ClientBatchId };

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            foreach (var dto in request.Helpers)
                await UpsertAsync<Helper, HelperDto>(userId, dto, response, MapHelper, ct);
            foreach (var dto in request.Attendance)
                await UpsertAsync<AttendanceEntry, AttendanceDto>(userId, dto, response, MapAttendance, ct);
            foreach (var dto in request.LedgerEntries)
                await UpsertAsync<LedgerEntry, LedgerEntryDto>(userId, dto, response, MapLedger, ct);
            foreach (var dto in request.Settlements)
                await UpsertAsync<Settlement, SettlementDto>(userId, dto, response, MapSettlement, ct);

            response.ServerWatermark = await CurrentWatermarkAsync(userId, ct);

            _db.SyncBatches.Add(new SyncBatch
            {
                ClientBatchId = request.ClientBatchId,
                UserId = userId,
                DeviceId = request.DeviceId,
                ProcessedAtUtc = DateTime.UtcNow,
                ResponseJson = JsonSerializer.Serialize(response)
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return response;
        });
    }

    public async Task<SyncPullResponse> PullAsync(Guid userId, SyncPullRequest request, CancellationToken ct)
    {
        long since = request.SinceWatermark;

        var response = new SyncPullResponse
        {
            Helpers = (await _db.Helpers.AsNoTracking()
                    .Where(x => x.UserId == userId && x.RowVersion > since).ToListAsync(ct))
                .Select(ToDto).ToList(),
            Attendance = (await _db.Attendance.AsNoTracking()
                    .Where(x => x.UserId == userId && x.RowVersion > since).ToListAsync(ct))
                .Select(ToDto).ToList(),
            LedgerEntries = (await _db.LedgerEntries.AsNoTracking()
                    .Where(x => x.UserId == userId && x.RowVersion > since).ToListAsync(ct))
                .Select(ToDto).ToList(),
            Settlements = (await _db.Settlements.AsNoTracking()
                    .Where(x => x.UserId == userId && x.RowVersion > since).ToListAsync(ct))
                .Select(ToDto).ToList()
        };

        response.NewWatermark = await CurrentWatermarkAsync(userId, ct);
        return response;
    }

    // ---------- internals ----------

    async Task<long> CurrentWatermarkAsync(Guid userId, CancellationToken ct)
    {
        long max = 0;
        max = Math.Max(max, await _db.Helpers.Where(x => x.UserId == userId)
            .Select(x => (long?)x.RowVersion).MaxAsync(ct) ?? 0);
        max = Math.Max(max, await _db.Attendance.Where(x => x.UserId == userId)
            .Select(x => (long?)x.RowVersion).MaxAsync(ct) ?? 0);
        max = Math.Max(max, await _db.LedgerEntries.Where(x => x.UserId == userId)
            .Select(x => (long?)x.RowVersion).MaxAsync(ct) ?? 0);
        max = Math.Max(max, await _db.Settlements.Where(x => x.UserId == userId)
            .Select(x => (long?)x.RowVersion).MaxAsync(ct) ?? 0);
        return max;
    }

    async Task UpsertAsync<TEntity, TDto>(
        Guid userId, TDto dto, SyncPushResponse response,
        Action<TDto, TEntity> map, CancellationToken ct)
        where TEntity : SyncEntityBase, new()
        where TDto : ISyncEntity
    {
        var set = _db.Set<TEntity>();
        var entity = await set.FirstOrDefaultAsync(x => x.Id == dto.Id, ct);

        if (entity is not null && entity.UserId != userId)
        {
            // Foreign record with a colliding GUID — reject silently as a conflict.
            response.Conflicts.Add(dto.Id);
            return;
        }

        if (entity is not null && entity.ModifiedAtUtc >= dto.ModifiedAtUtc)
        {
            // Server copy is newer (another device won). Client fetches it via pull.
            response.Conflicts.Add(dto.Id);
            return;
        }

        entity ??= set.Add(new TEntity { Id = dto.Id, UserId = userId }).Entity;
        map(dto, entity);
        entity.UserId = userId;
        entity.ModifiedAtUtc = dto.ModifiedAtUtc;
        entity.IsDeleted = dto.IsDeleted;
        entity.RowVersion = await _db.NextRowVersionAsync(ct);
        response.AcceptedRowVersions[dto.Id] = entity.RowVersion;
    }

    // ---------- mapping (DTO ⇄ entity) ----------

    static void MapHelper(HelperDto d, Helper e)
    {
        e.Name = d.Name; e.WhatsAppNumber = d.WhatsAppNumber; e.UpiId = d.UpiId;
        e.Category = d.Category; e.WageType = d.WageType; e.MonthlyWage = d.MonthlyWage;
        e.RatePerUnit = d.RatePerUnit; e.UnitLabel = d.UnitLabel;
        e.MonthlyAllowedAbsences = d.MonthlyAllowedAbsences;
        e.CarryOverLeaveAllowed = d.CarryOverLeaveAllowed;
        e.CarriedOverLeaves = d.CarriedOverLeaves; e.IsActive = d.IsActive;
    }

    static void MapAttendance(AttendanceDto d, AttendanceEntry e)
    {
        e.HelperId = d.HelperId; e.Date = d.Date; e.Status = d.Status;
        e.UnitsDelivered = d.UnitsDelivered;
    }

    static void MapLedger(LedgerEntryDto d, LedgerEntry e)
    {
        e.HelperId = d.HelperId; e.Type = d.Type; e.Amount = d.Amount; e.Method = d.Method;
        e.Note = d.Note; e.Period = d.Period; e.OccurredAtUtc = d.OccurredAtUtc;
        e.UpiTransactionRef = d.UpiTransactionRef;
    }

    static void MapSettlement(SettlementDto d, Settlement e)
    {
        e.HelperId = d.HelperId; e.Period = d.Period; e.Status = d.Status;
        e.FinalPayable = d.FinalPayable; e.PaidAtUtc = d.PaidAtUtc;
    }

    static HelperDto ToDto(Helper e) => new()
    {
        Id = e.Id, Name = e.Name, WhatsAppNumber = e.WhatsAppNumber, UpiId = e.UpiId,
        Category = e.Category, WageType = e.WageType, MonthlyWage = e.MonthlyWage,
        RatePerUnit = e.RatePerUnit, UnitLabel = e.UnitLabel,
        MonthlyAllowedAbsences = e.MonthlyAllowedAbsences,
        CarryOverLeaveAllowed = e.CarryOverLeaveAllowed, CarriedOverLeaves = e.CarriedOverLeaves,
        IsActive = e.IsActive, ModifiedAtUtc = e.ModifiedAtUtc, RowVersion = e.RowVersion,
        IsDeleted = e.IsDeleted
    };

    static AttendanceDto ToDto(AttendanceEntry e) => new()
    {
        Id = e.Id, HelperId = e.HelperId, Date = e.Date, Status = e.Status,
        UnitsDelivered = e.UnitsDelivered, ModifiedAtUtc = e.ModifiedAtUtc,
        RowVersion = e.RowVersion, IsDeleted = e.IsDeleted
    };

    static LedgerEntryDto ToDto(LedgerEntry e) => new()
    {
        Id = e.Id, HelperId = e.HelperId, Type = e.Type, Amount = e.Amount, Method = e.Method,
        Note = e.Note, Period = e.Period, OccurredAtUtc = e.OccurredAtUtc,
        UpiTransactionRef = e.UpiTransactionRef, ModifiedAtUtc = e.ModifiedAtUtc,
        RowVersion = e.RowVersion, IsDeleted = e.IsDeleted
    };

    static SettlementDto ToDto(Settlement e) => new()
    {
        Id = e.Id, HelperId = e.HelperId, Period = e.Period, Status = e.Status,
        FinalPayable = e.FinalPayable, PaidAtUtc = e.PaidAtUtc,
        ModifiedAtUtc = e.ModifiedAtUtc, RowVersion = e.RowVersion, IsDeleted = e.IsDeleted
    };
}

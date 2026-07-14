using PakkaHisaab.Maui.Data;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Maui.Models;
using PakkaHisaab.Shared.Sync;
using Shiny.Jobs;

namespace PakkaHisaab.Maui.Services;

public interface ISyncEngine
{
    bool IsSuspended { get; }
    /// <summary>Demo mode ON ⇒ suspended: the Shiny job becomes a hard no-op.</summary>
    void SetSuspended(bool suspended);
    /// <summary>Fire-and-forget nudge after a local write — never blocks the UI thread.</summary>
    Task RequestSyncAsync();
    /// <summary>Full push+pull cycle. Called by the background job and by manual pull-to-refresh.</summary>
    Task<bool> SynchronizeAsync(CancellationToken ct = default);
}

/// <summary>
/// Outbox-based delta sync. Local SQLite is the source of truth for the UI; this engine
/// silently drains IsDirty rows to POST /sync/push (idempotent via ClientBatchId) and applies
/// server deltas from POST /sync/pull using the stored watermark. All I/O happens off the
/// main thread; failures simply leave rows dirty for the next Shiny run.
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    readonly ILocalDatabase _db;
    readonly IApiClient _api;
    readonly ISessionService _session;
    readonly ITelemetryService _telemetry;
    readonly SemaphoreSlim _gate = new(1, 1);

    public SyncEngine(ILocalDatabase db, IApiClient api, ISessionService session, ITelemetryService telemetry)
    {
        _db = db;
        _api = api;
        _session = session;
        _telemetry = telemetry;
    }

    public bool IsSuspended { get; private set; }

    public void SetSuspended(bool suspended) => IsSuspended = suspended;

    public Task RequestSyncAsync()
    {
        if (IsSuspended || _session.IsDemo) return Task.CompletedTask;
        _ = Task.Run(() => SynchronizeAsync()); // background thread; UI stays at zero latency
        return Task.CompletedTask;
    }

    public async Task<bool> SynchronizeAsync(CancellationToken ct = default)
    {
        if (IsSuspended || _session.IsDemo) return true;
        if (await _session.GetAccessTokenAsync() is null) return false;
        if (!await _gate.WaitAsync(0, ct)) return true; // a cycle is already running

        try
        {
            var conn = await _db.GetConnectionAsync();

            // ---- PUSH (outbox drain) ----
            var dirtyHelpers = await conn.Table<LocalHelper>().Where(x => x.IsDirty).ToListAsync();
            var dirtyAttendance = await conn.Table<LocalAttendance>().Where(x => x.IsDirty).ToListAsync();
            var dirtyLedger = await conn.Table<LocalLedgerEntry>().Where(x => x.IsDirty).ToListAsync();
            var dirtySettlements = await conn.Table<LocalSettlement>().Where(x => x.IsDirty).ToListAsync();

            if (dirtyHelpers.Count + dirtyAttendance.Count + dirtyLedger.Count + dirtySettlements.Count > 0)
            {
                var push = new SyncPushRequest
                {
                    ClientBatchId = Guid.NewGuid(),
                    DeviceId = _session.DeviceId,
                    Helpers = dirtyHelpers.Select(x => x.ToDto()).ToList(),
                    Attendance = dirtyAttendance.Select(x => x.ToDto()).ToList(),
                    LedgerEntries = dirtyLedger.Select(x => x.ToDto()).ToList(),
                    Settlements = dirtySettlements.Select(x => x.ToDto()).ToList()
                };

                var pushRes = await _api.PushAsync(push, ct);
                if (pushRes is null) return false; // offline — rows stay dirty, retry later

                foreach (var h in dirtyHelpers) Accept(h, pushRes);
                foreach (var a in dirtyAttendance) Accept(a, pushRes);
                foreach (var l in dirtyLedger) Accept(l, pushRes);
                foreach (var s in dirtySettlements) Accept(s, pushRes);

                await conn.UpdateAllAsync(dirtyHelpers);
                await conn.UpdateAllAsync(dirtyAttendance);
                await conn.UpdateAllAsync(dirtyLedger);
                await conn.UpdateAllAsync(dirtySettlements);
            }

            // ---- PULL (server deltas since watermark) ----
            long watermark = long.Parse(
                Preferences.Default.Get(Constants.KeySyncWatermark, "0"));
            var pullRes = await _api.PullAsync(new SyncPullRequest
            {
                SinceWatermark = watermark,
                DeviceId = _session.DeviceId
            }, ct);
            if (pullRes is null) return false;

            foreach (var dto in pullRes.Helpers)
                await ApplyIfNewerAsync(conn, dto.ToLocal(), dto.ModifiedAtUtc);
            foreach (var dto in pullRes.Attendance)
                await ApplyIfNewerAsync(conn, dto.ToLocal(), dto.ModifiedAtUtc);
            foreach (var dto in pullRes.LedgerEntries)
                await ApplyIfNewerAsync(conn, dto.ToLocal(), dto.ModifiedAtUtc);
            foreach (var dto in pullRes.Settlements)
                await ApplyIfNewerAsync(conn, dto.ToLocal(), dto.ModifiedAtUtc);

            Preferences.Default.Set(Constants.KeySyncWatermark, pullRes.NewWatermark.ToString());
            return true;
        }
        catch (Exception ex)
        {
            _telemetry.TrackError(ex, "sync_cycle_failed");
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    static void Accept(LocalEntityBase row, SyncPushResponse res)
    {
        if (res.AcceptedRowVersions.TryGetValue(row.Id, out var version))
        {
            row.RowVersion = version;
            row.IsDirty = false;
        }
        // Conflict rows keep IsDirty=false too: the authoritative copy arrives via pull.
        else if (res.Conflicts.Contains(row.Id))
        {
            row.IsDirty = false;
        }
    }

    /// <summary>Last-writer-wins apply that never clobbers a local unsynced edit.</summary>
    static async Task ApplyIfNewerAsync<T>(SQLite.SQLiteAsyncConnection conn, T incoming, DateTime incomingModified)
        where T : LocalEntityBase, new()
    {
        var existing = await conn.FindAsync<T>(((LocalEntityBase)incoming).Id);
        if (existing is not null && (existing.IsDirty || existing.ModifiedAtUtc > incomingModified))
            return;
        await conn.InsertOrReplaceAsync(incoming);
    }
}

/// <summary>
/// Shiny.NET background job — the OS schedules this even when the app is backgrounded,
/// so ledger entries recorded offline reach the server without the user reopening the app.
/// </summary>
public class SyncJob : IJob
{
    readonly ISyncEngine _engine;
    readonly ISessionService _session;

    public SyncJob(ISyncEngine engine, ISessionService session)
    {
        _engine = engine;
        _session = session;
    }

    public async Task Run(JobInfo jobInfo, CancellationToken cancelToken)
    {
        // Sync suspension: demo sessions must never touch the network.
        if (_session.IsDemo || _engine.IsSuspended)
            return;

        await _engine.SynchronizeAsync(cancelToken);
    }
}

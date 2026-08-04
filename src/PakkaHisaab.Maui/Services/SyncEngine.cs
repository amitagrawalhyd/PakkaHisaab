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
        // Fire-and-forget: SynchronizeAsync is fully self-guarded (see below) and can never
        // throw, but this catch is cheap insurance — an uncaught exception on a background
        // Task nobody awaits becomes an unobserved task exception, which must never be the
        // thing that decides whether the app stays up (see App.xaml.cs's belt-and-suspenders
        // TaskScheduler.UnobservedTaskException handler for the same reasoning).
        _ = Task.Run(async () =>
        {
            try { await SynchronizeAsync(); }
            catch (Exception ex) { _telemetry.TrackError(ex, "sync_fire_and_forget"); }
        });
        return Task.CompletedTask;
    }

    /// <summary>A payment (or any other local write) must never appear to fail — or crash the
    /// app — just because the background sync to the server hit a network blip at that exact
    /// moment. Everything here, including token lookup and the busy-gate check, is inside one
    /// outer try/catch so a failure can only ever come back as "false" (rows stay dirty and are
    /// retried on the next Shiny job run or manual refresh) — never an exception that escapes
    /// this method.</summary>
    public async Task<bool> SynchronizeAsync(CancellationToken ct = default)
    {
        try
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

                    // Transient blips (a dropped packet, a free-tier App Service cold start)
                    // are exactly the kind of failure a payment-time sync is likely to hit —
                    // worth a couple of quick retries before giving up and leaving rows dirty
                    // for the next scheduled Shiny run.
                    var pushRes = await WithRetryAsync(() => _api.PushAsync(push, ct), ct);
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
                var pullRes = await WithRetryAsync(() => _api.PullAsync(new SyncPullRequest
                {
                    SinceWatermark = watermark,
                    DeviceId = _session.DeviceId
                }, ct), ct);
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
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception ex)
        {
            _telemetry.TrackError(ex, "sync_cycle_failed");
            return false;
        }
    }

    /// <summary>Retries a flaky network call up to 3 attempts total (400ms, 800ms backoff)
    /// before giving up. Retries both on an exception (timeout, DNS blip, connection reset)
    /// and on a null result (the API client's own "request failed" signal) — either way, the
    /// caller treats a final null as "offline, try again on the next sync cycle" exactly as
    /// before; this just avoids surrendering to a single transient hiccup first.</summary>
    static async Task<T?> WithRetryAsync<T>(Func<Task<T?>> action, CancellationToken ct, int maxAttempts = 3)
        where T : class
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var result = await action();
                if (result is not null || attempt == maxAttempts) return result;
            }
            catch when (attempt < maxAttempts)
            {
                // fall through to backoff below and try again
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
        }

        return null;
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

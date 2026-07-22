using PakkaHisaab.Maui.Data;
using PakkaHisaab.Shared.Dtos;

namespace PakkaHisaab.Maui.Services;

public interface IAuthService
{
    Task<AuthOutcome> LoginAsync(string email, string password);
    Task<AuthOutcome> RegisterAsync(string email, string password, string displayName);
    /// <summary>Zero-login reviewer track: isolated demo DB, no backend, sync suspended.</summary>
    Task StartDemoAsync();
    Task LogoutAsync();
    Task<bool> DeleteAccountAsync(string password);
}

public sealed class AuthService : IAuthService
{
    readonly IApiClient _api;
    readonly ISessionService _session;
    readonly ILocalDatabase _db;
    readonly ISyncEngine _sync;
    readonly INotificationService _notifications;
    readonly ITelemetryService _telemetry;

    public AuthService(IApiClient api, ISessionService session, ILocalDatabase db,
        ISyncEngine sync, INotificationService notifications, ITelemetryService telemetry)
    {
        _api = api;
        _session = session;
        _db = db;
        _sync = sync;
        _notifications = notifications;
        _telemetry = telemetry;
    }

    public async Task<AuthOutcome> LoginAsync(string email, string password)
    {
        var outcome = await _api.LoginAsync(new LoginRequest(email, password));
        if (outcome.Auth is null) return outcome;

        await _session.SetTokensAsync(outcome.Auth.UserId, outcome.Auth.AccessToken, outcome.Auth.RefreshToken);
        await _db.SwitchAsync(demo: false);   // initialize the local SQLite store upon login
        _sync.SetSuspended(false);
        await _sync.RequestSyncAsync();       // initial pull of any server-side data
        _telemetry.Track("login_success");
        return outcome;
    }

    public async Task<AuthOutcome> RegisterAsync(string email, string password, string displayName)
    {
        var outcome = await _api.RegisterAsync(new RegisterRequest(email, password, displayName, null));
        if (outcome.Auth is null) return outcome;

        await _session.SetTokensAsync(outcome.Auth.UserId, outcome.Auth.AccessToken, outcome.Auth.RefreshToken);
        await _db.SwitchAsync(demo: false);
        _sync.SetSuspended(false);
        _telemetry.Track("register_success");
        return outcome;
    }

    public async Task StartDemoAsync()
    {
        await _db.SwitchAsync(demo: true);            // Demo_PakkaHisaab.db — fully isolated
        _sync.SetSuspended(true);                     // explicitly disable the Shiny sync job
        var conn = await _db.GetConnectionAsync();
        await Data.DemoSeeder.SeedAsync(conn);        // Geeta + Raju mock data
        _telemetry.Track("demo_started");
    }

    public async Task LogoutAsync()
    {
        await _notifications.CancelAllAsync();
        await _session.ClearAsync();
        await _db.SwitchAsync(demo: false);
    }

    public async Task<bool> DeleteAccountAsync(string password)
    {
        // App Store compliance: server erases the account + all synced data, then local wipe.
        var ok = _session.IsDemo ||
                 await _api.DeleteAccountAsync(new DeleteAccountRequest(password, "DELETE"));
        if (!ok) return false;

        await _db.WipeCurrentAsync();
        await LogoutAsync();
        _telemetry.Track("account_deleted");
        return true;
    }
}

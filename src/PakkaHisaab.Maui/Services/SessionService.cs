using PakkaHisaab.Maui.Helpers;

namespace PakkaHisaab.Maui.Services;

public interface ISessionService
{
    bool IsDemo { get; set; }
    Guid? UserId { get; }
    string DeviceId { get; }
    Task<string?> GetAccessTokenAsync();
    Task SetTokensAsync(Guid userId, string accessToken, string refreshToken);
    Task<string?> GetRefreshTokenAsync();
    Task ClearAsync();
}

/// <summary>Holds the authenticated/demo session state. Tokens live in platform SecureStorage
/// (Keychain / EncryptedSharedPreferences), never in plain preferences.</summary>
public sealed class SessionService : ISessionService
{
    public bool IsDemo
    {
        get => Preferences.Default.Get(Constants.KeyIsDemo, false);
        set => Preferences.Default.Set(Constants.KeyIsDemo, value);
    }

    public Guid? UserId
    {
        get
        {
            var raw = Preferences.Default.Get(Constants.KeyUserId, string.Empty);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string DeviceId
    {
        get
        {
            var id = Preferences.Default.Get(Constants.KeyDeviceId, string.Empty);
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString("N");
                Preferences.Default.Set(Constants.KeyDeviceId, id);
            }
            return id;
        }
    }

    public Task<string?> GetAccessTokenAsync() =>
        SecureStorage.Default.GetAsync(Constants.KeyAccessToken);

    public Task<string?> GetRefreshTokenAsync() =>
        SecureStorage.Default.GetAsync(Constants.KeyRefreshToken);

    public async Task SetTokensAsync(Guid userId, string accessToken, string refreshToken)
    {
        Preferences.Default.Set(Constants.KeyUserId, userId.ToString());
        await SecureStorage.Default.SetAsync(Constants.KeyAccessToken, accessToken);
        await SecureStorage.Default.SetAsync(Constants.KeyRefreshToken, refreshToken);
    }

    public Task ClearAsync()
    {
        Preferences.Default.Remove(Constants.KeyUserId);
        Preferences.Default.Remove(Constants.KeyIsDemo);
        Preferences.Default.Remove(Constants.KeySyncWatermark);
        SecureStorage.Default.Remove(Constants.KeyAccessToken);
        SecureStorage.Default.Remove(Constants.KeyRefreshToken);
        return Task.CompletedTask;
    }
}

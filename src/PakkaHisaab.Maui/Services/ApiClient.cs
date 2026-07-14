using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Http;
using PakkaHisaab.Maui.Helpers;
using PakkaHisaab.Shared.Dtos;
using PakkaHisaab.Shared.Sync;

namespace PakkaHisaab.Maui.Services;

public interface IApiClient
{
    Task<AuthResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken ct = default);
    Task<SyncPullResponse?> PullAsync(SyncPullRequest request, CancellationToken ct = default);
    Task<bool> DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default);
}

/// <summary>Thin typed HTTP client. Attaches the JWT, refreshes it once on 401, then retries.</summary>
public sealed class ApiClient : IApiClient
{
    readonly IHttpClientFactory _factory;
    readonly ISessionService _session;

    public ApiClient(IHttpClientFactory factory, ISessionService session)
    {
        _factory = factory;
        _session = session;
    }

    HttpClient Create()
    {
        var client = _factory.CreateClient("pakkahisaab");
        client.BaseAddress = new Uri(Constants.ApiBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        using var http = Create();
        var res = await http.PostAsJsonAsync("/auth/register", request, ct);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<AuthResponse>(ct) : null;
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        using var http = Create();
        var res = await http.PostAsJsonAsync("/auth/login", request, ct);
        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<AuthResponse>(ct) : null;
    }

    public Task<SyncPushResponse?> PushAsync(SyncPushRequest request, CancellationToken ct = default) =>
        SendAuthorizedAsync<SyncPushRequest, SyncPushResponse>("/sync/push", request, ct);

    public Task<SyncPullResponse?> PullAsync(SyncPullRequest request, CancellationToken ct = default) =>
        SendAuthorizedAsync<SyncPullRequest, SyncPullResponse>("/sync/pull", request, ct);

    public async Task<bool> DeleteAccountAsync(DeleteAccountRequest request, CancellationToken ct = default)
    {
        using var http = await CreateAuthorizedAsync();
        using var msg = new HttpRequestMessage(HttpMethod.Delete, "/account")
        {
            Content = JsonContent.Create(request)
        };
        var res = await http.SendAsync(msg, ct);
        return res.IsSuccessStatusCode;
    }

    async Task<HttpClient> CreateAuthorizedAsync()
    {
        var http = Create();
        var token = await _session.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    async Task<TRes?> SendAuthorizedAsync<TReq, TRes>(string path, TReq body, CancellationToken ct)
        where TRes : class
    {
        using var http = await CreateAuthorizedAsync();
        var res = await http.PostAsJsonAsync(path, body, ct);

        if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized && await TryRefreshAsync(ct))
        {
            using var retryHttp = await CreateAuthorizedAsync();
            res = await retryHttp.PostAsJsonAsync(path, body, ct);
        }

        return res.IsSuccessStatusCode ? await res.Content.ReadFromJsonAsync<TRes>(ct) : null;
    }

    async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var refresh = await _session.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refresh)) return false;

        using var http = Create();
        var res = await http.PostAsJsonAsync("/auth/refresh", new RefreshRequest(refresh), ct);
        if (!res.IsSuccessStatusCode) return false;

        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(ct);
        if (auth is null) return false;

        await _session.SetTokensAsync(auth.UserId, auth.AccessToken, auth.RefreshToken);
        return true;
    }
}

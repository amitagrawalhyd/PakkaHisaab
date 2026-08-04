using PakkaHisaab.Infrastructure.Services;

namespace PakkaHisaab.Api.Tests;

public sealed class FakeTranslationSettingsStore : ITranslationSettingsStore
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "GoogleFree";

    public Task<(bool Enabled, string Provider)> GetAsync(CancellationToken ct = default) =>
        Task.FromResult((Enabled, Provider));
}

/// <summary>Records every request it handles and replies with a fixed body/status — lets tests
/// assert *which* provider's endpoint was actually called without hitting the real network.</summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    public List<Uri> RequestedUris { get; } = new();
    public string ResponseBody { get; set; } = "[[[\"hello\",\"नमस्ते\",null,null,1]],null,\"hi\"]";
    public System.Net.HttpStatusCode StatusCode { get; set; } = System.Net.HttpStatusCode.OK;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestedUris.Add(request.RequestUri!);
        var response = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}

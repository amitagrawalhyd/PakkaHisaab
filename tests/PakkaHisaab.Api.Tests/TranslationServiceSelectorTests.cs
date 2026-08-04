using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PakkaHisaab.Infrastructure.Services;

namespace PakkaHisaab.Api.Tests;

public sealed class TranslationServiceSelectorTests
{
    static (TranslationServiceSelector Selector, RecordingHttpMessageHandler Handler, FakeTranslationSettingsStore Settings)
        Build(string apiKey = "test-key")
    {
        var handler = new RecordingHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GoogleTranslate:ApiKey"] = apiKey })
            .Build();

        var free = new GoogleFreeTranslateService(httpClient, NullLogger<GoogleFreeTranslateService>.Instance);
        var cloud = new GoogleCloudTranslateService(httpClient, config, NullLogger<GoogleCloudTranslateService>.Instance);
        var settings = new FakeTranslationSettingsStore();
        var selector = new TranslationServiceSelector(settings, free, cloud);
        return (selector, handler, settings);
    }

    [Fact]
    public async Task Disabled_NeverMakesAnHttpCall()
    {
        var (selector, handler, settings) = Build();
        settings.Enabled = false;

        var result = await selector.TranslateToEnglishAsync("कुछ भी");

        Assert.Null(result);
        Assert.Empty(handler.RequestedUris); // the whole point of the gate: zero network calls
    }

    [Fact]
    public async Task Enabled_GoogleFree_CallsTheFreeEndpoint()
    {
        var (selector, handler, settings) = Build();
        settings.Enabled = true;
        settings.Provider = "GoogleFree";
        handler.ResponseBody = "[[[\"Hello\",\"नमस्ते\",null,null,1]],null,\"hi\"]";

        var result = await selector.TranslateToEnglishAsync("नमस्ते");

        Assert.Equal("Hello", result);
        Assert.Single(handler.RequestedUris);
        Assert.Contains("translate.googleapis.com/translate_a/single", handler.RequestedUris[0].ToString());
    }

    [Fact]
    public async Task Enabled_GoogleCloud_CallsTheOfficialEndpoint()
    {
        var (selector, handler, settings) = Build();
        settings.Enabled = true;
        settings.Provider = "GoogleCloud";
        handler.ResponseBody = """{"data":{"translations":[{"translatedText":"Hello"}]}}""";

        var result = await selector.TranslateToEnglishAsync("नमस्ते");

        Assert.Equal("Hello", result);
        Assert.Single(handler.RequestedUris);
        Assert.Contains("translation.googleapis.com/language/translate/v2", handler.RequestedUris[0].ToString());
    }

    [Fact]
    public async Task Enabled_GoogleCloud_WithNoApiKey_ReturnsNullWithoutCalling()
    {
        var (selector, handler, settings) = Build(apiKey: "");
        settings.Enabled = true;
        settings.Provider = "GoogleCloud";

        var result = await selector.TranslateToEnglishAsync("नमस्ते");

        Assert.Null(result);
        Assert.Empty(handler.RequestedUris);
    }

    [Fact]
    public async Task EmptyOrWhitespaceText_NeverCallsAnything()
    {
        var (selector, handler, settings) = Build();
        settings.Enabled = true;

        Assert.Null(await selector.TranslateToEnglishAsync(null));
        Assert.Null(await selector.TranslateToEnglishAsync("   "));
        Assert.Empty(handler.RequestedUris);
    }
}

public sealed class GoogleFreeTranslateServiceParsingTests
{
    [Fact]
    public void ParseResponse_SingleChunk_ReturnsTranslatedText()
    {
        var json = "[[[\"Hello\",\"नमस्ते\",null,null,1]],null,\"hi\"]";
        Assert.Equal("Hello", GoogleFreeTranslateService.ParseResponse(json));
    }

    [Fact]
    public void ParseResponse_MultipleChunks_ConcatenatesInOrder()
    {
        // Long input gets split into several translated/original pairs by the endpoint.
        var json = "[[[\"Hello \",\"नमस्ते \",null,null,1],[\"world\",\"दुनिया\",null,null,1]],null,\"hi\"]";
        Assert.Equal("Hello world", GoogleFreeTranslateService.ParseResponse(json));
    }

    [Fact]
    public void ParseResponse_UnexpectedShape_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(GoogleFreeTranslateService.ParseResponse("{}"));
        Assert.Null(GoogleFreeTranslateService.ParseResponse("null"));
        Assert.Null(GoogleFreeTranslateService.ParseResponse("[]"));
    }
}

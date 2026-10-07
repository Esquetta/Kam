using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Infrastructure.Services;
using System.Net;
using System.Text;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class OpenAiTranscriptionServiceTests
{
    [Theory]
    [InlineData("https://api.openai.com/v1", "https://api.openai.com/v1/audio/transcriptions")]
    [InlineData("https://api.groq.com/openai/v1/", "https://api.groq.com/openai/v1/audio/transcriptions")]
    [InlineData("http://localhost:8000/v1/audio/transcriptions", "http://localhost:8000/v1/audio/transcriptions")]
    public void BuildRequestUri_AppendsTheTranscriptionPath(string endpoint, string expected)
    {
        OpenAiTranscriptionService.BuildRequestUri(endpoint)!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("not a url")]
    public void BuildRequestUri_RejectsOtherSchemes(string endpoint)
    {
        OpenAiTranscriptionService.BuildRequestUri(endpoint).Should().BeNull();
    }

    [Fact]
    public async Task ConvertToTextAsync_PostsWaveWithModelLanguageAndKey()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"text":"Spotify'ı aç"}""");
        using var service = CreateService(handler, new()
        {
            ["Voice:SpeechEngine"] = "OpenAI",
            ["Voice:Language"] = "tr-TR",
            ["Voice:SpeechApi:Endpoint"] = "https://api.groq.com/openai/v1",
            ["Voice:SpeechApi:Model"] = "whisper-large-v3-turbo",
            ["Voice:SpeechApi:ApiKey"] = "gsk-test"
        });

        var result = await service.ConvertToTextAsync(new byte[3200]);

        result.Text.Should().Be("Spotify'ı aç");
        result.IsSuccess.Should().BeTrue();
        handler.Request!.RequestUri!.ToString().Should().Be("https://api.groq.com/openai/v1/audio/transcriptions");
        handler.Request.Headers.Authorization!.Parameter.Should().Be("gsk-test");
        handler.Body.Should().Contain("whisper-large-v3-turbo").And.Contain("name=language").And.Contain("RIFF");
        handler.Body.Should().Contain("\r\n\r\ntr\r\n");
    }

    [Fact]
    public async Task ConvertToTextAsync_ReportsHttpErrors()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
        using var service = CreateService(handler, new() { ["Voice:SpeechApi:ApiKey"] = "wrong" });

        var result = await service.ConvertToTextAsync(new byte[3200]);

        result.Text.Should().BeEmpty();
        result.ErrorMessage.Should().Contain("401");
    }

    [Theory]
    [InlineData("""{"text":"hello"}""", "hello")]
    [InlineData("plain text", "plain text")]
    [InlineData("""{"other":1}""", "")]
    public void ReadText_ReadsJsonOrPlainText(string body, string expected)
    {
        OpenAiTranscriptionService.ReadText(body).Should().Be(expected);
    }

    private static OpenAiTranscriptionService CreateService(HttpMessageHandler handler, Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new OpenAiTranscriptionService(new HandlerFactory(handler), configuration, NullLogger<OpenAiTranscriptionService>.Instance);
    }

    private sealed class HandlerFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = Encoding.Latin1.GetString(await request.Content!.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}

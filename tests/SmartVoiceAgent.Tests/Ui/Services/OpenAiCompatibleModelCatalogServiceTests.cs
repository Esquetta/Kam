using System.Net;
using System.Net.Http;
using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class OpenAiCompatibleModelCatalogServiceTests
{
    [Fact]
    public async Task GetModelIdsAsync_UsesModelsEndpointAndReturnsTextGenerationModels()
    {
        using var handler = new StubHttpMessageHandler("""{"object":"list","data":[{"id":"gpt-5.2","owned_by":"openai"},{"id":"text-embedding-3-small","owned_by":"openai"},{"id":"gpt-4.1-mini","owned_by":"openai"}]}""");
        using var httpClient = new HttpClient(handler);
        var service = new OpenAiCompatibleModelCatalogService(httpClient);

        var models = await service.GetModelIdsAsync(new ModelProviderProfile
        {
            Provider = ModelProviderType.OpenAI,
            Endpoint = "https://api.openai.com/v1",
            ApiKey = "sk-test"
        });

        handler.RequestUri.Should().Be(new Uri("https://api.openai.com/v1/models"));
        handler.AuthorizationHeader.Should().Be("Bearer sk-test");
        models.Should().Equal("gpt-5.2", "gpt-4.1-mini");
    }

    private sealed class StubHttpMessageHandler(string responseContent) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent)
            });
        }
    }
}

using FluentAssertions;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Extensions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class DeepSeekProviderTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-deepseek-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProviderNumericValues_ArePersistedAndDoNotChange()
    {
        ((int)ModelProviderType.OpenRouter).Should().Be(0);
        ((int)ModelProviderType.OpenAICompatible).Should().Be(1);
        ((int)ModelProviderType.Ollama).Should().Be(2);
        ((int)ModelProviderType.OpenAI).Should().Be(3);
        ((int)ModelProviderType.Anthropic).Should().Be(4);
        ((int)ModelProviderType.DeepSeek).Should().Be(5);
    }

    [Theory]
    [InlineData("DeepSeek")]
    [InlineData("deepseek")]
    public void ProviderName_ParsesToDeepSeek(string name)
    {
        Enum.TryParse<ModelProviderType>(name, ignoreCase: true, out var parsed).Should().BeTrue();
        parsed.Should().Be(ModelProviderType.DeepSeek);
        ModelProviderType.DeepSeek.ToString().Should().Be("DeepSeek");
    }

    [Fact]
    public void DeepSeekProfile_RoundTripsThroughSettings()
    {
        using (var service = new JsonSettingsService(_settingsDirectory))
        {
            service.ModelProviderProfiles =
            [
                new ModelProviderProfile
                {
                    Id = "deepseek-chat",
                    Provider = ModelProviderType.DeepSeek,
                    Endpoint = ModelProviderEndpoints.DeepSeek,
                    ApiKey = "sk-test",
                    ModelId = "deepseek-chat",
                    Roles = [ModelProviderRole.Chat],
                    Enabled = true
                }
            ];
            service.ActiveChatProfileId = "deepseek-chat";
            service.Save();
        }

        using var reloaded = new JsonSettingsService(_settingsDirectory);

        reloaded.ModelProviderProfiles.Should().ContainSingle(p =>
            p.Provider == ModelProviderType.DeepSeek
            && p.Endpoint == "https://api.deepseek.com/v1"
            && p.ModelId == "deepseek-chat");
    }

    [Fact]
    public void Mapper_DeepSeekProfile_WritesProviderEndpointAndModel()
    {
        var overrides = AiRuntimeConfigurationMapper.CreateAiServiceOverrides(
            [
                new ModelProviderProfile
                {
                    Id = "deepseek-chat",
                    Provider = ModelProviderType.DeepSeek,
                    Endpoint = ModelProviderEndpoints.DeepSeek,
                    ApiKey = "sk-deepseek",
                    ModelId = "deepseek-reasoner",
                    Roles = [ModelProviderRole.Chat],
                    Enabled = true
                }
            ],
            "deepseek-chat",
            "deepseek-chat");

        overrides.Should().Contain("AIService:Chat:Provider", "DeepSeek");
        overrides.Should().Contain("AIService:Chat:Endpoint", "https://api.deepseek.com/v1");
        overrides.Should().Contain("AIService:Chat:ApiKey", "sk-deepseek");
        overrides.Should().Contain("AIService:Chat:ModelId", "deepseek-reasoner");
    }

    [Fact]
    public void ChatClient_DeepSeekProvider_IsCreatedAsOpenAiCompatibleClient()
    {
        var client = ServiceCollectionExtensions.CreateChatClient(new AIServiceConfiguration
        {
            Provider = "DeepSeek",
            Endpoint = ModelProviderEndpoints.DeepSeek,
            ApiKey = "sk-deepseek",
            ModelId = "deepseek-chat"
        });

        client.Should().NotBeNull();
    }

    [Fact]
    public void ModelDefaults_DeepSeek_ListsChatAndReasoner()
    {
        ModelCatalogDefaults.GetModelIds(ModelProviderType.DeepSeek, "deepseek-chat")
            .Should().Equal("deepseek-chat", "deepseek-reasoner");
    }

    [Fact]
    public void SettingsViewModel_SelectingDeepSeek_PrefillsEndpointAndModelAndShowsKeyLink()
    {
        using var settingsService = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settingsService);

        viewModel.AiProviders.Should().Contain("DeepSeek");
        viewModel.ChatShowsDeepSeekKeyLink.Should().BeFalse();

        viewModel.ChatProvider = "DeepSeek";

        viewModel.ChatEndpoint.Should().Be("https://api.deepseek.com/v1");
        viewModel.ChatModelId.Should().Be("deepseek-chat");
        viewModel.ChatModelOptions.Should().Equal("deepseek-chat", "deepseek-reasoner");
        viewModel.IsChatModelCatalogBacked.Should().BeTrue();
        viewModel.ChatShowsDeepSeekKeyLink.Should().BeTrue();
        viewModel.AiShowsDeepSeekKeyLink.Should().BeFalse();
        settingsService.ModelProviderProfiles.Should().ContainSingle(p =>
            p.Provider == ModelProviderType.DeepSeek && p.Roles.Contains(ModelProviderRole.Chat));
    }

    [Fact]
    public async Task LiveCatalog_DeepSeek_UsesBearerKeyAndNoOpenRouterQuery()
    {
        var handler = new CapturingHandler("""{"data":[{"id":"deepseek-chat"},{"id":"deepseek-reasoner"}]}""");
        using var catalog = new OpenAiCompatibleModelCatalogService(new HttpClient(handler));

        var models = await catalog.GetModelsAsync(new ModelProviderProfile
        {
            Id = "deepseek",
            Provider = ModelProviderType.DeepSeek,
            Endpoint = ModelProviderEndpoints.DeepSeek,
            ApiKey = "sk-deepseek",
            ModelId = "deepseek-chat"
        });

        handler.Request!.RequestUri!.ToString().Should().Be("https://api.deepseek.com/v1/models");
        handler.Request.Headers.Authorization!.Parameter.Should().Be("sk-deepseek");
        handler.Request.Headers.Contains("HTTP-Referer").Should().BeFalse();
        models.Select(m => m.ModelId).Should().BeEquivalentTo("deepseek-chat", "deepseek-reasoner");
        models.Should().OnlyContain(m => m.Provider == ModelProviderType.DeepSeek && m.ProviderId == "deepseek");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private sealed class CapturingHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}

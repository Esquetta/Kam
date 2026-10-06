using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using SmartVoiceAgent.Infrastructure.Agent.Conf;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class ChatClientCacheTests
{
    [Fact]
    public void Get_ReusesTheClientUntilASettingChanges()
    {
        var built = new List<string>();
        var cache = new ChatClientCache(config =>
        {
            built.Add($"{config.ModelId}/{config.ApiKey}");
            return Mock.Of<IChatClient>();
        });

        var first = cache.Get(Config("model-a", "key-1"));
        var again = cache.Get(Config("model-a", "key-1"));
        var otherKey = cache.Get(Config("model-a", "key-2"));
        var otherModel = cache.Get(Config("model-b", "key-1"));

        again.Should().BeSameAs(first);
        otherKey.Should().NotBeSameAs(first);
        otherModel.Should().NotBeSameAs(first);
        built.Should().Equal("model-a/key-1", "model-a/key-2", "model-b/key-1");
    }

    [Fact]
    public void Get_KeepsOnlyRecentClients()
    {
        var builds = 0;
        var cache = new ChatClientCache(_ => { builds++; return Mock.Of<IChatClient>(); });

        for (var index = 0; index < 9; index++)
        {
            cache.Get(Config($"model-{index}", "key"));
        }

        cache.Get(Config("model-8", "key"));
        builds.Should().Be(9);
        cache.Get(Config("model-0", "key"));
        builds.Should().Be(10, "the oldest client was dropped to stay within the limit");
    }

    [Fact]
    public void WithModel_ChangesOnlyTheModel()
    {
        var config = Config("model-a", "key-1");

        var other = ChatClientCache.WithModel(config, " model-b ");

        other.ModelId.Should().Be("model-b");
        other.Provider.Should().Be(config.Provider);
        other.Endpoint.Should().Be(config.Endpoint);
        other.ApiKey.Should().Be(config.ApiKey);
        other.DefaultMaxTokens.Should().Be(config.DefaultMaxTokens);
        ChatClientCache.WithModel(config, null).Should().BeSameAs(config);
        ChatClientCache.WithModel(config, "model-a").Should().BeSameAs(config);
    }

    [Fact]
    public async Task ConfiguredChatClient_AsksForTheClientOnEveryRequest()
    {
        var current = new Mock<IChatClient>();
        current
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "first")));
        var next = new Mock<IChatClient>();
        next
            .Setup(client => client.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "second")));
        var active = current.Object;
        using var client = new ConfiguredChatClient(() => active);
        var cancellationToken = TestContext.Current.CancellationToken;

        var first = await client.GetResponseAsync("hi", cancellationToken: cancellationToken);
        active = next.Object;
        var second = await client.GetResponseAsync("hi", cancellationToken: cancellationToken);

        first.Text.Should().Be("first");
        second.Text.Should().Be("second");
    }

    private static AIServiceConfiguration Config(string model, string key) => new()
    {
        Provider = "OpenRouter",
        Endpoint = "https://openrouter.ai/api/v1",
        ApiKey = key,
        ModelId = model,
        DefaultMaxTokens = 1200,
        DefaultTemperature = 0.2f
    };
}

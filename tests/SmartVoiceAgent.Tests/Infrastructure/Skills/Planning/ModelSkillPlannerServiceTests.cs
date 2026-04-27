using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Planning;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.Planning;

public class ModelSkillPlannerServiceTests
{
    [Fact]
    public async Task CreatePlanAsync_RequestsJsonOnlyPlanAndParsesValidJson()
    {
        var chatClient = new RecordingChatClient("""
        {"skillId":"apps.open","arguments":{"applicationName":"Spotify"},"confidence":0.94,"requiresConfirmation":false,"reasoning":"Open Spotify"}
        """);
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest
        {
            Id = "apps.open",
            DisplayName = "Open Application",
            Enabled = true,
            Arguments =
            [
                new SkillArgumentDefinition
                {
                    Name = "applicationName",
                    Type = SkillArgumentType.String,
                    Required = true
                }
            ]
        });
        var planner = new ModelSkillPlannerService(chatClient, registry);

        var result = await planner.CreatePlanAsync("Spotify ac");

        result.IsValid.Should().BeTrue();
        result.Plan!.SkillId.Should().Be("apps.open");
        result.Plan.Arguments["applicationName"].GetString().Should().Be("Spotify");
        chatClient.LastMessages
            .Select(message => message.Text)
            .Any(text => text is not null
                && text.Contains("Return exactly one JSON object", StringComparison.Ordinal)
                && text.Contains("Do not call tools", StringComparison.Ordinal))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task CreatePlanAsync_ResponseWithTextAroundJson_ReturnsFailure()
    {
        var chatClient = new RecordingChatClient("""
        I will do that.
        {"skillId":"apps.open","arguments":{"applicationName":"Spotify"},"confidence":0.94,"requiresConfirmation":false,"reasoning":"Open Spotify"}
        """);
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest { Id = "apps.open", DisplayName = "Open Application", Enabled = true });
        var planner = new ModelSkillPlannerService(chatClient, registry);

        var result = await planner.CreatePlanAsync("Spotify ac");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("single JSON object");
    }

    [Fact]
    public async Task CreatePlanAsync_RecordsPlannerTraceForValidResponse()
    {
        var chatClient = new RecordingChatClient("""
        {"skillId":"apps.open","arguments":{"applicationName":"Spotify"},"confidence":0.94,"requiresConfirmation":false,"reasoning":"Open Spotify"}
        """);
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest { Id = "apps.open", DisplayName = "Open Application", Enabled = true });
        var traceStore = new InMemorySkillPlannerTraceStore();
        var planner = new ModelSkillPlannerService(chatClient, registry, traceStore);

        await planner.CreatePlanAsync("Spotify ac");

        var trace = traceStore.GetRecent().Should().ContainSingle().Subject;
        trace.UserRequest.Should().Be("Spotify ac");
        trace.IsValid.Should().BeTrue();
        trace.SkillId.Should().Be("apps.open");
        trace.RawResponse.Should().Contain("\"skillId\":\"apps.open\"");
        trace.SystemPrompt.Should().Contain("Return exactly one JSON object");
        trace.DurationMilliseconds.Should().BeGreaterThanOrEqualTo(0);
        trace.AvailableSkillCount.Should().Be(1);
    }

    [Fact]
    public async Task CreatePlanAsync_RecordsPlannerTraceForInvalidResponse()
    {
        var chatClient = new RecordingChatClient("I will open Spotify.");
        var registry = new InMemorySkillRegistry();
        registry.Register(new KamSkillManifest { Id = "apps.open", DisplayName = "Open Application", Enabled = true });
        var traceStore = new InMemorySkillPlannerTraceStore();
        var planner = new ModelSkillPlannerService(chatClient, registry, traceStore);

        var result = await planner.CreatePlanAsync("Spotify ac");

        result.IsValid.Should().BeFalse();
        var trace = traceStore.GetRecent().Should().ContainSingle().Subject;
        trace.IsValid.Should().BeFalse();
        trace.ErrorMessage.Should().Contain("single JSON object");
        trace.RawResponse.Should().Be("I will open Spotify.");
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly string _response;

        public RecordingChatClient(string response)
        {
            _response = response;
        }

        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return EmptyAsync();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }

        private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync()
        {
            await Task.Yield();
            yield break;
        }
    }
}

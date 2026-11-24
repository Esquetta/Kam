using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using SmartVoiceAgent.Infrastructure.Skills.Planning;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills;

public class SkillArgumentAllowedValuesTests
{
    private static KamSkillManifest DeviceControl =>
        BuiltInSkillManifestCatalog.CreateAll().Single(skill => skill.Id == "system.device.control");

    [Fact]
    public void ToolSchema_ListsAllowedValuesAsEnum()
    {
        var schema = SkillToolProvider.BuildSchema(DeviceControl);

        var values = schema.GetProperty("properties").GetProperty("action").GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString());
        values.Should().Contain(["increase", "decrease", "on", "off", "status"]);
    }

    [Fact]
    public async Task Planner_ShowsAllowedValuesAndRejectsOthers()
    {
        var client = new FixedChatClient(
            """{"skillId":"system.device.control","arguments":{"deviceName":"volume","action":"volume_up"},"confidence":0.9}""");
        var registry = new InMemorySkillRegistry();
        registry.Register(DeviceControl);

        var result = await new ModelSkillPlannerService(client, registry).CreatePlanAsync("turn it up");

        client.SystemPrompt.Should().Contain("(one of increase|decrease|mute|unmute|on|off|status|shutdown|restart|sleep|lock)");
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().StartWith("Argument 'action' must be one of:");
    }

    [Theory]
    [InlineData("Volume", "INCREASE")]
    [InlineData("wifi", "off")]
    public async Task Planner_AcceptsAllowedValuesInAnyCase(string deviceName, string action)
    {
        var client = new FixedChatClient(
            $$"""{"skillId":"system.device.control","arguments":{"deviceName":"{{deviceName}}","action":"{{action}}"},"confidence":0.9}""");
        var registry = new InMemorySkillRegistry();
        registry.Register(DeviceControl);

        var result = await new ModelSkillPlannerService(client, registry).CreatePlanAsync("request");

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public void ClipboardSkills_MentionTheTurkishWord()
    {
        BuiltInSkillManifestCatalog.CreateAll()
            .Where(skill => skill.Id.StartsWith("clipboard.", StringComparison.Ordinal))
            .Should().OnlyContain(skill => skill.Description.Contains("pano", StringComparison.Ordinal));
    }

    private sealed class FixedChatClient : IChatClient
    {
        private readonly string _response;

        public FixedChatClient(string response)
        {
            _response = response;
        }

        public string SystemPrompt { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            SystemPrompt = messages.First(message => message.Role == ChatRole.System).Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}

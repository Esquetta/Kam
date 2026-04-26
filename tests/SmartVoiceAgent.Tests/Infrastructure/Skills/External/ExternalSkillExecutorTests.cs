using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
using SmartVoiceAgent.Infrastructure.Skills.External;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.External;

public sealed class ExternalSkillExecutorTests : IDisposable
{
    private readonly string _workspace;

    public ExternalSkillExecutorTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"kam-external-skill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workspace);
    }

    [Fact]
    public void CanExecute_LocalAndSkillsShManifests_ReturnsTrue()
    {
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.desktop-navigation", "local"));
        registry.Register(CreateManifest("skills-sh.browser-control", "skills.sh"));
        var executor = new ExternalSkillExecutor(new RecordingChatClient("ok"), registry);

        executor.CanExecute("local.desktop-navigation").Should().BeTrue();
        executor.CanExecute("skills-sh.browser-control").Should().BeTrue();
        executor.CanExecute("apps.open").Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_LocalSkill_SendsSkillMarkdownAndArgumentsToChatClient()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Follow desktop navigation instructions.");
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.desktop-navigation", "local", skillDirectory));
        var chatClient = new RecordingChatClient("Focused the Settings window.");
        var executor = new ExternalSkillExecutor(chatClient, registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "local.desktop-navigation",
            new { input = "Open settings", target = "settings" }));

        result.Success.Should().BeTrue();
        result.Message.Should().Be("Focused the Settings window.");
        var promptText = string.Join(
            Environment.NewLine,
            chatClient.LastMessages.Select(message => message.Text));
        promptText.Should().Contain("Follow desktop navigation instructions.");
        promptText.Should().Contain("\"target\":\"settings\"");
        promptText.Should().Contain("Open settings");
        promptText.Should().Contain("Do not call tools");
    }

    [Fact]
    public async Task ExecuteAsync_MissingSkillMarkdown_ReturnsFailure()
    {
        var missingSkillDirectory = Path.Combine(_workspace, "missing-skill");
        Directory.CreateDirectory(missingSkillDirectory);
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.missing-skill", "local", missingSkillDirectory));
        var executor = new ExternalSkillExecutor(new RecordingChatClient("ok"), registry);

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "local.missing-skill",
            new { input = "Run missing skill" }));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SKILL.md");
    }

    [Fact]
    public async Task ExecuteAsync_ApprovedLocalSkillThroughPipeline_ReturnsExternalResult()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Follow desktop navigation instructions.");
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.desktop-navigation", "local", skillDirectory));
        var chatClient = new RecordingChatClient("External skill smoke passed.");
        var pipeline = new SkillExecutionPipeline(
            registry,
            [new ExternalSkillExecutor(chatClient, registry)]);

        var result = await pipeline.ExecuteAsync(SkillPlan.FromObject(
            "local.desktop-navigation",
            new { input = "Use the imported desktop navigation skill" }));

        result.Success.Should().BeTrue();
        result.Status.Should().Be(SkillExecutionStatus.Succeeded);
        result.Message.Should().Be("External skill smoke passed.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private string CreateSkillDirectory(string name, string body)
    {
        var skillDirectory = Path.Combine(_workspace, name);
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(
            Path.Combine(skillDirectory, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: Test skill
            ---

            # {name}

            {body}
            """);

        return skillDirectory;
    }

    private static KamSkillManifest CreateManifest(
        string skillId,
        string executorType,
        string? installedFrom = null)
    {
        return new KamSkillManifest
        {
            Id = skillId,
            DisplayName = skillId,
            Description = "Imported skill",
            Source = $"{executorType}:{installedFrom ?? skillId}",
            ExecutorType = executorType,
            InstalledFrom = installedFrom ?? string.Empty,
            Enabled = true,
            ReviewRequired = false,
            Arguments =
            [
                new SkillArgumentDefinition
                {
                    Name = "input",
                    Type = SkillArgumentType.String,
                    Required = true
                }
            ]
        };
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

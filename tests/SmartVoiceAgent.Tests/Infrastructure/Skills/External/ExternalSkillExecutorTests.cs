using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
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
        registry.Register(CreateManifest(
            "local.desktop-navigation",
            "local",
            skillDirectory,
            [SkillPermission.ProcessControl],
            [SkillPermission.ProcessControl]));
        var chatClient = new RecordingChatClient("""
        {"message":"Focused the Settings window.","actions":[{"type":"focus_window","target":"settings"}]}
        """);
        var actionExecutor = new RecordingSkillActionExecutor();
        var executor = new ExternalSkillExecutor(
            chatClient,
            registry,
            new StaticSkillRuntimeContextProvider("Settings"),
            actionExecutor);

        var plan = SkillPlan.FromObject(
            "local.desktop-navigation",
            new { input = "Open settings", target = "settings" });
        plan.IsConfirmedByUser = true;

        var result = await executor.ExecuteAsync(plan);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Focused the Settings window.");
        actionExecutor.LastPlan.Should().NotBeNull();
        actionExecutor.LastPlan!.Actions.Should().ContainSingle(action => action.Type == "focus_window");
        var promptText = string.Join(
            Environment.NewLine,
            chatClient.LastMessages.Select(message => message.Text));
        promptText.Should().Contain("Follow desktop navigation instructions.");
        promptText.Should().Contain("\"target\":\"settings\"");
        promptText.Should().Contain("Open settings");
        promptText.Should().Contain("Runtime context JSON");
        promptText.Should().Contain("Settings");
        promptText.Should().Contain("Do not call tools");
    }

    [Fact]
    public async Task ExecuteAsync_ActionPlanMissingPermission_ReturnsReviewRequiredAndRecordsAudit()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Follow desktop navigation instructions.");
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.desktop-navigation", "local", skillDirectory));
        var chatClient = new RecordingChatClient("""
        {"message":"Typing into the active window.","actions":[{"type":"type_text","text":"hello"}]}
        """);
        var auditLog = new RecordingSkillAuditLogService();
        var actionExecutor = new RecordingSkillActionExecutor();
        var executor = new ExternalSkillExecutor(
            chatClient,
            registry,
            new StaticSkillRuntimeContextProvider("Editor"),
            actionExecutor,
            auditLog,
            "openrouter/test-model");

        var result = await executor.ExecuteAsync(SkillPlan.FromObject(
            "local.desktop-navigation",
            new { input = "type hello" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ReviewRequired);
        result.ErrorCode.Should().Be("action_confirmation_required");
        result.ErrorMessage.Should().Contain("type_text");
        result.ErrorMessage.Should().Contain(nameof(SkillPermission.ProcessControl));
        actionExecutor.LastPlan.Should().BeNull();
        auditLog.Records.Should().ContainSingle(record =>
            record.SkillId == "local.desktop-navigation"
            && record.ModelId == "openrouter/test-model"
            && record.Status == SkillExecutionStatus.ReviewRequired
            && record.ActionTypes.Contains(SkillActionTypes.TypeText)
            && record.MissingPermissions.Contains(SkillPermission.ProcessControl));
    }

    [Fact]
    public async Task ExecuteAsync_ConfirmedPlanAllowsRiskyActionAndRecordsSuccessAudit()
    {
        var skillDirectory = CreateSkillDirectory(
            "desktop-navigation",
            "Follow desktop navigation instructions.");
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.desktop-navigation", "local", skillDirectory));
        var chatClient = new RecordingChatClient("""
        {"message":"Typing into the active window.","actions":[{"type":"type_text","text":"hello"}]}
        """);
        var auditLog = new RecordingSkillAuditLogService();
        var actionExecutor = new RecordingSkillActionExecutor();
        var executor = new ExternalSkillExecutor(
            chatClient,
            registry,
            new StaticSkillRuntimeContextProvider("Editor"),
            actionExecutor,
            auditLog,
            "openrouter/test-model");
        var plan = SkillPlan.FromObject("local.desktop-navigation", new { input = "type hello" });
        plan.IsConfirmedByUser = true;

        var result = await executor.ExecuteAsync(plan);

        result.Success.Should().BeTrue();
        actionExecutor.LastPlan.Should().NotBeNull();
        auditLog.Records.Should().ContainSingle(record =>
            record.Status == SkillExecutionStatus.Succeeded
            && record.ActionTypes.Contains(SkillActionTypes.TypeText)
            && record.MissingPermissions.Contains(SkillPermission.ProcessControl));
    }

    [Fact]
    public async Task ExecuteAsync_MissingSkillMarkdown_ReturnsFailure()
    {
        var missingSkillDirectory = Path.Combine(_workspace, "missing-skill");
        Directory.CreateDirectory(missingSkillDirectory);
        var registry = new InMemorySkillRegistry();
        registry.Register(CreateManifest("local.missing-skill", "local", missingSkillDirectory));
        var executor = new ExternalSkillExecutor(
            new RecordingChatClient("""{"message":"ok","actions":[]}"""),
            registry);

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
        var chatClient = new RecordingChatClient("""{"message":"External skill smoke passed.","actions":[]}""");
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
        string? installedFrom = null,
        IReadOnlyCollection<SkillPermission>? permissions = null,
        IReadOnlyCollection<SkillPermission>? grantedPermissions = null)
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
            Permissions = permissions?.ToList() ?? [],
            GrantedPermissions = grantedPermissions?.ToList() ?? [],
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

    private sealed class StaticSkillRuntimeContextProvider : ISkillRuntimeContextProvider
    {
        private readonly string _activeWindowTitle;

        public StaticSkillRuntimeContextProvider(string activeWindowTitle)
        {
            _activeWindowTitle = activeWindowTitle;
        }

        public Task<SkillRuntimeContext> CreateAsync(
            SkillPlan plan,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SkillRuntimeContext
            {
                UserInput = plan.Arguments["input"].GetString() ?? string.Empty,
                OperatingSystem = "Windows",
                ActiveWindow = new SkillRuntimeWindow
                {
                    Title = _activeWindowTitle,
                    ProcessName = "SystemSettings"
                }
            });
        }
    }

    private sealed class RecordingSkillActionExecutor : ISkillActionExecutor
    {
        public SkillActionPlan? LastPlan { get; private set; }

        public Task<SkillActionExecutionResult> ExecuteAsync(
            SkillActionPlan plan,
            CancellationToken cancellationToken = default)
        {
            LastPlan = plan;
            return Task.FromResult(SkillActionExecutionResult.Succeeded(
                $"{plan.Message} Executed {plan.Actions.Count} action(s).",
                [SkillActionStepResult.Succeeded(plan.Actions[0].Type, "ok")]));
        }
    }

    private sealed class RecordingSkillAuditLogService : ISkillAuditLogService
    {
        public List<SkillAuditRecord> Records { get; } = [];

        public Task RecordAsync(
            SkillAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<SkillAuditRecord>> GetRecentAsync(
            int maxCount = 100,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<SkillAuditRecord>>(Records.TakeLast(maxCount).ToArray());
        }
    }
}

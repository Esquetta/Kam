using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Agents;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent;

public sealed class RuntimeAgentFactoryTests
{
    [Fact]
    public async Task RunAsync_RecordsCompletedRunAndReturnsRunId()
    {
        var chatClient = new RecordingChatClient("agent response");
        var runStore = new InMemoryRuntimeAgentRunStore();
        var uiLogService = new RecordingUiLogService();
        var factory = new RuntimeAgentFactory(
            () => chatClient,
            NullLogger<RuntimeAgentFactory>.Instance,
            runStore,
            null,
            uiLogService,
            "gpt-test");

        var result = await factory.RunAsync(new RuntimeAgentRequest(
            "Repo Agent!",
            "coding",
            "Inspect the workspace.",
            [
                new RuntimeAgentToolObservation("workspace.map", "Workspace Map: D:\\repo", true)
            ]));

        result.RunId.Should().NotBeNullOrWhiteSpace();
        result.AgentName.Should().Be("RepoAgent");
        result.Response.Should().Be("agent response");

        var run = runStore.Get(result.RunId);
        run.Should().NotBeNull();
        run!.Status.Should().Be(RuntimeAgentRunStatus.Succeeded);
        run.Response.Should().Be("agent response");
        run.ModelId.Should().Be("gpt-test");
        run.CompletedAt.Should().NotBeNull();
        run.ToolObservations.Should().ContainSingle(observation => observation.SkillId == "workspace.map");

        chatClient.Messages.Should().HaveCount(2);
        chatClient.Messages[0].Role.Should().Be(ChatRole.System);
        chatClient.Messages[0].Text.Should().Contain("Read-only tool context");
        chatClient.Messages[0].Text.Should().Contain("workspace.map");
        chatClient.Messages[1].Text.Should().Be("Inspect the workspace.");
        uiLogService.Entries.Should().Contain(entry =>
            entry.AgentName == "RepoAgent"
            && entry.Message == "Completed."
            && entry.RunId == result.RunId);
    }

    [Fact]
    public async Task RunAsync_WhenChatClientThrows_RecordsFailedRun()
    {
        var runStore = new InMemoryRuntimeAgentRunStore();
        var factory = new RuntimeAgentFactory(
            () => new ThrowingChatClient(new InvalidOperationException("provider failed")),
            NullLogger<RuntimeAgentFactory>.Instance,
            runStore,
            null,
            new RecordingUiLogService(),
            "gpt-test");

        var act = () => factory.RunAsync(new RuntimeAgentRequest(
            "FailureAgent",
            "general",
            "Run failing task."));

        await act.Should().ThrowAsync<InvalidOperationException>();

        var run = runStore.List().Should().ContainSingle().Subject;
        run.AgentName.Should().Be("FailureAgent");
        run.Status.Should().Be(RuntimeAgentRunStatus.Failed);
        run.ErrorMessage.Should().Contain("provider failed");
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_IncludesOnlyFirstFiveToolObservationsInSystemPrompt()
    {
        var chatClient = new RecordingChatClient("agent response");
        var runStore = new InMemoryRuntimeAgentRunStore();
        var factory = new RuntimeAgentFactory(
            () => chatClient,
            NullLogger<RuntimeAgentFactory>.Instance,
            runStore,
            null,
            new RecordingUiLogService(),
            "gpt-test");

        var observations = Enumerable.Range(1, 6)
            .Select(index => new RuntimeAgentToolObservation($"tool.{index}", $"summary {index}", true))
            .ToArray();

        await factory.RunAsync(new RuntimeAgentRequest(
            "ContextAgent",
            "coding",
            "Inspect the workspace.",
            observations));

        chatClient.Messages.Should().NotBeEmpty();
        var systemPrompt = chatClient.Messages[0].Text;
        systemPrompt.Should().Contain("tool.1");
        systemPrompt.Should().Contain("tool.5");
        systemPrompt.Should().NotContain("tool.6");
        runStore.List().Should().ContainSingle().Subject.ToolObservations.Should().HaveCount(6);
    }

    [Fact]
    public async Task RunAsync_WhenModelRequestsReadOnlyTools_RunsOneToolRoundAndReturnsFinalResponse()
    {
        var chatClient = new QueueingChatClient(
            """
            {"toolRequests":[{"tool":"workspace.search_text","query":"Needle"},{"tool":"shell.run","query":"rm"}]}
            """,
            "final answer");
        var runStore = new InMemoryRuntimeAgentRunStore();
        var uiLogService = new RecordingUiLogService();
        var toolService = new RecordingReadOnlyToolService([
            new RuntimeAgentToolObservation("workspace.search_text", "Needle found", true)
        ]);
        var factory = new RuntimeAgentFactory(
            () => chatClient,
            NullLogger<RuntimeAgentFactory>.Instance,
            runStore,
            toolService,
            uiLogService,
            "gpt-test");

        var result = await factory.RunAsync(new RuntimeAgentRequest(
            "LoopAgent",
            "coding",
            "Find Needle."));

        result.Response.Should().Be("final answer");
        chatClient.Calls.Should().Be(2);
        toolService.LastRequests.Should().ContainSingle();
        toolService.LastRequests![0].Tool.Should().Be("workspace.search_text");
        chatClient.SystemPrompts.Last().Should().Contain("Needle found");
        var run = runStore.List().Should().ContainSingle().Subject;
        run.Response.Should().Be("final answer");
        run.ToolObservations.Should().ContainSingle(observation => observation.SkillId == "workspace.search_text");
        uiLogService.Entries
            .Where(entry => entry.IsAgentUpdate)
            .Select(entry => entry.Message)
            .Should()
            .Contain([
                "Requested context: search text.",
                "Context ready: search text."
            ]);
        uiLogService.Entries.Select(entry => entry.Message)
            .Should()
            .NotContain(message => message.Contains("workspace.search_text", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WhenReadOnlyObservationFails_LogsUnavailableContextStep()
    {
        var chatClient = new QueueingChatClient(
            """
            {"toolRequests":[{"tool":"file.read_lines","path":"missing.cs"}]}
            """,
            "final answer");
        var uiLogService = new RecordingUiLogService();
        var toolService = new RecordingReadOnlyToolService([
            new RuntimeAgentToolObservation("file.read_lines", "File path is missing.", false)
        ]);
        var factory = new RuntimeAgentFactory(
            () => chatClient,
            NullLogger<RuntimeAgentFactory>.Instance,
            new InMemoryRuntimeAgentRunStore(),
            toolService,
            uiLogService,
            "gpt-test");

        await factory.RunAsync(new RuntimeAgentRequest(
            "LoopAgent",
            "coding",
            "Read missing.cs."));

        uiLogService.Entries
            .Where(entry => entry.IsAgentUpdate)
            .Select(entry => entry.Message)
            .Should()
            .Contain("Context unavailable: read file.");
    }

    [Fact]
    public async Task RunAsync_WhenToolRequestJsonIsInvalid_TreatsResponseAsFinalAnswer()
    {
        var chatClient = new QueueingChatClient("{ not valid json");
        var toolService = new RecordingReadOnlyToolService([]);
        var factory = new RuntimeAgentFactory(
            () => chatClient,
            NullLogger<RuntimeAgentFactory>.Instance,
            new InMemoryRuntimeAgentRunStore(),
            toolService,
            new RecordingUiLogService(),
            "gpt-test");

        var result = await factory.RunAsync(new RuntimeAgentRequest(
            "LoopAgent",
            "coding",
            "Inspect."));

        result.Response.Should().Be("{ not valid json");
        chatClient.Calls.Should().Be(1);
        toolService.LastRequests.Should().BeNull();
    }

    [Fact]
    public async Task RunAsync_WhenModelRequestsApprovalGatedActions_ReturnsActionRequests()
    {
        var chatClient = new QueueingChatClient(
            """
            {"message":"Patch is ready for review.","actionRequests":[{"action":"file.patch","filePath":"Program.cs","oldText":"old","newText":"new","expectedOccurrences":1},{"action":"tests.run","command":"dotnet test"}]}
            """);
        var uiLogService = new RecordingUiLogService();
        var factory = new RuntimeAgentFactory(
            () => chatClient,
            NullLogger<RuntimeAgentFactory>.Instance,
            new InMemoryRuntimeAgentRunStore(),
            null,
            uiLogService,
            "gpt-test");

        var result = await factory.RunAsync(new RuntimeAgentRequest(
            "LoopAgent",
            "coding",
            "Patch Program.cs."));

        result.Response.Should().Be("Patch is ready for review.");
        result.ActionRequests.Should().HaveCount(2);
        result.ActionRequests![0].Action.Should().Be("file.patch");
        result.ActionRequests[0].FilePath.Should().Be("Program.cs");
        result.ActionRequests[0].OldText.Should().Be("old");
        result.ActionRequests[0].NewText.Should().Be("new");
        result.ActionRequests[1].Action.Should().Be("tests.run");
        result.ActionRequests[1].Command.Should().Be("dotnet test");
        uiLogService.Entries.Should().Contain(entry => entry.Message == "Requested approval for 2 action(s).");
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly string _response;

        public RecordingChatClient(string response)
        {
            _response = response;
        }

        public List<ChatMessage> Messages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return EmptyAsync(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class QueueingChatClient : IChatClient
    {
        private readonly Queue<string> _responses;

        public QueueingChatClient(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public int Calls { get; private set; }

        public List<string> SystemPrompts { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            var messageList = messages.ToArray();
            SystemPrompts.Add(messageList.First(message => message.Role == ChatRole.System).Text ?? string.Empty);
            var response = _responses.Count > 0 ? _responses.Dequeue() : "fallback final";
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return EmptyAsync(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingReadOnlyToolService : IRuntimeAgentReadOnlyToolService
    {
        private readonly IReadOnlyList<RuntimeAgentToolObservation> _observations;

        public RecordingReadOnlyToolService(IReadOnlyList<RuntimeAgentToolObservation> observations)
        {
            _observations = observations;
        }

        public IReadOnlyList<RuntimeAgentReadOnlyToolRequest>? LastRequests { get; private set; }

        public Task<IReadOnlyList<RuntimeAgentToolObservation>> ExecuteAsync(
            IReadOnlyList<RuntimeAgentReadOnlyToolRequest> requests,
            CancellationToken cancellationToken = default)
        {
            LastRequests = requests;
            return Task.FromResult(_observations);
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly Exception _exception;

        public ThrowingChatClient(Exception exception)
        {
            _exception = exception;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            throw _exception;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return ThrowStreamingAsync(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }

        private async IAsyncEnumerable<ChatResponseUpdate> ThrowStreamingAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            throw _exception;
            #pragma warning disable CS0162
            yield break;
            #pragma warning restore CS0162
        }
    }

    private sealed class RecordingUiLogService : IUiLogService
    {
        public event EventHandler<UiLogEntry>? OnLogEntry;

        public List<UiLogEntry> Entries { get; } = [];

        public void Log(string message, LogLevel level = LogLevel.Information, string? source = null)
        {
            var entry = new UiLogEntry
            {
                Message = message,
                Level = level,
                Source = source
            };
            Entries.Add(entry);
            OnLogEntry?.Invoke(this, entry);
        }

        public void LogAgentUpdate(string agentName, string message, bool isComplete = false, string? runId = null)
        {
            var entry = new UiLogEntry
            {
                AgentName = agentName,
                RunId = runId,
                Message = message,
                IsAgentUpdate = true,
                IsComplete = isComplete
            };
            Entries.Add(entry);
            OnLogEntry?.Invoke(this, entry);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmptyAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }
}

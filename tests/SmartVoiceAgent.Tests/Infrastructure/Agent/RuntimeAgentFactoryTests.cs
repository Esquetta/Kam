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
        uiLogService.Entries.Should().Contain(entry => entry.AgentName == "RepoAgent" && entry.Message == "Completed.");
    }

    [Fact]
    public async Task RunAsync_WhenChatClientThrows_RecordsFailedRun()
    {
        var runStore = new InMemoryRuntimeAgentRunStore();
        var factory = new RuntimeAgentFactory(
            () => new ThrowingChatClient(new InvalidOperationException("provider failed")),
            NullLogger<RuntimeAgentFactory>.Instance,
            runStore,
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

        public void LogAgentUpdate(string agentName, string message, bool isComplete = false)
        {
            var entry = new UiLogEntry
            {
                AgentName = agentName,
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

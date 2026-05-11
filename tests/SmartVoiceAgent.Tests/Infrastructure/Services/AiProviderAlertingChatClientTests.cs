using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services;
using System.Runtime.CompilerServices;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class AiProviderAlertingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_WhenProviderRateLimits_LogsWarningAndRethrows()
    {
        var uiLogService = new RecordingUiLogService();
        var client = new AiProviderAlertingChatClient(
            new ThrowingChatClient(new InvalidOperationException("HTTP 429 rate limit api_key=sk-test-secret")),
            uiLogService,
            "gpt-5.5");

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        uiLogService.Entries.Should().ContainSingle();
        uiLogService.Entries[0].Level.Should().Be(LogLevel.Warning);
        uiLogService.Entries[0].Source.Should().Be("AI");
        uiLogService.Entries[0].Message.Should().Contain("AI_PROVIDER_RATE_LIMIT");
        uiLogService.Entries[0].Message.Should().Contain("gpt-5.5");
        uiLogService.Entries[0].Message.Should().NotContain("sk-test-secret");
    }

    [Fact]
    public async Task GetResponseAsync_WhenProviderQuotaFails_LogsBalanceWarning()
    {
        var uiLogService = new RecordingUiLogService();
        var client = new AiProviderAlertingChatClient(
            new ThrowingChatClient(new InvalidOperationException("insufficient_quota: billing credit exhausted")),
            uiLogService,
            "claude-sonnet-4-6");

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        uiLogService.Entries.Should().ContainSingle();
        uiLogService.Entries[0].Message.Should().Contain("AI_PROVIDER_QUOTA_OR_BALANCE");
    }

    [Fact]
    public async Task GetResponseAsync_WhenFailureIsNotProviderLimit_DoesNotLogWarning()
    {
        var uiLogService = new RecordingUiLogService();
        var client = new AiProviderAlertingChatClient(
            new ThrowingChatClient(new InvalidOperationException("local planner failed")),
            uiLogService,
            "gpt-5.5");

        var act = () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")]);

        await act.Should().ThrowAsync<InvalidOperationException>();
        uiLogService.Entries.Should().BeEmpty();
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

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
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
}

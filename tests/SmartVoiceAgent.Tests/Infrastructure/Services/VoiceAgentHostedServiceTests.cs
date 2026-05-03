using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Commands;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class VoiceAgentHostedServiceTests
{
    [Fact]
    public async Task ExecuteAsync_LegacyAgentToolFailure_DoesNotBlockCommandRuntime()
    {
        var commandInput = new CommandInputService();
        var runtime = new RecordingCommandRuntime(new CommandRuntimeResult(true, "apps.list completed")
        {
            SkillId = "apps.list",
            Status = SkillExecutionStatus.Succeeded
        });
        var resultPublished = new TaskCompletionSource<CommandResultEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        commandInput.OnResult += (_, args) => resultPublished.TrySetResult(args);

        var service = new VoiceAgentHostedService(
            runtime,
            new NoOpAgentRegistry(),
            new ThrowingAgentFactory(),
            NullLogger<VoiceAgentHostedService>.Instance,
            commandInput,
            new VoiceAgentHostControlService());

        await service.StartAsync(CancellationToken.None);
        try
        {
            commandInput.SubmitCommand("list installed apps");

            var published = await resultPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

            runtime.Commands.Should().ContainSingle("list installed apps");
            published.Success.Should().BeTrue();
            published.Command.Should().Be("list installed apps");
            published.Result.Should().Be("apps.list completed");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteAsync_PendingConfirmation_PublishesNonErrorResult()
    {
        var commandInput = new CommandInputService();
        var confirmationId = Guid.NewGuid();
        var runtime = new RecordingCommandRuntime(CommandRuntimeResult.PendingConfirmation(
            "Skill 'apps.open' requires confirmation before execution.",
            "apps.open",
            confirmationId));
        var resultPublished = new TaskCompletionSource<CommandResultEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        commandInput.OnResult += (_, args) => resultPublished.TrySetResult(args);

        var service = new VoiceAgentHostedService(
            runtime,
            new NoOpAgentRegistry(),
            new ThrowingAgentFactory(),
            NullLogger<VoiceAgentHostedService>.Instance,
            commandInput,
            new VoiceAgentHostControlService());

        await service.StartAsync(CancellationToken.None);
        try
        {
            commandInput.SubmitCommand("Open Spotify");

            var published = await resultPublished.Task.WaitAsync(TimeSpan.FromSeconds(2));

            published.Success.Should().BeTrue();
            published.Command.Should().Be("Open Spotify");
            published.Result.Should().Contain("requires confirmation");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingCommandRuntime : ICommandRuntimeService
    {
        private readonly CommandRuntimeResult _result;

        public RecordingCommandRuntime(CommandRuntimeResult result)
        {
            _result = result;
        }

        public List<string> Commands { get; } = [];

        public Task<CommandRuntimeResult> ExecuteAsync(
            string command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingAgentFactory : IAgentFactory
    {
        public AIAgent CreateSystemAgent() => throw CreateFailure();

        public Task<AIAgent> CreateTaskAgentAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<AIAgent>(CreateFailure());

        public AIAgent CreateResearchAgent() => throw CreateFailure();

        public AIAgent CreateCommunicationAgent() => throw CreateFailure();

        public AIAgent CreateCoordinatorAgent() => throw CreateFailure();

        public IAgentBuilder CreateCustomAgent() => throw CreateFailure();

        private static InvalidOperationException CreateFailure() =>
            new("legacy tools unavailable");
    }

    private sealed class NoOpAgentRegistry : IAgentRegistry
    {
        public AIAgent GetAgent(string name) =>
            throw new KeyNotFoundException(name);

        public IEnumerable<string> GetAllAgentNames() => [];

        public void RegisterAgent(string name, AIAgent agent)
        {
        }

        public bool IsAgentAvailable(string name) => false;
    }
}

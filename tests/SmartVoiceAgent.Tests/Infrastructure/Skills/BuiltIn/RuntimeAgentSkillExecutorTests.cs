using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn.AgentTools;

namespace SmartVoiceAgent.Tests.Infrastructure.Skills.BuiltIn;

public sealed class RuntimeAgentSkillExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_CreatesRuntimeAgentFromSkillArguments()
    {
        var factory = new CapturingRuntimeAgentFactory("done");
        var executor = new RuntimeAgentSkillExecutor(factory);

        var result = await executor.ExecuteAsync(
            SkillPlan.FromObject(
                RuntimeAgentSkillExecutor.SkillId,
                new
                {
                    task = "Review this repository state.",
                    role = "coding",
                    agentName = "RepoAgent"
                }));

        result.Success.Should().BeTrue();
        result.Message.Should().Be("done");
        result.Data.Should().BeOfType<RuntimeAgentResult>();
        factory.LastRequest.Should().Be(new RuntimeAgentRequest(
            "RepoAgent",
            "coding",
            "Review this repository state."));
    }

    [Fact]
    public async Task ExecuteAsync_RequiresTaskArgument()
    {
        var executor = new RuntimeAgentSkillExecutor(new CapturingRuntimeAgentFactory("unused"));

        var result = await executor.ExecuteAsync(
            SkillPlan.FromObject(
                RuntimeAgentSkillExecutor.SkillId,
                new { role = "coding" }));

        result.Success.Should().BeFalse();
        result.Status.Should().Be(SkillExecutionStatus.ValidationFailed);
    }

    private sealed class CapturingRuntimeAgentFactory : IRuntimeAgentFactory
    {
        private readonly string _response;

        public CapturingRuntimeAgentFactory(string response)
        {
            _response = response;
        }

        public RuntimeAgentRequest? LastRequest { get; private set; }

        public Task<RuntimeAgentResult> RunAsync(
            RuntimeAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new RuntimeAgentResult(
                request.AgentName,
                request.Role,
                _response,
                "test-model"));
        }
    }
}

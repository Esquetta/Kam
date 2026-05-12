using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Agent.Tools;
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

    [Fact]
    public async Task ExecuteAsync_WhenCodingRoleIsRequested_AttachesReadOnlyWorkspaceContext()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "Program.cs"), "Console.WriteLine(\"hi\");");
            var factory = new CapturingRuntimeAgentFactory("done");
            var executor = new RuntimeAgentSkillExecutor(factory, new FileAgentTools(workspace));

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Inspect this repository.",
                        role = "coding"
                    }));

            result.Success.Should().BeTrue();
            factory.LastRequest.Should().NotBeNull();
            factory.LastRequest!.ToolObservations.Should().ContainSingle();
            factory.LastRequest.ToolObservations![0].SkillId.Should().Be("workspace.map");
            factory.LastRequest.ToolObservations[0].Summary.Should().Contain("Workspace Map:");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenTaskMentionsFileAndSearch_AttachesReadAndSearchObservations()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace, "Program.cs"),
                """
                public static class Program
                {
                    public static void Main() => Console.WriteLine("NeedleValue");
                }
                """);
            var factory = new CapturingRuntimeAgentFactory("done");
            var executor = new RuntimeAgentSkillExecutor(factory, new FileAgentTools(workspace));

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Review Program.cs and search \"NeedleValue\"",
                        role = "coding"
                    }));

            result.Success.Should().BeTrue();
            factory.LastRequest.Should().NotBeNull();
            factory.LastRequest!.ToolObservations.Should().NotBeNull();
            factory.LastRequest.ToolObservations!.Select(observation => observation.SkillId)
                .Should()
                .Contain(["workspace.map", "file.read_lines", "workspace.search_text"]);
            factory.LastRequest.ToolObservations!
                .Single(observation => observation.SkillId == "file.read_lines")
                .Summary.Should().Contain("Program.cs");
            factory.LastRequest.ToolObservations!
                .Single(observation => observation.SkillId == "workspace.search_text")
                .Summary.Should().Contain("NeedleValue");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenTaskMentionsDiff_AttachesGitDiffSummaryObservation()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var factory = new CapturingRuntimeAgentFactory("done");
            var executor = new RuntimeAgentSkillExecutor(factory, new FileAgentTools(workspace));

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Review current diff before commit.",
                        role = "coding"
                    }));

            result.Success.Should().BeTrue();
            factory.LastRequest.Should().NotBeNull();
            factory.LastRequest!.ToolObservations.Should().Contain(observation =>
                observation.SkillId == "git.diff_summary"
                && observation.Summary.Contains("Git Snapshot:", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenTaskIsNotWorkspaceRelated_DoesNotAttachReadOnlyContext()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var factory = new CapturingRuntimeAgentFactory("done");
            var executor = new RuntimeAgentSkillExecutor(factory, new FileAgentTools(workspace));

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Write a short greeting.",
                        role = "general"
                    }));

            result.Success.Should().BeTrue();
            factory.LastRequest.Should().NotBeNull();
            factory.LastRequest!.ToolObservations.Should().BeNull();
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
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

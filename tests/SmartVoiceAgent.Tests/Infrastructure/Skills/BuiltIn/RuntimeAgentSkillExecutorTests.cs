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

    [Fact]
    public async Task ExecuteAsync_WhenAgentProposesFilePatch_QueuesPreviewForApprovalWithoutWriting()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var filePath = Path.Combine(workspace, "Program.cs");
            await File.WriteAllTextAsync(filePath, "Console.WriteLine(\"old\");");
            var confirmation = new RecordingSkillConfirmationService();
            var factory = new CapturingRuntimeAgentFactory(
                new RuntimeAgentResult(
                    "CodingAgent",
                    "coding",
                    "Patch proposed.",
                    "test-model",
                    ActionRequests:
                    [
                        new RuntimeAgentActionRequest(
                            "file.patch",
                            FilePath: "Program.cs",
                            OldText: "old",
                            NewText: "new",
                            ExpectedOccurrences: 1)
                    ]));
            var executor = new RuntimeAgentSkillExecutor(
                factory,
                new FileAgentTools(workspace),
                confirmation);

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Patch Program.cs",
                        role = "coding"
                    }));

            result.Success.Should().BeTrue();
            result.Message.Should().Contain("Approval required: 1 action");
            confirmation.QueueCount.Should().Be(1);
            confirmation.LastRequest.Should().NotBeNull();
            confirmation.LastRequest!.SkillId.Should().Be("file.patch");
            confirmation.LastRequest.Plan.RequiresConfirmation.Should().BeTrue();
            confirmation.LastRequest.Preview.Should().Contain("Preview only:");
            confirmation.LastRequest.Preview.Should().Contain("-Console.WriteLine(\"old\");");
            confirmation.LastRequest.Preview.Should().Contain("+Console.WriteLine(\"new\");");
            (await File.ReadAllTextAsync(filePath)).Should().Be("Console.WriteLine(\"old\");");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentProposesTestRun_QueuesShellCommandForApproval()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var confirmation = new RecordingSkillConfirmationService();
            var factory = new CapturingRuntimeAgentFactory(
                new RuntimeAgentResult(
                    "CodingAgent",
                    "coding",
                    "Tests proposed.",
                    "test-model",
                    ActionRequests:
                    [
                        new RuntimeAgentActionRequest(
                            "tests.run",
                            Command: "dotnet test tests/SmartVoiceAgent.Tests/SmartVoiceAgent.Tests.csproj")
                    ]));
            var executor = new RuntimeAgentSkillExecutor(
                factory,
                new FileAgentTools(workspace),
                confirmation);

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Run tests",
                        role = "coding"
                    }));

            result.Success.Should().BeTrue();
            confirmation.QueueCount.Should().Be(1);
            confirmation.LastRequest.Should().NotBeNull();
            confirmation.LastRequest!.SkillId.Should().Be("shell.run");
            confirmation.LastRequest.Plan.RequiresConfirmation.Should().BeTrue();
            confirmation.LastRequest.Preview.Should().Contain("Test command preview:");
            confirmation.LastRequest.Preview.Should().Contain("dotnet test tests/SmartVoiceAgent.Tests/SmartVoiceAgent.Tests.csproj");
            confirmation.LastRequest.Plan.Arguments["workingDirectory"].GetString().Should().Be(workspace);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenAgentPatchPreviewFails_DoesNotQueueApproval()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"kam-agent-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(workspace, "Program.cs"), "Console.WriteLine(\"old\");");
            var confirmation = new RecordingSkillConfirmationService();
            var factory = new CapturingRuntimeAgentFactory(
                new RuntimeAgentResult(
                    "CodingAgent",
                    "coding",
                    "Patch proposed.",
                    "test-model",
                    ActionRequests:
                    [
                        new RuntimeAgentActionRequest(
                            "file.patch",
                            FilePath: "Program.cs",
                            OldText: "missing",
                            NewText: "new",
                            ExpectedOccurrences: 1)
                    ]));
            var executor = new RuntimeAgentSkillExecutor(
                factory,
                new FileAgentTools(workspace),
                confirmation);

            var result = await executor.ExecuteAsync(
                SkillPlan.FromObject(
                    RuntimeAgentSkillExecutor.SkillId,
                    new
                    {
                        task = "Patch Program.cs",
                        role = "coding"
                    }));

            result.Success.Should().BeFalse();
            result.ErrorCode.Should().Be("agent_action_request_invalid");
            confirmation.QueueCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    private sealed class CapturingRuntimeAgentFactory : IRuntimeAgentFactory
    {
        private readonly RuntimeAgentResult? _result;
        private readonly string _response;

        public CapturingRuntimeAgentFactory(string response)
        {
            _response = response;
        }

        public CapturingRuntimeAgentFactory(RuntimeAgentResult result)
        {
            _result = result;
            _response = result.Response;
        }

        public RuntimeAgentRequest? LastRequest { get; private set; }

        public Task<RuntimeAgentResult> RunAsync(
            RuntimeAgentRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (_result is not null)
            {
                return Task.FromResult(_result);
            }

            return Task.FromResult(new RuntimeAgentResult(
                request.AgentName,
                request.Role,
                _response,
                "test-model"));
        }
    }

    private sealed class RecordingSkillConfirmationService : ISkillConfirmationService
    {
        public event EventHandler? PendingChanged;

        public int QueueCount { get; private set; }

        public SkillConfirmationRequest? LastRequest { get; private set; }

        public IReadOnlyCollection<SkillConfirmationRequest> GetPending() =>
            LastRequest is null ? [] : [LastRequest];

        public SkillConfirmationRequest Queue(
            string userCommand,
            SkillPlan plan,
            string? reason = null,
            string? preview = null)
        {
            QueueCount++;
            LastRequest = new SkillConfirmationRequest
            {
                Id = Guid.NewGuid(),
                UserCommand = userCommand,
                Plan = plan,
                CreatedAt = DateTimeOffset.UtcNow,
                Reason = reason ?? string.Empty,
                Preview = preview ?? string.Empty
            };
            PendingChanged?.Invoke(this, EventArgs.Empty);
            return LastRequest;
        }

        public Task<SkillResult> ApproveAsync(
            Guid requestId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SkillResult.Failed(
                "Not implemented by test stub.",
                SkillExecutionStatus.Failed,
                "test_stub"));
        }

        public bool Reject(Guid requestId) => false;
    }
}

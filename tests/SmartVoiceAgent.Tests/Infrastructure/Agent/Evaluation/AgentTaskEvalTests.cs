using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Evaluation;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Evaluation;

public sealed class AgentTaskEvalTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "kam-agent-task-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    [Fact]
    public void Sandbox_SeedsFilesAndDeletesThemAfterwards()
    {
        var sandbox = new AgentTaskSandbox(_base);

        var root = sandbox.Begin(new Dictionary<string, string> { ["notes/a.txt"] = "alpha" });

        File.ReadAllText(Path.Combine(root, "notes", "a.txt")).Should().Be("alpha");
        sandbox.Root.Should().Be(root);
        sandbox.End();
        Directory.Exists(root).Should().BeFalse();
        sandbox.Root.Should().BeNull();
    }

    [Fact]
    public void Sandbox_RefusesSeedPathsOutsideTheWorkspace()
    {
        var sandbox = new AgentTaskSandbox(_base);

        var act = () => sandbox.Begin(new Dictionary<string, string> { ["../escape.txt"] = "x" });

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Gate_AllowsFileToolsInsideTheWorkspaceOnly()
    {
        var sandbox = new AgentTaskSandbox(_base);
        var root = sandbox.Begin(new Dictionary<string, string>());
        var inside = Json(Path.Combine(root, "a.txt"));
        var outside = Json(Path.Combine(_base, "other", "a.txt"));

        sandbox.Evaluate(Tool("files_write"), inside).Should().Be(ToolPermissionDecision.Allow);
        sandbox.Evaluate(Tool("files_write"), outside).Should().Be(ToolPermissionDecision.Deny);
        sandbox.Evaluate(Tool("files_write"), """{"filePath":"a.txt"}""").Should().Be(ToolPermissionDecision.Deny);
        sandbox.Evaluate(Tool("shell_run"), """{"command":"dir"}""").Should().Be(ToolPermissionDecision.Deny);
        sandbox.Evaluate(Tool("files_open"), inside).Should().Be(ToolPermissionDecision.Deny);
        sandbox.BlockedCalls.Should().Be(4);
    }

    [Theory]
    [InlineData("openAfterWrite", true, false)]
    [InlineData("openAfterWrite", false, true)]
    [InlineData("showInFolder", true, false)]
    public void Gate_RefusesCallsThatOpenAWindow(string flag, bool value, bool allowed)
    {
        var root = Path.Combine(_base, "ws");
        var arguments = $$"""{"filePath":{{System.Text.Json.JsonSerializer.Serialize(Path.Combine(root, "a.txt"))}},"{{flag}}":{{value.ToString().ToLowerInvariant()}}}""";

        AgentTaskSandbox.PathsStayInside(root, arguments).Should().Be(allowed);
    }

    [Fact]
    public void Checks_ReadTheWorkspaceAndTheReply()
    {
        var root = Path.Combine(_base, "checks");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "total.txt"), "Total: 1250\r\n");

        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.Exists, "total.txt"), string.Empty).Should().BeTrue();
        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.Missing, "total.txt"), string.Empty).Should().BeFalse();
        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.Contains, "total.txt", "TOTAL: 1250"), string.Empty).Should().BeTrue();
        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.NotContains, "total.txt", "1255"), string.Empty).Should().BeTrue();
        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.Equals, "total.txt", "total: 1250"), string.Empty).Should().BeTrue();
        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.Contains, "missing.txt", "x"), string.Empty).Should().BeFalse();
        AgentTaskEvaluator.Passes(root, Check(AgentTaskCheckType.AnswerContains, text: "march.txt"), "It is March.txt").Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_ScoresTheWorkspaceAndCountsCalls()
    {
        var sandbox = new AgentTaskSandbox(_base);
        var task = new AgentTaskCase
        {
            Id = "write",
            Language = "en",
            Request = "Write 5 to n.txt",
            Checks = [Check(AgentTaskCheckType.Equals, "n.txt", "5")]
        };
        var runtime = new ScriptedRuntime(sandbox, writeFile: true);

        var attempt = await AgentTaskEvaluator.RunAsync(runtime, sandbox, "m", task, TimeSpan.FromSeconds(10));

        attempt.Passed.Should().BeTrue();
        attempt.ToolCalls.Should().Be(2);
        attempt.FailedToolCalls.Should().Be(1);
        attempt.BlockedToolCalls.Should().Be(1);
        attempt.InputTokens.Should().Be(100);
        attempt.Tools.Should().Equal("files_write", "shell_run");
        attempt.Blocked.Should().Equal("shell_run {}");
        sandbox.Root.Should().BeNull();
    }

    [Theory]
    [InlineData("en", "I wrote the total to total.txt.", true)]
    [InlineData("en", "Toplamı total.txt dosyasına yazdım.", false)]
    [InlineData("tr", "Toplamı total.txt dosyasına yazdım.", true)]
    [InlineData("tr", "I wrote the total.", false)]
    [InlineData("en", "", false)]
    public void ReplyLanguage_IsJudgedByTurkishLetters(string language, string reply, bool matches)
    {
        AgentTaskEvaluator.ReplyMatchesLanguage(language, reply).Should().Be(matches);
    }

    [Fact]
    public async Task RunAsync_FailsWhenTheWorkIsNotDone()
    {
        var sandbox = new AgentTaskSandbox(_base);
        var task = new AgentTaskCase
        {
            Id = "write",
            Language = "en",
            Request = "Write 5 to n.txt",
            Checks = [Check(AgentTaskCheckType.Equals, "n.txt", "5")]
        };

        var attempt = await AgentTaskEvaluator.RunAsync(new ScriptedRuntime(sandbox, writeFile: false), sandbox, "m", task, TimeSpan.FromSeconds(10));

        attempt.Passed.Should().BeFalse();
        attempt.FailedChecks.Should().ContainSingle().Which.Should().Contain("n.txt");
    }

    [Fact]
    public void TasksFile_IsPairedAndNotDoneBeforeTheAgentRuns()
    {
        var tasks = AgentTaskCase.ParseAll(File.ReadAllText(FindFile("evals", "agent-tasks", "tasks.json")));
        var root = Path.Combine(_base, "seed");

        tasks.GroupBy(task => task.Id)
            .Should().OnlyContain(group => group.Select(task => task.Language).Order().SequenceEqual(new[] { "en", "tr" }));

        foreach (var task in tasks)
        {
            var sandbox = new AgentTaskSandbox(root);
            var workspace = sandbox.Begin(task.Files);
            task.Checks
                .Should().Contain(check => !AgentTaskEvaluator.Passes(workspace, check, string.Empty),
                    $"task {task.Id}.{task.Language} must not pass before the agent works");
            sandbox.End();
        }
    }

    private static AgentToolDescriptor Tool(string name)
    {
        return new AgentToolDescriptor(AIFunctionFactory.Create(() => "ok", name), ToolRisk.Write, "builtin", name);
    }

    private static string Json(string path)
    {
        return $$"""{"filePath":{{System.Text.Json.JsonSerializer.Serialize(path)}}}""";
    }

    private static AgentTaskCheck Check(AgentTaskCheckType type, string path = "", string text = "")
    {
        return new AgentTaskCheck { Type = type, Path = path, Text = text };
    }

    private static string FindFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(string.Join('/', parts));
    }

    /// <summary>
    /// Writes n.txt like an agent would, then tries a blocked call, and reports one failed call.
    /// </summary>
    private sealed class ScriptedRuntime : IAgentRuntime
    {
        private readonly AgentTaskSandbox _sandbox;
        private readonly bool _writeFile;

        public ScriptedRuntime(AgentTaskSandbox sandbox, bool writeFile)
        {
            _sandbox = sandbox;
            _writeFile = writeFile;
        }

        public async IAsyncEnumerable<AgentEvent> RunTurnAsync(
            string sessionId,
            string userMessage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new AgentToolCallStarted(sessionId, "1", "files_write", "Write", "{}", ToolRisk.Write);
            if (_writeFile)
            {
                File.WriteAllText(Path.Combine(_sandbox.Root!, "n.txt"), "5\n");
            }

            yield return new AgentToolCallCompleted(sessionId, "1", "files_write", false, "Error: disk busy");
            yield return new AgentToolCallStarted(sessionId, "2", "shell_run", "Shell", "{}", ToolRisk.Execute);
            _sandbox.Evaluate(Tool("shell_run"), "{}");
            yield return new AgentToolCallCompleted(sessionId, "2", "shell_run", false, "Blocked by a deny rule");
            yield return new AgentTurnCompleted(sessionId, "Done.", 2, 100, 20);
        }

        public bool ResolveApproval(string requestId, bool approved, bool alwaysAllow = false) => false;
    }
}

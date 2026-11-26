using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Infrastructure.Agent.Subagents;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class GraphsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Layout_GroupsOverlappingRunsAndJoinsBetweenGroups()
    {
        var runs = new[]
        {
            Run("a", "s1", Now.AddMinutes(-10), Now.AddMinutes(-6)),
            Run("b", "s1", Now.AddMinutes(-9), Now.AddMinutes(-7)),
            Run("c", "s1", Now.AddMinutes(-5), null)
        };

        var waves = AgentGraphLayout.GroupIntoWaves(runs, Now);
        var graph = AgentGraphLayout.Build(runs, Now);

        waves.Select(wave => string.Join(",", wave.Select(run => run.RunId))).Should().Equal("a,b", "c");
        graph.Nodes.Select(node => node.Id).Should().Equal("root", "a", "b", "join:0", "c", "join:1");
        graph.Edges.Should().HaveCount(6, "root to a and b, a and b to the join, the join to c, c to the last join");
        graph.Edges.Count(edge => edge.StatusKey == "running").Should().Be(2);
        var a = graph.Nodes.Single(node => node.Id == "a").Bounds;
        var b = graph.Nodes.Single(node => node.Id == "b").Bounds;
        a.X.Should().Be(b.X, "parallel subagents share a column");
        b.Y.Should().BeGreaterThan(a.Bottom);
        graph.Nodes.Single(node => node.Id == "c").Bounds.X.Should().BeGreaterThan(a.Right);
        graph.Size.Width.Should().BeGreaterThan(graph.Nodes.Max(node => node.Bounds.Right));
    }

    [Fact]
    public void Refresh_ListsTasksNewestFirstAndShowsTheNewestTasksGraph()
    {
        var tracker = Tracker(
            Run("a", "older", Now.AddHours(-2), Now.AddHours(-2)),
            Run("b", "newer", Now.AddMinutes(-3), null, Step("c1", SubagentStepStatus.Running)),
            Run("c", "newer", Now.AddMinutes(-2), Now.AddMinutes(-1)));

        var viewModel = Create(tracker);

        viewModel.Tasks.Select(task => task.SessionId).Should().Equal("newer", "older");
        viewModel.Tasks[0].IsSelected.Should().BeTrue();
        viewModel.Tasks[0].IsRunning.Should().BeTrue();
        viewModel.Tasks[0].Title.Should().Be("Fix the settings");
        viewModel.SelectedSessionId.Should().Be("newer");
        viewModel.Nodes.Select(node => node.Id).Should().Equal("root", "b", "c", "join:0");
        var running = viewModel.Nodes.Single(node => node.Id == "b");
        running.IsRunning.Should().BeTrue();
        running.Title.Should().Be("Explorer");
        running.CurrentText.Should().Be("Read File");
        viewModel.Nodes[0].IsRunning.Should().BeTrue("the main agent waits while a subagent runs");
        viewModel.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void SelectNode_ShowsTheRunsDetailAndStopCancelsIt()
    {
        var tracker = Tracker(Run("b", "s1", Now.AddMinutes(-3), null, Step("c1", SubagentStepStatus.Succeeded), Step("c2", SubagentStepStatus.AwaitingApproval)));
        var viewModel = Create(tracker);

        viewModel.SelectNodeCommand.Execute(viewModel.Nodes.Single(node => node.Id == "b")).Subscribe();

        viewModel.HasSelectedRun.Should().BeTrue();
        viewModel.SelectedRunType.Should().Be("Explorer");
        viewModel.SelectedRunStatusKey.Should().Be("needs-you");
        viewModel.SelectedRunIsRunning.Should().BeTrue();
        viewModel.SelectedSteps.Select(step => step.StatusKey).Should().Equal("done", "needs-you");
        viewModel.SelectedSteps[0].Target.Should().Be("notes.txt");
        viewModel.Nodes.Single(node => node.Id == "b").IsSelected.Should().BeTrue();

        viewModel.StopRunCommand.Execute().Subscribe();
        tracker.Cancelled.Should().Equal("b");

        viewModel.SelectNodeCommand.Execute(viewModel.Nodes[0]).Subscribe();
        viewModel.HasSelectedRun.Should().BeFalse("the main agent node shows the task overview");
    }

    [Fact]
    public void Focus_SelectsTheTaskAndRunAndFollowsChanges()
    {
        var tracker = Tracker(Run("a", "s1", Now.AddMinutes(-9), Now.AddMinutes(-8)), Run("b", "s2", Now.AddMinutes(-3), null));
        var viewModel = Create(tracker);

        viewModel.Focus("s1", "a");

        viewModel.SelectedSessionId.Should().Be("s1");
        viewModel.SelectedRun!.RunId.Should().Be("a");
        viewModel.SelectedRunReport.Should().Be("report a");

        tracker.Add(Run("d", "s1", Now.AddMinutes(-1), null));
        viewModel.Nodes.Select(node => node.Id).Should().Contain("d", "a change from the tracker refreshes the graph");
    }

    [Fact]
    public void WithoutRuns_ShowsTheEmptyState()
    {
        var viewModel = Create(Tracker());

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.HasGraph.Should().BeFalse();
        viewModel.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task Runs_ListsSubagentRunsWithTheirStepsAndOpensTheirGraph()
    {
        var sessions = new Mock<IAgentSessionStore>();
        sessions.Setup(store => store.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var opened = new List<(string, string)>();
        var tracker = Tracker(Run("b", "s1", DateTimeOffset.Now.AddMinutes(-3), null, Step("c1", SubagentStepStatus.AwaitingApproval)));
        var viewModel = new RunsViewModel(sessions.Object, null, _ => { }, subagents: tracker, openGraph: (session, run) => opened.Add((session, run)));

        await viewModel.LoadAsync();

        var row = viewModel.Rows.Single();
        row.KindText.Should().Be("Subagent");
        row.Name.Should().Be("Job b");
        row.ContextText.Should().Be("Explorer");
        row.IsNeedsYou.Should().BeTrue();
        row.Observations.Should().ContainSingle(item => item.Skill == "Read File");
        row.IsSubagent.Should().BeTrue();
        viewModel.OpenGraphCommand.Execute(row).Subscribe();
        opened.Should().Equal(("s1", "b"));
    }

    private static GraphsViewModel Create(ISubagentRunTracker tracker)
    {
        var sessions = new Mock<IAgentSessionStore>();
        sessions.Setup(store => store.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AgentSessionSummary("newer", "Fix the settings", Now, 3, null)
        ]);
        return new GraphsViewModel(tracker, sessions.Object, _ => { }, post: action => action(), clock: () => Now);
    }

    private static SubagentRun Run(string id, string session, DateTimeOffset started, DateTimeOffset? completed, params SubagentStep[] steps) =>
        new(id, session, "call-" + id, "explorer", "Job " + id, "Brief " + id,
            completed is null ? SubagentRunStatus.Running : SubagentRunStatus.Completed,
            started, completed, Steps: steps, Result: completed is null ? null : "report " + id);

    private static SubagentStep Step(string callId, SubagentStepStatus status) =>
        new(callId, "files_read", "Read File", "{\"path\":\"notes.txt\"}", ToolRisk.Read, status, Now.AddMinutes(-2),
            status is SubagentStepStatus.Running or SubagentStepStatus.AwaitingApproval ? null : Now.AddMinutes(-1), "ok");

    private static FakeTracker Tracker(params SubagentRun[] runs) => new(runs);

    private sealed class FakeTracker(IEnumerable<SubagentRun> runs) : ISubagentRunTracker
    {
        private readonly SubagentRunTracker _inner = Seed(runs);

        public List<string> Cancelled { get; } = [];

        public event EventHandler<SubagentRun>? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public void Add(SubagentRun run) => _inner.Start(run, new CancellationTokenSource());

        public IReadOnlyList<SubagentRun> List(string? parentSessionId = null, int maxCount = 200) => _inner.List(parentSessionId, maxCount);

        public SubagentRun? Get(string runId) => _inner.Get(runId);

        public bool Cancel(string runId)
        {
            Cancelled.Add(runId);
            return true;
        }

        public void Start(SubagentRun run, CancellationTokenSource cancellation) => _inner.Start(run, cancellation);

        public void Update(string runId, Func<SubagentRun, SubagentRun> change) => _inner.Update(runId, change);

        public void Finish(string runId, Func<SubagentRun, SubagentRun> change) => _inner.Finish(runId, change);

        private static SubagentRunTracker Seed(IEnumerable<SubagentRun> runs)
        {
            var tracker = new SubagentRunTracker(path: null);
            foreach (var run in runs.OrderBy(run => run.StartedAt))
            {
                tracker.Start(run, new CancellationTokenSource());
            }

            return tracker;
        }
    }
}

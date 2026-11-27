using Avalonia;
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

    [Fact]
    public void MoveNode_MovesTheNodeAndTheEdgesThatTouchIt()
    {
        var tracker = Tracker(Run("a", "s1", Now.AddMinutes(-9), Now.AddMinutes(-8)), Run("b", "s1", Now.AddMinutes(-9), Now.AddMinutes(-7)));
        var viewModel = Create(tracker);
        var a = viewModel.Nodes.Single(node => node.Id == "a");

        viewModel.MoveNode("a", new Point(a.X + 120, a.Y + 300));

        a.X.Should().Be(a.LayoutX + 120);
        a.Y.Should().Be(a.LayoutY + 300);
        viewModel.Edges.Single(edge => edge.ToId == "a").To.Should().Be(new Point(a.X, a.Y + (a.Height / 2)));
        viewModel.Edges.Single(edge => edge.FromId == "a").From.Should().Be(new Point(a.X + a.Width, a.Y + (a.Height / 2)));
        viewModel.CanvasHeight.Should().BeGreaterThanOrEqualTo(a.Y + a.Height + AgentGraphLayout.Padding, "the canvas grows so a dragged node is never clipped");

        viewModel.MoveNode("a", new Point(-50, -50));
        (a.X, a.Y).Should().Be((0d, 0d), "nodes stay inside the canvas");
    }

    [Fact]
    public void DragNode_ScalesThePointerMovementByTheZoom()
    {
        var viewModel = Create(Tracker(Run("a", "s1", Now.AddMinutes(-9), null)));
        var a = viewModel.Nodes.Single(node => node.Id == "a");
        var origin = new Point(a.X, a.Y);

        viewModel.SetZoom(2);
        viewModel.DragNode("a", origin, new Vector(40, 20));

        (a.X, a.Y).Should().Be((origin.X + 20, origin.Y + 10));
    }

    [Fact]
    public void DraggedOffsets_SurviveRefreshesAndNewRunsPerTask()
    {
        var tracker = Tracker(Run("a", "s1", Now.AddMinutes(-9), Now.AddMinutes(-8)), Run("x", "s2", Now.AddMinutes(-30), Now.AddMinutes(-29)));
        var viewModel = Create(tracker);
        viewModel.Focus("s1", null);
        viewModel.MoveNode("a", new Point(viewModel.Nodes.Single(node => node.Id == "a").LayoutX + 40, 200));
        viewModel.MoveNode("root", new Point(5, 5));

        viewModel.Refresh();
        tracker.Add(Run("d", "s1", Now.AddMinutes(-1), null));

        var a = viewModel.Nodes.Single(node => node.Id == "a");
        (a.X - a.LayoutX).Should().Be(40, "the offset from the layout is kept");
        a.Y.Should().Be(200);
        var root = viewModel.Nodes.Single(node => node.Id == "root");
        (root.X, root.Y).Should().Be((5d, 5d));
        var d = viewModel.Nodes.Single(node => node.Id == "d");
        (d.X, d.Y).Should().Be((d.LayoutX, d.LayoutY), "a new node appears at its layout place");
        viewModel.Edges.Single(edge => edge.ToId == "a").From.Should().Be(new Point(root.X + root.Width, root.Y + (root.Height / 2)));

        viewModel.Focus("s2", null);
        viewModel.Nodes.Should().OnlyContain(node => node.X == node.LayoutX && node.Y == node.LayoutY, "offsets belong to their task");

        viewModel.Focus("s1", null);
        viewModel.Nodes.Single(node => node.Id == "a").Y.Should().Be(200);
    }

    [Fact]
    public void ResetLayout_PutsTheTasksNodesBackAtTheirLayoutPlaces()
    {
        var viewModel = Create(Tracker(Run("a", "s1", Now.AddMinutes(-9), Now.AddMinutes(-8))));
        var layoutEdges = viewModel.Edges.ToList();
        viewModel.MoveNode("a", new Point(600, 400));

        viewModel.ResetLayoutCommand.Execute().Subscribe();

        viewModel.Nodes.Should().OnlyContain(node => node.X == node.LayoutX && node.Y == node.LayoutY);
        viewModel.Edges.Should().Equal(layoutEdges);
        viewModel.Refresh();
        viewModel.Nodes.Single(node => node.Id == "a").X.Should().Be(viewModel.Nodes.Single(node => node.Id == "a").LayoutX);
    }

    [Fact]
    public void Zoom_StaysWithinItsLimitsAndFitUsesTheViewport()
    {
        var viewModel = Create(Tracker(Run("a", "s1", Now.AddMinutes(-9), Now.AddMinutes(-8))));
        viewModel.Zoom.Should().Be(1);
        viewModel.ZoomText.Should().Be("100%");

        for (var step = 0; step < 20; step++)
        {
            viewModel.ZoomInCommand.Execute().Subscribe();
        }

        viewModel.Zoom.Should().Be(GraphsViewModel.MaxZoom);
        viewModel.ZoomText.Should().Be("200%");

        for (var step = 0; step < 30; step++)
        {
            viewModel.ZoomOutCommand.Execute().Subscribe();
        }

        viewModel.Zoom.Should().Be(GraphsViewModel.MinZoom);

        viewModel.SetViewport(new Size(100, 100));
        viewModel.FitCommand.Execute().Subscribe();
        viewModel.Zoom.Should().Be(GraphsViewModel.MinZoom, "fit never zooms out past the limit");

        viewModel.SetViewport(new Size(5000, 5000));
        viewModel.FitCommand.Execute().Subscribe();
        viewModel.Zoom.Should().Be(GraphsViewModel.MaxZoom, "fit never zooms in past the limit");
        viewModel.CanvasWidth.Should().BeApproximately((5000 - 2) / GraphsViewModel.MaxZoom, 0.001, "the canvas fills the viewport");

        var contentWidth = viewModel.Nodes.Max(node => node.X + node.Width) + AgentGraphLayout.Padding;
        var contentHeight = viewModel.Nodes.Max(node => node.Y + node.Height) + AgentGraphLayout.Padding;
        viewModel.SetViewport(new Size(contentWidth * 0.8, 2000));
        viewModel.FitCommand.Execute().Subscribe();
        viewModel.Zoom.Should().BeLessThan(0.8).And.BeGreaterThan(0.75);
        (contentWidth * viewModel.Zoom).Should().BeLessThanOrEqualTo(contentWidth * 0.8);
        (contentHeight * viewModel.Zoom).Should().BeLessThanOrEqualTo(2000);
    }

    [Fact]
    public void NewGraph_ShrinksToFitASmallViewportUntilTheUserZooms()
    {
        var viewModel = Create(Tracker(Run("a", "s1", Now.AddMinutes(-9), Now.AddMinutes(-8)), Run("b", "s1", Now.AddMinutes(-9), null)));
        var contentHeight = viewModel.Nodes.Max(node => node.Y + node.Height) + AgentGraphLayout.Padding;

        viewModel.SetViewport(new Size(5000, 5000));
        viewModel.Zoom.Should().Be(1, "a graph that fits is never zoomed in");

        viewModel.SetViewport(new Size(5000, contentHeight / 2));
        viewModel.Zoom.Should().BeLessThanOrEqualTo(0.5).And.BeGreaterThanOrEqualTo(GraphsViewModel.MinZoom);

        viewModel.ZoomInCommand.Execute().Subscribe();
        var chosen = viewModel.Zoom;
        viewModel.SetViewport(new Size(5000, contentHeight / 3));
        viewModel.Zoom.Should().Be(chosen, "the user's zoom wins");
    }

    [Fact]
    public void ZoomedOffset_KeepsThePointUnderThePointer()
    {
        var offset = GraphsViewModel.ZoomedOffset(new Vector(100, 50), new Point(200, 100), 1, 2);

        offset.Should().Be(new Vector(400, 200), "canvas point (300, 150) stays under the pointer at (200, 100)");
        GraphsViewModel.ZoomedOffset(new Vector(0, 0), new Point(200, 100), 1, 0.5).Should().Be(new Vector(0, 0), "offsets never go negative");
    }

    [Fact]
    public void PointerGesture_IsAClickUntilThePointerMovesPastTheThreshold()
    {
        var viewModel = Create(Tracker(Run("b", "s1", Now.AddMinutes(-3), null)));
        var b = viewModel.Nodes.Single(node => node.Id == "b");
        var origin = new Point(b.X, b.Y);
        var gesture = new GraphPointerGesture(new Point(10, 10));

        gesture.Move(new Point(12, 11)).Should().BeFalse();
        gesture.IsClick.Should().BeTrue("a press that moved under 4 px still selects the node");
        if (gesture.IsClick)
        {
            viewModel.SelectNodeCommand.Execute(b).Subscribe();
        }

        viewModel.HasSelectedRun.Should().BeTrue();
        (b.X, b.Y).Should().Be((origin.X, origin.Y), "a click does not move the node");

        gesture.Move(new Point(30, 10)).Should().BeTrue();
        gesture.IsClick.Should().BeFalse();
        gesture.Delta.Should().Be(new Vector(20, 0));
        gesture.Move(new Point(11, 10)).Should().BeTrue("once dragging, the gesture stays a drag");
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

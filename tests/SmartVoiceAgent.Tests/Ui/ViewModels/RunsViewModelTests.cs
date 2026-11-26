using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class RunsViewModelTests
{
    private readonly Mock<IAgentSessionStore> _sessions = new();
    private readonly Mock<IRuntimeAgentRunStore> _runs = new();
    private readonly List<string> _opened = [];
    private readonly DateTimeOffset _now = DateTimeOffset.Now;

    public RunsViewModelTests()
    {
        _sessions.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new AgentSessionSummary("s1", "Fix the build", _now.AddMinutes(-5), 12, @"D:\Work\Kam", "gpt-x"),
            new AgentSessionSummary("s2", "Old chat", _now.AddDays(-3), 4, null),
            new AgentSessionSummary("s3", "Waiting chat", _now.AddMinutes(-20), 2, null)
        ]);
        _runs.Setup(r => r.List(It.IsAny<int>())).Returns(
        [
            new RuntimeAgentRun("r1", "Reviewer", "review", "Review the diff", "model-a", RuntimeAgentRunStatus.Failed, _now.AddHours(-1), _now.AddHours(-1), ErrorMessage: "boom"),
            new RuntimeAgentRun("r2", "Planner", "plan", "Plan the work", "model-b", RuntimeAgentRunStatus.Succeeded, _now.AddMinutes(-30), _now.AddMinutes(-29),
                Response: "All planned", ToolObservations: [new RuntimeAgentToolObservation("files.read", "read 3 files", true)]),
            new RuntimeAgentRun("r3", "Reviewer", "review", "Old review", "model-a", RuntimeAgentRunStatus.Failed, _now.AddDays(-10), _now.AddDays(-10)),
            new RuntimeAgentRun("r4", "Planner", "plan", "Stopped", "model-b", RuntimeAgentRunStatus.Canceled, _now.AddDays(-2), _now.AddDays(-2))
        ]);
    }

    private async Task<RunsViewModel> CreateAsync(Func<string, string?>? liveStatus = null)
    {
        var viewModel = new RunsViewModel(_sessions.Object, _runs.Object, _opened.Add, liveStatus);
        await viewModel.LoadAsync();
        return viewModel;
    }

    [Fact]
    public async Task LoadAsync_ListsTasksAndAgentRunsNewestFirst()
    {
        var viewModel = await CreateAsync();

        viewModel.Rows.Select(row => row.Id).Should().Equal("s1", "s3", "r2", "r1", "r4", "s2", "r3");
    }

    [Fact]
    public async Task Rows_MapKindModelContextCountAndStatus()
    {
        var viewModel = await CreateAsync(id => id switch { "s1" => "running", "s3" => "needs-you", _ => null });

        var task = viewModel.Rows.Single(row => row.Id == "s1");
        task.IsTask.Should().BeTrue();
        task.ModelText.Should().Be("gpt-x");
        task.ContextText.Should().Be("Kam");
        task.ContextTooltip.Should().Be(@"D:\Work\Kam");
        task.Count.Should().Be(12);
        task.IsRunning.Should().BeTrue();

        var noModel = viewModel.Rows.Single(row => row.Id == "s2");
        noModel.ModelText.Should().Be("Default");
        noModel.ContextText.Should().Be("—");
        noModel.IsDone.Should().BeTrue();
        viewModel.Rows.Single(row => row.Id == "s3").IsNeedsYou.Should().BeTrue();

        var run = viewModel.Rows.Single(row => row.Id == "r2");
        run.IsTask.Should().BeFalse();
        run.ContextText.Should().Be("plan");
        run.Count.Should().Be(1);
        run.IsDone.Should().BeTrue();
        viewModel.Rows.Single(row => row.Id == "r1").IsFailed.Should().BeTrue();
        viewModel.Rows.Single(row => row.Id == "r4").IsCancelled.Should().BeTrue();
    }

    [Fact]
    public async Task Filter_ShowsOnlyTheChosenKind()
    {
        var viewModel = await CreateAsync();

        viewModel.SetFilterCommand.Execute("Tasks").Subscribe();
        viewModel.Rows.Should().OnlyContain(row => row.IsTask).And.HaveCount(3);

        viewModel.SetFilterCommand.Execute("AgentRuns").Subscribe();
        viewModel.Rows.Should().OnlyContain(row => !row.IsTask).And.HaveCount(4);
        viewModel.IsFilterAgentRuns.Should().BeTrue();
    }

    [Fact]
    public async Task Search_MatchesNameModelAndRole()
    {
        var viewModel = await CreateAsync();

        viewModel.SearchText = "review";
        viewModel.Rows.Select(row => row.Id).Should().BeEquivalentTo(["r1", "r3"]);

        viewModel.SearchText = "gpt-x";
        viewModel.Rows.Select(row => row.Id).Should().Equal("s1");
    }

    [Fact]
    public async Task ActivateRow_OpensATaskAndTogglesAnAgentRun()
    {
        var viewModel = await CreateAsync();

        viewModel.ActivateRow(viewModel.Rows.Single(row => row.Id == "s1"));
        _opened.Should().Equal("s1");

        var run = viewModel.Rows.Single(row => row.Id == "r2");
        viewModel.ActivateRow(run);
        run.IsExpanded.Should().BeTrue();
        run.TaskText.Should().Be("Plan the work");
        run.ResponseText.Should().Be("All planned");
        run.Observations.Should().ContainSingle().Which.Summary.Should().Be("read 3 files");
        viewModel.ActivateRow(run);
        run.IsExpanded.Should().BeFalse();
        _opened.Should().HaveCount(1);
    }

    [Fact]
    public async Task EmptyStores_ShowTheEmptyState()
    {
        _sessions.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _runs.Setup(r => r.List(It.IsAny<int>())).Returns([]);

        var viewModel = await CreateAsync();

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.HasNoHistory.Should().BeTrue();
        viewModel.EmptyTitle.Should().Be("No runs yet");
    }

    [Fact]
    public async Task NoMatch_ExplainsThatTheFilterHidesEverything()
    {
        var viewModel = await CreateAsync();

        viewModel.SearchText = "zzz-nothing";

        viewModel.IsEmpty.Should().BeTrue();
        viewModel.HasNoHistory.Should().BeFalse();
        viewModel.EmptyTitle.Should().Be("No runs match");
    }

    [Fact]
    public async Task LoadAsync_WithMissingStores_ShowsNothing()
    {
        var viewModel = new RunsViewModel(null, null, _opened.Add);
        await viewModel.LoadAsync();

        viewModel.Rows.Should().BeEmpty();
        viewModel.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_WhenAStoreThrows_StillShowsTheOtherOne()
    {
        _sessions.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("disk"));

        var viewModel = await CreateAsync();

        viewModel.Rows.Should().OnlyContain(row => !row.IsTask).And.HaveCount(4);
    }

    [Fact]
    public async Task SubagentRuns_SitUnderTheirTaskInsteadOfAmongTheOtherRows()
    {
        var viewModel = await CreateWithSubagentsAsync();

        viewModel.Rows.Select(row => row.Id).Should().Equal("s1", "sa", "sb", "so", "s3", "r2", "r1", "r4", "s2", "r3");
        viewModel.Rows.Single(row => row.Id == "s1").HasChildren.Should().BeTrue();
        viewModel.Rows.Where(row => row.IsNested).Select(row => row.Id).Should().Equal("sa", "sb");
        viewModel.Rows.Single(row => row.Id == "so").IsNested.Should().BeFalse("its task is gone, so it stands on its own");
        viewModel.Rows.Single(row => row.Id == "s3").HasChildren.Should().BeFalse();
    }

    [Fact]
    public async Task ToggleChildren_FoldsAndUnfoldsATasksSubagentRuns()
    {
        var viewModel = await CreateWithSubagentsAsync();
        var task = viewModel.Rows.Single(row => row.Id == "s1");

        viewModel.ToggleChildrenCommand.Execute(task).Subscribe();
        task.IsChildrenExpanded.Should().BeFalse();
        task.HasChildren.Should().BeTrue("the chevron stays to unfold them again");
        viewModel.Rows.Select(row => row.Id).Should().NotContain(["sa", "sb"]);

        await viewModel.LoadAsync();
        viewModel.Rows.Select(row => row.Id).Should().NotContain("sa", "a reload keeps the task folded");

        viewModel.ToggleChildren(viewModel.Rows.Single(row => row.Id == "s1"));
        viewModel.Rows.Select(row => row.Id).Should().ContainInConsecutiveOrder("s1", "sa", "sb");
    }

    [Fact]
    public async Task Filter_NestsSubagentRunsUnderTasksAndListsThemFlatAsAgentRuns()
    {
        var viewModel = await CreateWithSubagentsAsync();

        viewModel.SetFilterCommand.Execute("Tasks").Subscribe();
        viewModel.Rows.Select(row => row.Id).Should().Equal("s1", "sa", "sb", "s3", "s2");

        viewModel.SetFilterCommand.Execute("AgentRuns").Subscribe();
        viewModel.Rows.Select(row => row.Id).Should().Equal("sa", "so", "r2", "r1", "sb", "r4", "r3");
        viewModel.Rows.Should().OnlyContain(row => !row.IsNested && !row.HasChildren);
    }

    [Fact]
    public async Task Search_ShowsATaskWithTheSubagentRunsThatMatch()
    {
        var viewModel = await CreateWithSubagentsAsync();

        viewModel.SearchText = "logs";

        viewModel.Rows.Select(row => row.Id).Should().Equal("s1", "sb");
        viewModel.Rows.Single(row => row.Id == "sb").IsNested.Should().BeTrue();
    }

    private async Task<RunsViewModel> CreateWithSubagentsAsync()
    {
        var tracker = new Mock<ISubagentRunTracker>();
        tracker.Setup(t => t.List(It.IsAny<string?>(), It.IsAny<int>())).Returns(
        [
            new SubagentRun("sa", "s1", "call-a", "explorer", "Read the code", "Brief a", SubagentRunStatus.Completed, _now.AddMinutes(-2)),
            new SubagentRun("sb", "s1", "call-b", "explorer", "Scan the logs", "Brief b", SubagentRunStatus.Failed, _now.AddMinutes(-61)),
            new SubagentRun("so", "gone", "call-o", "explorer", "Orphan job", "Brief o", SubagentRunStatus.Completed, _now.AddMinutes(-10))
        ]);
        var viewModel = new RunsViewModel(_sessions.Object, _runs.Object, _opened.Add, subagents: tracker.Object);
        await viewModel.LoadAsync();
        return viewModel;
    }
}

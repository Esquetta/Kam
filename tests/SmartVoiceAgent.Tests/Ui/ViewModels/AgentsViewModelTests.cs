using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.CliAgents;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class AgentsViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    private static RuntimeAgentRun Run(
        string id,
        string name,
        RuntimeAgentRunStatus status,
        TimeSpan age,
        TimeSpan? duration = null,
        string? response = null,
        string? error = null,
        IReadOnlyList<RuntimeAgentToolObservation>? observations = null) =>
        new(id, name, "researcher", "Find the release notes", "model-x", status, Now - age,
            duration is null ? null : Now - age + duration, null, response, error, observations);

    private static AgentsViewModel Create(
        IRuntimeAgentRunStore? runs,
        ICliAgentDetector? detector,
        Action? openIntegrations = null) =>
        new(runs, detector, openIntegrations ?? (() => { }), action => action(), () => Now);

    private static IRuntimeAgentRunStore Store(params RuntimeAgentRun[] runs)
    {
        var store = new Mock<IRuntimeAgentRunStore>();
        store.Setup(s => s.List(It.IsAny<int>())).Returns(runs);
        return store.Object;
    }

    private static ICliAgentDetector Detector(params CliAgentStatus[] statuses)
    {
        var detector = new Mock<ICliAgentDetector>();
        detector.Setup(d => d.DetectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(statuses);
        return detector.Object;
    }

    [Fact]
    public async Task Refresh_ListsRunsNewestFirstAndSelectsTheNewest()
    {
        var viewModel = Create(
            Store(
                Run("old", "Old Helper", RuntimeAgentRunStatus.Succeeded, TimeSpan.FromHours(3), TimeSpan.FromSeconds(5)),
                Run("new", "Fresh Helper", RuntimeAgentRunStatus.Running, TimeSpan.FromMinutes(2))),
            null);

        await viewModel.RefreshAsync();

        viewModel.RoleAgents.Select(item => item.AgentName).Should().Equal("Fresh Helper", "Old Helper");
        viewModel.HasRoleAgents.Should().BeTrue();
        viewModel.HasNoRoleAgents.Should().BeFalse();
        viewModel.SelectedRole!.AgentName.Should().Be("Fresh Helper");
        viewModel.SelectedCoding.Should().BeNull();
        viewModel.RoleAgents[0].IsSelected.Should().BeTrue();
        viewModel.RoleAgents[0].RelativeTimeText.Should().Be("2m");
    }

    [Fact]
    public async Task Refresh_WithoutRuns_ShowsTheEmptyStateAndSelectsCodex()
    {
        var viewModel = Create(Store(), null);

        await viewModel.RefreshAsync();

        viewModel.HasNoRoleAgents.Should().BeTrue();
        viewModel.SelectedCoding!.Kind.Should().Be(CliAgentKind.Codex);
        viewModel.SelectedRole.Should().BeNull();
    }

    [Fact]
    public async Task RunStatus_MapsToChipFlags()
    {
        var viewModel = Create(
            Store(
                Run("a", "A", RuntimeAgentRunStatus.Running, TimeSpan.FromMinutes(1)),
                Run("b", "B", RuntimeAgentRunStatus.Succeeded, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(1)),
                Run("c", "C", RuntimeAgentRunStatus.Failed, TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(1), error: "boom"),
                Run("d", "D", RuntimeAgentRunStatus.Canceled, TimeSpan.FromMinutes(4), TimeSpan.FromSeconds(1))),
            null);

        await viewModel.RefreshAsync();

        var byName = viewModel.RoleAgents.ToDictionary(item => item.AgentName);
        byName["A"].IsRunning.Should().BeTrue();
        byName["B"].IsSucceeded.Should().BeTrue();
        byName["C"].IsFailed.Should().BeTrue();
        byName["D"].IsRunning.Should().BeFalse();
        byName["D"].IsSucceeded.Should().BeFalse();
        byName["D"].IsFailed.Should().BeFalse();
        byName["D"].StatusText.Should().Be("Cancelled");
    }

    [Fact]
    public async Task RunDetail_ShowsDurationToolResultsAndResponseOrError()
    {
        var observations = new[]
        {
            new RuntimeAgentToolObservation("web.search", "Found   three\npages", true),
            new RuntimeAgentToolObservation("files.read", new string('x', 500), false)
        };
        var viewModel = Create(
            Store(
                Run("ok", "Reader", RuntimeAgentRunStatus.Succeeded, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(75),
                    response: "Done.", observations: observations),
                Run("bad", "Breaker", RuntimeAgentRunStatus.Failed, TimeSpan.FromMinutes(20), TimeSpan.FromSeconds(4),
                    error: "model unreachable")),
            null);

        await viewModel.RefreshAsync();

        var ok = viewModel.RoleAgents.Single(item => item.AgentName == "Reader");
        ok.DurationText.Should().Be("1m 15s");
        ok.Response.Should().Be("Done.");
        ok.HasResponse.Should().BeTrue();
        ok.HasError.Should().BeFalse();
        ok.Observations.Should().HaveCount(2);
        ok.Observations[0].Summary.Should().Be("Found three pages");
        ok.Observations[1].Summary.Length.Should().BeLessThanOrEqualTo(200);
        ok.Observations[1].Failed.Should().BeTrue();
        ok.Task.Should().Be("Find the release notes");
        ok.ModelId.Should().Be("model-x");

        var bad = viewModel.RoleAgents.Single(item => item.AgentName == "Breaker");
        bad.HasError.Should().BeTrue();
        bad.ErrorMessage.Should().Be("model unreachable");
        bad.HasResponse.Should().BeFalse();
        bad.HasObservations.Should().BeFalse();
        bad.DurationText.Should().Be("4s");
    }

    [Fact]
    public async Task RunningRun_HasNoFinishTimeOrDuration()
    {
        var viewModel = Create(Store(Run("r", "Runner", RuntimeAgentRunStatus.Running, TimeSpan.FromSeconds(10))), null);

        await viewModel.RefreshAsync();

        var run = viewModel.RoleAgents.Single();
        run.DurationText.Should().Be("—");
        run.FinishedText.Should().Be("Still running");
        run.HasNoOutput.Should().BeTrue();
    }

    [Theory]
    [InlineData("Research Helper", "RH")]
    [InlineData("scout", "SC")]
    [InlineData("code-reviewer", "CR")]
    [InlineData("", "A")]
    public void Initials_UseTheFirstLettersOfTheName(string name, string expected)
    {
        AgentsRoleRunItem.BuildInitials(name).Should().Be(expected);
    }

    [Fact]
    public async Task CodingAgents_ReflectTheDetector()
    {
        var viewModel = Create(
            Store(),
            Detector(
                new CliAgentStatus(CliAgentKind.Codex, true, "0.161.0", CliAgentSignIn.Subscription),
                new CliAgentStatus(CliAgentKind.ClaudeCode, false, null, CliAgentSignIn.Unknown)));

        await viewModel.RefreshAsync();

        var codex = viewModel.CodingAgents.Single(item => item.Kind == CliAgentKind.Codex);
        codex.IsReady.Should().BeTrue();
        codex.VersionText.Should().Be("v0.161.0");
        codex.StatusText.Should().Be("Ready");
        codex.InstalledText.Should().Be("Found on PATH");
        codex.SandboxText.Should().Contain("read-only").And.Contain("workspace-write");

        var claude = viewModel.CodingAgents.Single(item => item.Kind == CliAgentKind.ClaudeCode);
        claude.IsReady.Should().BeFalse();
        claude.StatusText.Should().Be("Not installed");
        claude.InstalledText.Should().Be("Not found on PATH");
        viewModel.IsChecking.Should().BeFalse();
    }

    [Fact]
    public async Task CodingAgent_SignedOutShowsWarning()
    {
        var viewModel = Create(
            Store(),
            Detector(new CliAgentStatus(CliAgentKind.Codex, true, "1.0.0", CliAgentSignIn.SignedOut)));

        await viewModel.RefreshAsync();

        var codex = viewModel.CodingAgents[0];
        codex.IsSignedOut.Should().BeTrue();
        codex.IsReady.Should().BeFalse();
        codex.StatusText.Should().Be("Signed out");
    }

    [Fact]
    public async Task DetectorFailure_FallsBackToNotInstalled()
    {
        var detector = new Mock<ICliAgentDetector>();
        detector.Setup(d => d.DetectAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("no"));
        var viewModel = Create(Store(), detector.Object);

        await viewModel.RefreshAsync();

        viewModel.CodingAgents[0].StatusText.Should().Be("Not installed");
        viewModel.IsChecking.Should().BeFalse();
    }

    [Fact]
    public void NullServices_ShowCheckingStateWithoutThrowing()
    {
        var viewModel = Create(null, null);

        viewModel.RoleAgents.Should().BeEmpty();
        viewModel.CodingAgents.Should().HaveCount(2);
        viewModel.CodingAgents[0].StatusText.Should().Be("Checking");
    }

    [Fact]
    public async Task Selecting_ChangesTheDetailAndSurvivesARefresh()
    {
        var viewModel = Create(
            Store(
                Run("one", "First", RuntimeAgentRunStatus.Succeeded, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1)),
                Run("two", "Second", RuntimeAgentRunStatus.Succeeded, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1))),
            null);
        await viewModel.RefreshAsync();

        viewModel.RoleAgents[1].SelectCommand.Execute(null);
        viewModel.SelectedRole!.AgentName.Should().Be("Second");

        await viewModel.RefreshAsync();
        viewModel.SelectedRole!.AgentName.Should().Be("Second");
        viewModel.RoleAgents.Count(item => item.IsSelected).Should().Be(1);

        viewModel.CodingAgents[1].SelectCommand.Execute(null);
        viewModel.SelectedCoding!.Kind.Should().Be(CliAgentKind.ClaudeCode);
        viewModel.SelectedRole.Should().BeNull();
    }

    [Fact]
    public void OpenIntegrationsCommand_CallsTheCallback()
    {
        var opened = 0;
        var viewModel = Create(Store(), null, () => opened++);

        viewModel.CodingAgents[0].OpenIntegrationsCommand.Execute(null);
        viewModel.OpenIntegrationsCommand.Execute(null);

        opened.Should().Be(2);
    }

    [Fact]
    public async Task OnNavigatedTo_ReloadsTheRuns()
    {
        var current = new List<RuntimeAgentRun>();
        var store = new Mock<IRuntimeAgentRunStore>();
        store.Setup(s => s.List(It.IsAny<int>())).Returns(() => current.ToArray());
        var viewModel = Create(store.Object, null);
        await viewModel.RefreshAsync();
        viewModel.HasNoRoleAgents.Should().BeTrue();

        current.Add(Run("late", "Late", RuntimeAgentRunStatus.Running, TimeSpan.FromSeconds(5)));
        viewModel.OnNavigatedTo();
        await viewModel.RefreshAsync();

        viewModel.RoleAgents.Should().ContainSingle();
    }
}

using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Execution;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class MainWindowSkillExecutionHistoryFilterTests
{
    [Fact]
    public void SkillExecutionHistoryFilterText_WhenSet_FiltersSearchableExecutionFields()
    {
        var viewModel = CreateViewModelWithHistory();

        viewModel.SkillExecutionHistoryFilterText = "restore";

        viewModel.SkillExecutionHistory.Select(item => item.SkillId).Should().Equal("shell.run");
        viewModel.HasSkillExecutionHistory.Should().BeTrue();
        viewModel.HasSkillExecutionHistoryFilter.Should().BeTrue();
        viewModel.HasSkillExecutionHistoryMatches.Should().BeTrue();
        viewModel.SkillExecutionHistorySummaryText.Should().Contain("1");
    }

    [Fact]
    public void SkillExecutionHistoryStatusFilter_WhenSet_FiltersByStatus()
    {
        var viewModel = CreateViewModelWithHistory();

        viewModel.SkillExecutionHistoryStatusFilter = "Failed";

        viewModel.SkillExecutionHistory.Select(item => item.SkillId).Should().Equal("shell.run");
        viewModel.HasSkillExecutionHistoryFilter.Should().BeTrue();
        viewModel.HasSkillExecutionHistoryMatches.Should().BeTrue();
    }

    [Fact]
    public void SkillExecutionHistoryFilters_WhenNoMatch_KeepPanelVisibleAndExposeEmptyState()
    {
        var viewModel = CreateViewModelWithHistory();

        viewModel.SkillExecutionHistoryFilterText = "missing-term";

        viewModel.SkillExecutionHistory.Should().BeEmpty();
        viewModel.HasSkillExecutionHistory.Should().BeTrue();
        viewModel.HasSkillExecutionHistoryMatches.Should().BeFalse();
        viewModel.HasNoSkillExecutionHistoryMatches.Should().BeTrue();
        viewModel.SkillExecutionHistorySummaryText.Should().Contain("0");
    }

    [Fact]
    public void ClearSkillExecutionHistoryFiltersCommand_WhenFiltersAreActive_ResetsFilters()
    {
        var viewModel = CreateViewModelWithHistory();
        viewModel.SkillExecutionHistoryFilterText = "restore";
        viewModel.SkillExecutionHistoryStatusFilter = "Failed";

        viewModel.ClearSkillExecutionHistoryFiltersCommand.Execute(null);

        viewModel.SkillExecutionHistoryFilterText.Should().BeEmpty();
        viewModel.SkillExecutionHistoryStatusFilter.Should().Be("All");
        viewModel.HasSkillExecutionHistoryFilter.Should().BeFalse();
        viewModel.SkillExecutionHistory.Select(item => item.SkillId)
            .Should()
            .Equal("files.read", "shell.run", "apps.open");
    }

    private static MainWindowViewModel CreateViewModelWithHistory()
    {
        var service = new InMemorySkillExecutionHistoryService();
        service.Record(
            SkillPlan.FromObject("apps.open", new { applicationName = "Spotify" }),
            SkillResult.Succeeded("Opened Spotify."),
            new DateTimeOffset(2026, 4, 27, 12, 0, 0, TimeSpan.Zero));
        service.Record(
            SkillPlan.FromObject("shell.run", new { command = "dotnet restore" }),
            new SkillResult(
                false,
                "Command failed.",
                "NuGet restore failed.",
                new ShellCommandResult
                {
                    Command = "dotnet restore",
                    StdErr = "NuGet restore failed."
                })
            {
                Status = SkillExecutionStatus.Failed,
                ErrorCode = "shell_exit_code"
            },
            new DateTimeOffset(2026, 4, 27, 12, 1, 0, TimeSpan.Zero));
        service.Record(
            SkillPlan.FromObject("files.read", new { filePath = "README.md" }),
            SkillResult.Succeeded("Read README."),
            new DateTimeOffset(2026, 4, 27, 12, 2, 0, TimeSpan.Zero));

        var viewModel = new MainWindowViewModel();
        viewModel.SetSkillExecutionHistoryService(service);
        return viewModel;
    }
}

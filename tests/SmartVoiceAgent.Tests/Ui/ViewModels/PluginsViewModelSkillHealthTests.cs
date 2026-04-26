using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public class PluginsViewModelSkillHealthTests
{
    [Fact]
    public void Constructor_WithSkillHealthReports_MapsReportsToPluginCards()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "files.read",
                DisplayName = "Read File",
                Description = "Reads a local file.",
                Source = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available."
            },
            new SkillHealthReport
            {
                SkillId = "mcp.todoist.add_task",
                DisplayName = "Add Todoist Task",
                Description = "Creates a Todoist task.",
                Source = "mcp.todoist",
                Status = SkillHealthStatus.MissingExecutor,
                Details = "No executor registered for this skill."
            },
            new SkillHealthReport
            {
                SkillId = "local.desktop-navigation",
                DisplayName = "Desktop Navigation",
                Description = "Imported local skill.",
                Source = "local:C:\\skills\\desktop-navigation",
                Status = SkillHealthStatus.ReviewRequired,
                Details = "Skill requires review before it can be enabled."
            }
        ]);

        viewModel.Plugins.Should().HaveCount(3);

        var healthy = viewModel.Plugins[0];
        healthy.Name.Should().Be("READ FILE");
        healthy.SkillId.Should().Be("files.read");
        healthy.Source.Should().Be("builtin");
        healthy.Status.Should().Be("Healthy");
        healthy.HealthDetail.Should().Be("Executor available.");
        healthy.IsActive.Should().BeTrue();

        var missing = viewModel.Plugins[1];
        missing.Name.Should().Be("ADD TODOIST TASK");
        missing.Status.Should().Be("Missing Executor");
        missing.HealthDetail.Should().Contain("No executor");
        missing.IsActive.Should().BeFalse();

        var reviewRequired = viewModel.Plugins[2];
        reviewRequired.Name.Should().Be("DESKTOP NAVIGATION");
        reviewRequired.Status.Should().Be("Review Required");
        reviewRequired.HealthDetail.Should().Contain("requires review");
        reviewRequired.IsActive.Should().BeFalse();
        reviewRequired.CanApproveReview.Should().BeTrue();
        reviewRequired.CanDisable.Should().BeFalse();
        reviewRequired.CanEnable.Should().BeFalse();
        reviewRequired.CanRevokePermissions.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithEvalSummary_ExposesSmokeEvalStatus()
    {
        var viewModel = new PluginsViewModel(
        [
            new SkillHealthReport
            {
                SkillId = "files.read",
                DisplayName = "Read File",
                Source = "builtin",
                Status = SkillHealthStatus.Healthy,
                Details = "Executor available."
            }
        ],
        new SkillEvalSummary
        {
            Total = 3,
            Passed = 2,
            Failed = 1,
            Results =
            [
                new SkillEvalResult
                {
                    Name = "files.exists smoke",
                    SkillId = "files.exists",
                    Passed = false,
                    ExpectedStatus = SkillExecutionStatus.Succeeded,
                    ActualStatus = SkillExecutionStatus.ValidationFailed,
                    Message = "Expected Succeeded, got ValidationFailed."
                }
            ]
        });

        viewModel.SkillEvalStatus.Should().Be("2/3 smoke evals passing");
        viewModel.SkillEvalDetail.Should().Contain("files.exists");
        viewModel.IsSkillEvalHealthy.Should().BeFalse();
    }
}

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
            }
        ]);

        viewModel.Plugins.Should().HaveCount(2);

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
    }
}

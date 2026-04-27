using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public class PendingSkillConfirmationViewModelTests
{
    [Fact]
    public void Constructor_WhenRequestHasPreview_ExposesPreviewState()
    {
        var request = new SkillConfirmationRequest
        {
            Id = Guid.NewGuid(),
            UserCommand = "patch notes",
            Plan = SkillPlan.FromObject("file.patch", new { filePath = "C:\\temp\\notes.txt" }),
            Reason = "Review the diff before applying.",
            Preview = "Diff Preview:\n-old\n+new"
        };

        var viewModel = new PendingSkillConfirmationViewModel(
            request,
            _ => Task.CompletedTask,
            _ => { });

        viewModel.Preview.Should().Be("Diff Preview:\n-old\n+new");
        viewModel.HasPreview.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WhenRequestHasNoPreview_HidesPreviewState()
    {
        var request = new SkillConfirmationRequest
        {
            Id = Guid.NewGuid(),
            UserCommand = "delete notes",
            Plan = SkillPlan.FromObject("files.delete", new { filePath = "C:\\temp\\notes.txt" }),
            Reason = "Requires confirmation."
        };

        var viewModel = new PendingSkillConfirmationViewModel(
            request,
            _ => Task.CompletedTask,
            _ => { });

        viewModel.Preview.Should().BeEmpty();
        viewModel.HasPreview.Should().BeFalse();
    }
}

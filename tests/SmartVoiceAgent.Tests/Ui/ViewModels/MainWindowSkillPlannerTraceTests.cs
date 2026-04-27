using FluentAssertions;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Planning;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class MainWindowSkillPlannerTraceTests
{
    [Fact]
    public void SetSkillPlannerTraceStore_ExposesRecentPlannerTraces()
    {
        var store = new InMemorySkillPlannerTraceStore();
        store.Record(new SkillPlannerTraceEntry
        {
            Timestamp = new DateTimeOffset(2026, 4, 27, 13, 0, 0, TimeSpan.Zero),
            UserRequest = "Spotify ac",
            RawResponse = """{"skillId":"apps.open","arguments":{}}""",
            IsValid = true,
            SkillId = "apps.open",
            Confidence = 0.92,
            DurationMilliseconds = 25
        });
        var viewModel = new MainWindowViewModel();

        viewModel.SetSkillPlannerTraceStore(store);

        viewModel.HasSkillPlannerTraces.Should().BeTrue();
        viewModel.SkillPlannerTraces.Should().ContainSingle();
        viewModel.SkillPlannerTraces[0].SkillIdText.Should().Be("apps.open");
        viewModel.SkillPlannerTraces[0].StatusText.Should().Be("Valid");
        viewModel.SkillPlannerTraces[0].RawResponseText.Should().Contain("apps.open");
        viewModel.SkillPlannerTraces[0].ConfidenceText.Should().Be("confidence 0.92");
    }

    [Fact]
    public void ClearSkillPlannerTraceCommand_WhenTraceStoreIsAttached_ClearsTracePanel()
    {
        var store = new InMemorySkillPlannerTraceStore();
        store.Record(new SkillPlannerTraceEntry
        {
            UserRequest = "Spotify ac",
            RawResponse = "not json",
            IsValid = false,
            ErrorMessage = "Planner response must be a single JSON object."
        });
        var viewModel = new MainWindowViewModel();
        viewModel.SetSkillPlannerTraceStore(store);

        viewModel.ClearSkillPlannerTraceCommand.Execute(null);

        viewModel.HasSkillPlannerTraces.Should().BeFalse();
        viewModel.SkillPlannerTraces.Should().BeEmpty();
    }
}

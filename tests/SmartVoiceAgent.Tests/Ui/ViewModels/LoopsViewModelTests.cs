using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System.Globalization;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class LoopsViewModelTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-loops-viewmodel-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewSettings_ShowTheRuntimeDefaults()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);

        var viewModel = new LoopsViewModel(settings);

        viewModel.MaxIterationsText.Should().Be("24");
        viewModel.MaxToolResultCharactersText.Should().Be(Grouped(16000));
        viewModel.ContextTokenBudgetText.Should().Be(Grouped(64000));
        viewModel.Explanation.Should().Contain("think");
    }

    [Fact]
    public void EditingALimit_SavesItAndSurvivesReload()
    {
        using (var settings = new JsonSettingsService(_settingsDirectory))
        {
            var viewModel = new LoopsViewModel(settings);

            viewModel.MaxIterationsText = "40";
            viewModel.MaxToolResultCharactersText = "30000";
            viewModel.ContextTokenBudgetText = "0";

            settings.AgentMaxIterations.Should().Be(40);
            settings.AgentMaxToolResultCharacters.Should().Be(30000);
            settings.AgentContextTokenBudget.Should().Be(0);
        }

        using var reloaded = new JsonSettingsService(_settingsDirectory);
        reloaded.AgentMaxIterations.Should().Be(40);
        reloaded.AgentMaxToolResultCharacters.Should().Be(30000);
        reloaded.AgentContextTokenBudget.Should().Be(0);
        new LoopsViewModel(reloaded).MaxIterationsText.Should().Be("40");
    }

    [Theory]
    [InlineData("0", "1")]
    [InlineData("-5", "1")]
    [InlineData("999", "200")]
    public void StepLimit_IsClampedAndTheTextShowsTheSavedValue(string typed, string saved)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new LoopsViewModel(settings);

        viewModel.MaxIterationsText = typed;

        viewModel.MaxIterationsText.Should().Be(saved);
        settings.AgentMaxIterations.Should().Be(int.Parse(saved));
    }

    [Theory]
    [InlineData("5", "1000")]
    [InlineData("9999999", "200000")]
    public void ToolResultLimit_IsClamped(string typed, string saved)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new LoopsViewModel(settings);

        viewModel.MaxToolResultCharactersText = typed;

        viewModel.MaxToolResultCharactersText.Should().Be(Grouped(int.Parse(saved)));
        settings.AgentMaxToolResultCharacters.Should().Be(int.Parse(saved));
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("100", "8000")]
    [InlineData("5000000", "1000000")]
    public void ContextBudget_IsOffOrWithinRange(string typed, string saved)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new LoopsViewModel(settings);

        viewModel.ContextTokenBudgetText = typed;

        viewModel.ContextTokenBudgetText.Should().Be(Grouped(int.Parse(saved)));
        settings.AgentContextTokenBudget.Should().Be(int.Parse(saved));
    }

    [Fact]
    public void Limits_ShowAndAcceptThousandsSeparators()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new LoopsViewModel(settings);

        viewModel.MaxToolResultCharactersText = Grouped(30000);

        settings.AgentMaxToolResultCharacters.Should().Be(30000);
        viewModel.MaxToolResultCharactersText.Should().Be(Grouped(30000));
    }

    [Fact]
    public void TextThatIsNotANumber_KeepsTheSavedValue()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new LoopsViewModel(settings);
        viewModel.MaxIterationsText = "30";

        viewModel.MaxIterationsText = "many";

        viewModel.MaxIterationsText.Should().Be("30");
        settings.AgentMaxIterations.Should().Be(30);
    }

    [Fact]
    public void Reset_PutsAllThreeLimitsBackToDefaults()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        var viewModel = new LoopsViewModel(settings);
        viewModel.MaxIterationsText = "99";
        viewModel.MaxToolResultCharactersText = "50000";
        viewModel.ContextTokenBudgetText = "0";

        viewModel.ResetCommand.Execute(null);

        viewModel.MaxIterationsText.Should().Be("24");
        viewModel.MaxToolResultCharactersText.Should().Be(Grouped(16000));
        viewModel.ContextTokenBudgetText.Should().Be(Grouped(64000));
        settings.AgentMaxIterations.Should().Be(24);
        settings.AgentMaxToolResultCharacters.Should().Be(16000);
        settings.AgentContextTokenBudget.Should().Be(64000);
    }

    // The page shows numbers with the current culture's thousands separator, such as 16,000 or 16.000.
    private static string Grouped(int value) => value.ToString("N0", CultureInfo.CurrentCulture);

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }
}

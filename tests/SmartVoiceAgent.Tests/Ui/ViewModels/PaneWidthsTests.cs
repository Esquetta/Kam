using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class PaneWidthsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-pane-widths-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 232)]
    [InlineData(-5, 232)]
    [InlineData(double.NaN, 232)]
    [InlineData(100, 180)]
    [InlineData(250.4, 250)]
    [InlineData(900, 360)]
    public void ClampRail_KeepsTheRailBetweenItsLimits(double requested, double expected) =>
        PaneWidths.ClampRail(requested).Should().Be(expected);

    [Theory]
    [InlineData(0, 290)]
    [InlineData(100, 240)]
    [InlineData(400, 400)]
    [InlineData(2000, 560)]
    public void ClampActivity_KeepsThePanelBetweenItsLimits(double requested, double expected) =>
        PaneWidths.ClampActivity(requested).Should().Be(expected);

    [Fact]
    public void Widths_StartAtTheDefaultsAndAreLimitedWhileDragging()
    {
        using var settings = new JsonSettingsService(_directory);
        var viewModel = new MainWindowViewModel(settings);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.RailWidth.Should().Be(232);
        viewModel.ActivityWidth.Should().Be(290);

        viewModel.RailWidth = 50;
        viewModel.ActivityWidth = 410;

        viewModel.RailWidth.Should().Be(180);
        viewModel.ActivityWidth.Should().Be(410);
        changes.Should().Contain([nameof(MainWindowViewModel.RailWidth), nameof(MainWindowViewModel.ActivityPanelWidth)]);

        changes.Clear();
        viewModel.RailWidth = 20;
        changes.Should().Contain(nameof(MainWindowViewModel.RailWidth), "a handle dragged past the limit snaps back to it");
        settings.RailWidth.Should().Be(232, "nothing is saved until the drag ends");
    }

    [Fact]
    public void SavePaneWidths_KeepsTheWidthsForTheNextStart()
    {
        using (var settings = new JsonSettingsService(_directory))
        {
            var viewModel = new MainWindowViewModel(settings)
            {
                RailWidth = 300,
                ActivityWidth = 420
            };

            viewModel.SavePaneWidthsCommand.Execute(null);
        }

        using var reopened = new JsonSettingsService(_directory);
        var next = new MainWindowViewModel(reopened);

        next.RailWidth.Should().Be(300);
        next.ActivityWidth.Should().Be(420);
    }
}

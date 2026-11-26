using Avalonia.Threading;
using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

/// <summary>
/// The rail's collapsible Build and Manage groups, and its recent tasks opening from any page.
/// </summary>
public sealed class RailTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kam-rail-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void RailGroups_StartCollapsedOnlyInAShortWindow()
    {
        using var settings = new JsonSettingsService(_directory);
        var viewModel = new MainWindowViewModel(settings);
        var changes = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        viewModel.IsBuildGroupCollapsed.Should().BeFalse();
        viewModel.IsManageGroupCollapsed.Should().BeFalse();

        viewModel.IsWindowShort = true;

        viewModel.IsBuildGroupCollapsed.Should().BeTrue();
        viewModel.IsManageGroupCollapsed.Should().BeTrue();
        changes.Should().Contain([nameof(MainWindowViewModel.IsBuildGroupCollapsed), nameof(MainWindowViewModel.IsManageGroupCollapsed)]);
        settings.RailBuildCollapsed.Should().BeNull("the window height alone saves nothing");
    }

    [Fact]
    public void ToggleRailGroup_SavesTheChoiceForTheNextStart()
    {
        using (var settings = new JsonSettingsService(_directory))
        {
            var viewModel = new MainWindowViewModel(settings) { IsWindowShort = true };

            viewModel.ToggleRailGroupCommand.Execute(MainWindowViewModel.BuildRailGroup);

            viewModel.IsBuildGroupCollapsed.Should().BeFalse("a click opens a group the short window collapsed");
            viewModel.IsManageGroupCollapsed.Should().BeTrue();
            settings.RailBuildCollapsed.Should().BeFalse();
            settings.RailManageCollapsed.Should().BeNull();
        }

        using (var reopened = new JsonSettingsService(_directory))
        {
            var viewModel = new MainWindowViewModel(reopened) { IsWindowShort = true };

            viewModel.IsBuildGroupCollapsed.Should().BeFalse("the saved choice beats the window height");
            viewModel.IsManageGroupCollapsed.Should().BeTrue();

            viewModel.ToggleRailGroupCommand.Execute(MainWindowViewModel.ManageRailGroup);
            viewModel.ToggleRailGroupCommand.Execute(MainWindowViewModel.BuildRailGroup);
            viewModel.IsWindowShort = false;

            viewModel.IsBuildGroupCollapsed.Should().BeTrue();
            viewModel.IsManageGroupCollapsed.Should().BeFalse();
        }

        using var third = new JsonSettingsService(_directory);
        third.RailBuildCollapsed.Should().BeTrue();
        third.RailManageCollapsed.Should().BeFalse();
    }

    [Fact]
    public void OpenTask_FromAnotherPage_ShowsTasksOnThatTask()
    {
        // Page switches run on Avalonia's UI thread; without a platform that is whichever test thread touched it first.
        Assert.SkipUnless(Dispatcher.UIThread.CheckAccess(), "Another test thread owns Avalonia's UI thread in this run.");

        using var settings = new JsonSettingsService(_directory);
        var viewModel = new MainWindowViewModel(settings);
        var first = viewModel.SelectedAgentChatSession!;
        viewModel.NewAgentChatCommand.Execute(null);
        viewModel.NavigateToPageCommand.Execute(NavView.Prompts).Subscribe();
        viewModel.IsChatWorkbenchVisible.Should().BeFalse();

        viewModel.OpenTaskCommand.Execute(first);

        viewModel.ActiveView.Should().Be(NavView.Coordinator);
        viewModel.IsChatWorkbenchVisible.Should().BeTrue();
        viewModel.SelectedAgentChatSession.Should().Be(first);
        first.IsSelected.Should().BeTrue();
    }
}

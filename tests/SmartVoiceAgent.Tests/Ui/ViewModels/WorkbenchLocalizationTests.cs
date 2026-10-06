using System.Text.RegularExpressions;
using FluentAssertions;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.SlashCommands;
using SmartVoiceAgent.Infrastructure.Services;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed partial class WorkbenchLocalizationTests
{
    private static readonly DateTimeOffset Now = new(
        2026, 10, 5, 18, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 5, 18, 0, 0)));

    [Theory]
    [InlineData(0, "az önce")]
    [InlineData(5, "5 dk önce")]
    [InlineData(180, "3 sa önce")]
    [InlineData(60 * 24, "Dün")]
    [InlineData(60 * 24 * 3, "2 Eki")]
    public void FormatRelativeTime_InTurkish_UsesNaturalForms(int minutesAgo, string expected)
    {
        var turkish = new LocalizationService();
        turkish.SetLanguage("tr-TR");

        MainWindowViewModel.FormatRelativeTime(Now.AddMinutes(-minutesAgo), Now, turkish).Should().Be(expected);
    }

    [Fact]
    public void FormatRelativeTime_InEnglish_KeepsShortLabels()
    {
        var english = new LocalizationService();

        MainWindowViewModel.FormatRelativeTime(Now.AddMinutes(-5), Now, english).Should().Be("5m");
        MainWindowViewModel.FormatRelativeTime(Now.AddDays(-3), Now, english).Should().Be("Oct 2");
    }

    [Fact]
    public void FormatStatusText_ReadsTheGivenLanguage()
    {
        var turkish = new LocalizationService();
        turkish.SetLanguage("tr-TR");

        SkillExecutionHistoryItemViewModel.FormatStatusText(SkillExecutionStatus.TimedOut, turkish).Should().Be("Zaman aşımı");
        SkillExecutionHistoryItemViewModel.FormatStatusText(SkillExecutionStatus.TimedOut).Should().Be("Timed Out");
        Enum.GetValues<SkillExecutionStatus>()
            .Select(status => SkillExecutionHistoryItemViewModel.FormatStatusText(status, turkish))
            .Should().NotContain(text => text.StartsWith("Workbench.", StringComparison.Ordinal));
    }

    [Fact]
    public void BuiltInSlashCommands_HaveDescriptionsAndCategoriesInBothLanguages()
    {
        var english = LocalizationService.LoadDictionary("en-US");
        var turkish = LocalizationService.LoadDictionary("tr-TR");

        foreach (var command in new SlashCommandService().GetCommands())
        {
            var key = SlashCommandSuggestionViewModel.GetSummaryKey(command.Name);
            english.Should().ContainKey(key, command.Name);
            english[key].Should().Be(command.Summary, $"{key} must stay in step with {command.Name}");
            turkish.Should().ContainKey(key, command.Name);
            english.Should().ContainKey("Workbench.SlashCategory." + command.Category, command.Name);
        }
    }

    [Theory]
    [InlineData("/help", "Workbench.Slash.Help")]
    [InlineData("/model health", "Workbench.Slash.Model.Health")]
    [InlineData("/github app run", "Workbench.Slash.Github.App.Run")]
    [InlineData("/github-app", "Workbench.Slash.GithubApp")]
    public void GetSummaryKey_MapsSpacesToSegmentsAndHyphensToPascalCase(string name, string expected)
    {
        SlashCommandSuggestionViewModel.GetSummaryKey(name).Should().Be(expected);
    }

    [Fact]
    public void SlashCommandSuggestion_KeepsTheOwnDescriptionOfACommandThatReusesABuiltInName()
    {
        var builtIn = new SlashCommandSuggestionViewModel(
            new SlashCommandDefinition("/help", "Show available slash commands.", "/help", "General"));
        var plugin = new SlashCommandSuggestionViewModel(
            new SlashCommandDefinition("/help", "Explain the plugin's commands.", "/help", "Plugin"));

        builtIn.Summary.Should().Be("Show available slash commands.");
        builtIn.Category.Should().Be("General");
        plugin.Summary.Should().Be("Explain the plugin's commands.");
        plugin.Category.Should().Be("Plugin");
    }

    [Fact]
    public void RefreshLocalizedText_RefreshesRowsCardsAndPickersWithoutTouchingUserText()
    {
        var viewModel = new MainWindowViewModel();
        var workspace = viewModel.SelectedAgentChatSession!;
        viewModel.NewAgentChatCommand.Execute(null);
        var renamed = viewModel.SelectedAgentChatSession!;
        renamed.Title = "Release plan";
        var step = AgentChatMessageViewModel.ToolStep("c1", "shell_run", "Run Shell Command", "{}", ToolRisk.Execute);
        step.RequestApproval("req-1", "shell_run(git status:*)");
        workspace.AddMessage(step);
        var mode = MainWindowViewModel.ApprovalModes[0];

        var raised = new List<string>();
        viewModel.PropertyChanged += (_, e) => raised.Add("vm." + e.PropertyName);
        workspace.PropertyChanged += (_, e) => raised.Add("workspace." + e.PropertyName);
        renamed.PropertyChanged += (_, e) => raised.Add("renamed." + e.PropertyName);
        step.PropertyChanged += (_, e) => raised.Add("step." + e.PropertyName);
        void OnModeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => raised.Add("mode." + e.PropertyName);
        mode.PropertyChanged += OnModeChanged;

        try
        {
            viewModel.RefreshLocalizedText();
        }
        finally
        {
            mode.PropertyChanged -= OnModeChanged;
        }

        raised.Should().Contain([
            "vm.StatusText",
            "vm.ActivePageTitle",
            "vm.AgentChatSessionCountText",
            "vm.SelectedAgentChatMessageCountText",
            "vm.AgentChatModelOptions",
            "vm.SkillExecutionHistoryStatusFilters",
            "workspace.Title",
            "workspace.RelativeTimeText",
            "workspace.StatusText",
            "workspace.MessageCountText",
            "step.Role",
            "step.RiskText",
            "step.ToolStatusText",
            "step.AlwaysAllowHint",
            "mode.Label",
            "mode.Description"
        ]);
        raised.Should().NotContain("renamed.Title", "a title the user typed is not a placeholder");
        workspace.Title.Should().Be("Workspace chat");
        renamed.Title.Should().Be("Release plan");
        step.RiskText.Should().Be("Runs programs");
        step.AlwaysAllowHint.Should().Be("Stop asking for shell_run(git status:*)");
        viewModel.StatusText.Should().Be("Starting");
        viewModel.SkillExecutionHistoryStatusFilter.Should().Be("All");
    }

    [Fact]
    public void RefreshLocalizedText_KeepsTheChosenHistoryStatusFilter()
    {
        var viewModel = new MainWindowViewModel();
        viewModel.SkillExecutionHistoryStatusFilter = "Timed Out";

        viewModel.RefreshLocalizedText();

        viewModel.SkillExecutionHistoryStatusFilter.Should().Be("Timed Out");
        viewModel.SkillExecutionHistoryStatusFilters.Should().Contain("Timed Out");
        viewModel.HasSkillExecutionHistoryFilter.Should().BeTrue();
    }

    [Fact]
    public void SessionRefresh_RecomputesTheLastActivityTextFromItsTime()
    {
        var session = AgentChatSessionViewModel.CreateSaved(
            "s1", "Weekly report", "now", 2, updatedAt: Now.AddMinutes(-5));

        session.RefreshLocalizedText(Now);

        session.RelativeTimeText.Should().Be("5m");
        session.Title.Should().Be("Weekly report");
        session.Summary.Should().Be("Saved thread");
    }

    [Fact]
    public void SavedThreadWithTheStoreDefaultTitle_IsNamedByItsFirstMessage()
    {
        AgentChatSessionViewModel.CreateSaved("s1", "New chat", "now", 0).HasPlaceholderTitle.Should().BeTrue();
        AgentChatSessionViewModel.CreateSaved("s2", "New chat", "now", 0, hasCustomTitle: true).HasPlaceholderTitle.Should().BeFalse();
        AgentChatSessionViewModel.CreateSaved("s3", "Weekly report", "now", 0).HasPlaceholderTitle.Should().BeFalse();
    }

    [Fact]
    public void WorkbenchKeysUsedInCode_ExistInEnglish()
    {
        var english = LocalizationService.LoadDictionary("en-US");
        var root = Path.GetFullPath(Path.Combine(LanguageResourceProductCopyTests.FindLanguageResourceDirectory(), "..", ".."));
        var missing = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => WorkbenchKey().Matches(File.ReadAllText(file))
                .Select(match => match.Groups["key"].Value)
                .Where(key => !english.ContainsKey(key))
                .Select(key => $"{Path.GetFileName(file)}: {key}"))
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void TurkishWorkbenchCopy_UsesTheGlossary()
    {
        var turkish = LocalizationService.LoadDictionary("tr-TR")
            .Where(entry => entry.Key.StartsWith("Workbench.", StringComparison.Ordinal))
            .ToList();

        turkish.Should().NotBeEmpty();
        turkish.Should().NotContain(entry =>
            Regex.IsMatch(entry.Value, @"\b(agent|chat|thread|skill|settings|workbench)\b", RegexOptions.IgnoreCase),
            "Turkish copy says ajan, sohbet, yetenek, Ayarlar and çalışma masası");
        turkish.Single(entry => entry.Key == "Workbench.Page.Workbench").Value.Should().Be("Çalışma masası");
        turkish.Single(entry => entry.Key == "Workbench.Thread.NewChat").Value.Should().Be("Yeni sohbet");
    }

    [GeneratedRegex(@"""(?<key>Workbench\.[A-Za-z0-9_.]*[A-Za-z0-9_])""")]
    private static partial Regex WorkbenchKey();
}

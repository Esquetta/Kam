using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class MainWindowMetadataTests
{
    [Fact]
    public void MainWindow_TitleDoesNotRenderProductNameOverSystemStatus()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root;

        mainWindow.Should().NotBeNull();
        mainWindow!.Attribute("Title")?.Value.Should().BeEmpty();
        mainWindow.Attribute("Title")?.Value.Should().NotContain("COORDINATOR");
    }

    [Fact]
    public void MainWindow_LogPanelUsesCalmerActivityCopy()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root;

        var visibleText = mainWindow!
            .Descendants()
            .Where(element => element.Name.LocalName is "TextBlock" or "Button")
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();

        visibleText.Should().Contain("Activity");
        visibleText.Should().Contain("Session");
        visibleText.Should().Contain("Agent runs");
        visibleText.Should().Contain("Event stream");
        visibleText.Should().Contain("Message");
        visibleText.Should().NotContain(value =>
            value.Contains("ACTIVITY_LOG", StringComparison.Ordinal)
            || value.Contains("KERNEL_LOG", StringComparison.Ordinal)
            || value.Contains("PENDING_CONFIRMATION", StringComparison.Ordinal)
            || value.Contains("PLANNER_TRACE", StringComparison.Ordinal)
            || value.Contains("RESULT_VIEWER", StringComparison.Ordinal)
            || value.Contains("Coordinator AI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainWindow_UsesModernWorkbenchShellChrome()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("Agent workspace");
        mainWindowText.Should().Contain("Border.Sidebar");
        mainWindowText.Should().Contain("Button.NavBtn");
        mainWindowText.Should().Contain("ActivePageTitle");
        mainWindowText.Should().Contain("BrandGradientBrush");
        mainWindowText.Should().Contain("Classes=\"ModelPicker\"");
        mainWindowText.Should().Contain("ComposerSurface");
        mainWindowText.Should().Contain("UseComposerSuggestionCommand");
        mainWindowText.Should().Contain("WindowStateManager.Instance");
        mainWindowText.Should().Contain("ContentControl Content=\"{Binding CurrentViewModel}\"");
        mainWindowText.Should().NotContain("BlurEffect Radius=\"120\"");
        mainWindowText.Should().NotContain("AccentCyan");
        mainWindowText.Should().NotContain("Command Center");
    }

    [Fact]
    public void MainWindow_UsesAgentWorkbenchDrawerTabs()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("SelectedActivityPanelMode");
        mainWindowText.Should().Contain("ShowRunsCommand");
        mainWindowText.Should().Contain("ShowContextCommand");
        mainWindowText.Should().Contain("ShowEventsCommand");
        mainWindowText.Should().Contain("ActivityPanelMode.Runs");
        mainWindowText.Should().Contain("ActivityPanelMode.Context");
        mainWindowText.Should().Contain("ActivityPanelMode.Events");
        mainWindowText.Should().Contain("Agent runs");
        mainWindowText.Should().Contain("Context");
        mainWindowText.Should().Contain("Event stream");
    }

    [Fact]
    public void MainWindow_ComposerExposesFileAttachmentChips()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("Attach files");
        mainWindowText.Should().Contain("OnAttachFilesClick");
        mainWindowText.Should().Contain("ComposerAttachments");
        mainWindowText.Should().Contain("HasComposerAttachments");
        mainWindowText.Should().Contain("ActiveComposerContextText");
        mainWindowText.Should().Contain("WorkbenchPromptInput");
        mainWindowText.Should().Contain("Ask Kam to inspect, edit, test, or plan");
        mainWindowText.Should().Contain("RemoveComposerAttachmentCommand");
        mainWindowText.Should().Contain("ClearComposerAttachmentsCommand");
        mainWindowText.Should().Contain("Text=\"{Binding FileName}\"");
        mainWindowText.Should().Contain("Text=\"{Binding DisplayPath}\"");
        mainWindowText.Should().Contain("IsVisible=\"{Binding IsPageHostVisible}\"");
    }

    [Fact]
    public void MainWindow_ExposesMultiSessionAgentWorkbench()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("AgentChatSessions");
        mainWindowText.Should().Contain("SelectedAgentChatSession.Messages");
        mainWindowText.Should().Contain("AgentChatSessionCountText");
        mainWindowText.Should().Contain("SelectedAgentChatMessageCountText");
        mainWindowText.Should().Contain("MessageCountText");
        mainWindowText.Should().Contain("AgentName");
        mainWindowText.Should().Contain("NewAgentChatCommand");
        mainWindowText.Should().Contain("SelectAgentChatCommand");
        mainWindowText.Should().Contain("IsChatWorkbenchVisible");
        mainWindowText.Should().Contain("IsPageHostVisible");
        mainWindowText.Should().Contain("Chats");
        mainWindowText.Should().Contain("Agent threads");
        mainWindowText.Should().Contain("New task");
        mainWindowText.Should().Contain("Conversation timeline");
        mainWindowText.Should().NotContain("Model follows Settings");
        mainWindowText.Should().Contain("Start a focused agent task");
        mainWindowText.Should().Contain("Review current workspace");
        mainWindowText.Should().NotContain("Command Deck");
    }

    [Fact]
    public void MainWindow_ThreadListSupportsSearchRenameAndDelete()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("x:Name=\"ChatSearchInput\"");
        mainWindowText.Should().Contain("PlaceholderText=\"Search chats (Ctrl+K)\"");
        mainWindowText.Should().Contain("Text=\"{Binding AgentChatSearchText, Mode=TwoWay}\"");
        mainWindowText.Should().Contain("ClearAgentChatSearchCommand");
        mainWindowText.Should().Contain("IsVisible=\"{Binding HasNoAgentChatSearchResults}\"");
        mainWindowText.Should().Contain("No chats match");
        mainWindowText.Should().Contain("IsVisible=\"{Binding IsVisibleInList}\"");
        mainWindowText.Should().Contain("<MenuItem Header=\"Rename\" InputGesture=\"F2\" Click=\"OnRenameChatClick\"/>");
        mainWindowText.Should().Contain("Click=\"OnDeleteChatClick\"");
        mainWindowText.Should().Contain("Text=\"{Binding EditTitle, Mode=TwoWay}\"");
        mainWindowText.Should().Contain("KeyDown=\"OnRenameKeyDown\"");
        mainWindowText.Should().Contain("LostFocus=\"OnRenameLostFocus\"");
        mainWindowText.Should().Contain("Delete this chat?");
        mainWindowText.Should().Contain("ConfirmDeleteAgentChatCommand");
        mainWindowText.Should().Contain("CancelDeleteAgentChatCommand");
        mainWindowText.Should().Contain("ToolTip.Tip=\"New chat (Ctrl+N)\"");
    }

    [Fact]
    public void MainWindow_ChatRendersMarkdownWithCopyButtonsAndAModelPicker()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("xmlns:controls=\"using:SmartVoiceAgent.Ui.Controls\"");
        mainWindowText.Should().Contain("<controls:MarkdownView Markdown=\"{Binding Content}\"/>");
        mainWindowText.Should().Contain("CopyAgentMessageCommand");
        mainWindowText.Should().Contain("IsVisible=\"{Binding IsCopied}\"");
        mainWindowText.Should().Contain("Header=\"Copy conversation\" Command=\"{Binding CopyAgentChatCommand}\"");
        mainWindowText.Should().Contain("ItemsSource=\"{Binding AgentChatModelOptions}\"");
        mainWindowText.Should().Contain("SelectedItem=\"{Binding SelectedAgentChatModel, Mode=TwoWay}\"");
        mainWindowText.Should().Contain("ToolTip.Tip=\"{Binding AgentChatModelTip}\"");
        mainWindowText.Should().Contain("<DataTemplate DataType=\"vm:AgentChatModelOption\">");
    }

    [Fact]
    public void MainWindow_ComposerSendsOnEnterAndAddsLinesOnShiftEnter()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root;
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        var prompt = mainWindow!
            .Descendants()
            .Single(element => AttributeValue(element, "Name") == "WorkbenchPromptInput");

        AttributeValue(prompt, "AcceptsReturn").Should().Be("True");
        AttributeValue(prompt, "KeyDown").Should().BeNull("MainWindow handles prompt keys before the TextBox adds a line");
        mainWindowText.Should().Contain("Enter to send  ·  Shift+Enter for a new line  ·  Type / for commands");
        mainWindowText.Should().Contain("ToolTip.Tip=\"Stop (Esc)\"");
    }

    [Fact]
    public void MainWindow_SidebarAndPageHost_IncludeExtensions()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("Command=\"{Binding NavigateToExtensionsCommand}\"");
        mainWindowText.Should().Contain("ConverterParameter={x:Static vm:NavView.Extensions}");
        mainWindowText.Should().Contain("<DataTemplate DataType=\"pagevm:ExtensionsViewModel\">");
        mainWindowText.Should().Contain("<local:ExtensionsView/>");
        mainWindowText.Should().Contain("Data=\"{StaticResource IconPuzzle}\"");
    }

    [Fact]
    public void MainWindow_ChatRendersAgentToolStepsAndApprovalCards()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("IsVisible=\"{Binding IsToolStep}\"");
        mainWindowText.Should().Contain("Classes.Approval=\"{Binding IsAwaitingApproval}\"");
        mainWindowText.Should().Contain("Text=\"{Binding ToolDisplayName}\"");
        mainWindowText.Should().Contain("Text=\"{Binding ArgumentsPreview}\"");
        mainWindowText.Should().Contain("Text=\"{Binding ToolStatusText}\"");
        mainWindowText.Should().Contain("ApproveToolCallCommand");
        mainWindowText.Should().Contain("AlwaysAllowToolCallCommand");
        mainWindowText.Should().Contain("DenyToolCallCommand");
        mainWindowText.Should().Contain("Content=\"Always allow\"");
        mainWindowText.Should().Contain("StopAgentTurnCommand");
        mainWindowText.Should().Contain("IsVisible=\"{Binding IsSendVisible}\"");
        mainWindowText.Should().Contain("Kam is working");
        mainWindowText.Should().Contain("vm:MainWindowViewModel.ApprovalModes");
        mainWindowText.Should().Contain("SelectedItem=\"{Binding SelectedApprovalMode}\"");
        mainWindowText.Should().Contain("x:Name=\"ChatScrollViewer\"");
    }

    [Fact]
    public void MainWindow_ActivityPanelUsesStructuredFeedBindings()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("ItemsSource=\"{Binding ActivityLogEntries}\"");
        mainWindowText.Should().Contain("ItemsSource=\"{Binding RuntimeAgentActivities}\"");
        mainWindowText.Should().Contain("IsVisible=\"{Binding !HasRuntimeAgentActivities}\"");
        mainWindowText.Should().Contain("Classes=\"ActivityLogItem\"");
        mainWindowText.Should().Contain("ConverterParameter={x:Static vm:ActivityPanelMode.Events}");
        mainWindowText.Should().Contain("Text=\"{Binding CategoryText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding SourceText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding MessageText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding TimeText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding DisplayName}\"");
        mainWindowText.Should().Contain("Text=\"{Binding StatusText}\"");
        mainWindowText.Should().Contain("Text=\"{Binding LastMessage}\"");
        mainWindowText.Should().Contain("ShowRuntimeAgentRunDetailCommand");
        mainWindowText.Should().Contain("CommandParameter=\"{Binding}\"");
        mainWindowText.Should().Contain("IsVisible=\"{Binding HasSelectedRuntimeAgentRun}\"");
        mainWindowText.Should().Contain("SelectedRuntimeAgentRun.ModelIdText");
        mainWindowText.Should().Contain("SelectedRuntimeAgentRun.Observations");
        mainWindowText.Should().Contain("Text=\"{Binding SummaryText}\"");
        mainWindowText.Should().Contain("Run detail");
        mainWindowText.Should().Contain("Context");
    }

    [Fact]
    public void MainWindow_ExposesRuntimeDiagnosticsNavigation()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("NavigateToDiagnosticsCommand");
        mainWindowText.Should().Contain("RuntimeDiagnosticsViewModel");
        mainWindowText.Should().Contain("RuntimeDiagnosticsView");
        mainWindowText.Should().Contain("ToolTip.Tip=\"Runtime Diagnostics\"");
    }

    [Fact]
    public void MainWindow_ExposesSlashCommandPaletteBindings()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        mainWindowText.Should().Contain("IsSlashCommandPaletteVisible");
        mainWindowText.Should().Contain("SlashCommandSuggestions");
        mainWindowText.Should().Contain("SelectSlashCommandCommand");
        mainWindowText.Should().Contain("Type / for commands");
    }

    [Fact]
    public void MainWindow_SlashCommandPaletteUsesCalmSuggestionChrome()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root;
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());

        var templates = mainWindow!
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "DataTemplate"
                && AttributeValue(element, "DataType") == "vm:SlashCommandSuggestionViewModel")
            .ToArray();

        templates.Should().HaveCountGreaterThanOrEqualTo(1);

        foreach (var template in templates)
        {
            var suggestionButton = template
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Button"
                    && AttributeValue(element, "Classes") == "SlashCommandItem");

            var suggestionChrome = suggestionButton
                .Descendants()
                .Single(element =>
                    element.Name.LocalName == "Border"
                    && AttributeValue(element, "Background")?.Contains("IsSelected", StringComparison.Ordinal) == true);

            AttributeValue(suggestionButton, "Command")
                .Should()
                .Be("{Binding $parent[Window].DataContext.SelectSlashCommandCommand}");
            AttributeValue(suggestionChrome, "Background")
                .Should()
                .Contain("ConverterParameter='CardBgHoverBrush|TransparentBrush'");
            AttributeValue(suggestionChrome, "BorderBrush")
                .Should()
                .Be("Transparent");
            AttributeValue(suggestionChrome, "HorizontalAlignment").Should().Be("Stretch");
        }

        mainWindowText.Should().Contain("<Style Selector=\"Button.SlashCommandItem\">");
        mainWindowText.Should().Contain("<Style Selector=\"Button.SlashCommandItem /template/ ContentPresenter#PART_ContentPresenter\">");
        mainWindowText.Should().Contain("<Style Selector=\"Button.SlashCommandItem:pointerover /template/ ContentPresenter#PART_ContentPresenter\">");
        mainWindowText.Should().Contain("Opacity=\"{Binding IsSelected, Converter={StaticResource BoolToOpacityConverter}, ConverterParameter='1|0'}\"");
        mainWindowText.Should().Contain("HorizontalAlignment=\"Stretch\"");
    }

    [Fact]
    public void MainWindow_SlashCommandPaletteAvoidsAlertColorsAndShadowEffects()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root;

        var templates = mainWindow!
            .Descendants()
            .Where(element =>
                element.Name.LocalName == "DataTemplate"
                && AttributeValue(element, "DataType") == "vm:SlashCommandSuggestionViewModel")
            .ToArray();

        templates.Should().HaveCountGreaterThanOrEqualTo(1);

        var templateText = string.Join(
            " ",
            templates
                .SelectMany(template => template
                .DescendantsAndSelf()
                .SelectMany(element => element.Attributes())
                .Select(attribute => attribute.Value)));

        templateText.Should().NotContain("AccentGreen");
        templateText.Should().NotContain("AccentError");
        templateText.Should().NotContain("Red");
        templateText.Should().NotContain("Green");
        templateText.Should().NotContain("BoxShadow");
        templates
            .SelectMany(template => template.Descendants())
            .Should()
            .NotContain(element =>
                element.Name.LocalName.Contains("Shadow", StringComparison.OrdinalIgnoreCase)
                || element.Name.LocalName.Contains("BlurEffect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MainWindow_SlashCommandPaletteBrushKeysExist()
    {
        var brushes = XDocument.Load(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Themes", "Brushes.axaml")).Root;
        var brushKeys = brushes!
            .Descendants()
            .Select(element => AttributeValue(element, "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();

        brushKeys.Should().Contain("AccentBrush");
        brushKeys.Should().Contain("TransparentBrush");
        brushKeys.Should().Contain("CardBgHoverBrush");
        brushKeys.Should().Contain("CardBgBrush");
        brushKeys.Should().Contain("BorderSubtleBrush");
    }

    [Fact]
    public void MainWindow_BottomNavigationOnlyRendersThemeToggle()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root;

        var bottomNavigation = mainWindow!
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && AttributeValue(element, "Grid.Row") == "2"
                && AttributeValue(element, "Classes") == "SidebarFooter");

        bottomNavigation
            .Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Should()
            .ContainSingle(button =>
                AttributeValue(button, "Command") == "{Binding ToggleThemeCommand}"
                && AttributeValue(button, "ToolTip.Tip") == "Toggle Theme");

        bottomNavigation
            .Descendants()
            .Where(element => element.Name.LocalName == "Border")
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void MainWindow_ThemeToggleLabelFollowsTheLanguage()
    {
        var mainWindowText = File.ReadAllText(FindMainWindowXamlPath());
        var mainWindow = XDocument.Parse(mainWindowText).Root;

        var labelGroup = mainWindow!
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "StackPanel"
                && AttributeValue(element, "Classes") == "SidebarFooter")
            .Descendants()
            .Single(element => element.Name.LocalName == "Panel" && AttributeValue(element, "Classes") == "NavLabelGroup");

        var labels = labelGroup.Elements().Where(element => element.Name.LocalName == "TextBlock").ToArray();
        labels.Should().HaveCount(2);
        labels.Select(label => (AttributeValue(label, "Text"), AttributeValue(label, "IsVisible")))
            .Should()
            .BeEquivalentTo(new[]
            {
                ("{DynamicResource Lang.Shell.LightMode}", "{Binding IsDarkMode}"),
                ("{DynamicResource Lang.Shell.DarkMode}", "{Binding !IsDarkMode}")
            });

        // The labels bind IsVisible themselves, so the compact sidebar hides their group instead.
        mainWindowText.Should().Contain("<Style Selector=\"Border.Sidebar.Compact Panel.NavLabelGroup\">");
        mainWindowText.Should().NotContain("ConverterParameter='Light mode|Dark mode'");

        LocalizedXaml.English["Shell.LightMode"].Should().Be("Light mode");
        LocalizedXaml.English["Shell.DarkMode"].Should().Be("Dark mode");
        var turkish = LocalizationService.LoadDictionary("tr-TR");
        turkish["Shell.LightMode"].Should().Be("Açık tema");
        turkish["Shell.DarkMode"].Should().Be("Koyu tema");
    }

    [Fact]
    public void MainWindow_MicButtonsTalkAndTheComposerShowsVoiceStatus()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindMainWindowXamlPath());
        var mainWindow = XDocument.Parse(mainWindowText).Root!;
        var talkButtons = mainWindow
            .Descendants()
            .Where(element => element.Name.LocalName == "Button"
                && (AttributeValue(element, "Classes") ?? string.Empty).Contains("TalkButton", StringComparison.Ordinal))
            .ToArray();

        talkButtons.Should().HaveCount(2, "the chat composer and the command prompt on other pages both have one");
        foreach (var button in talkButtons)
        {
            AttributeValue(button, "Command").Should().Be("{Binding TalkCommand}");
            AttributeValue(button, "ToolTip.Tip").Should().Be("{Binding TalkToolTip}");
            AttributeValue(button, "IsEnabled").Should().Be("{Binding IsVoiceAvailable}");
            AttributeValue(button, "Classes.listening").Should().Be("{Binding IsVoiceListening}");
        }

        mainWindowText.Should().NotContain("ToggleVoiceCommand");
        mainWindowText.Should().NotContain("Toggle Voice Control");

        var status = mainWindow.Descendants().Single(element => AttributeValue(element, "Name") == "VoiceStatusPanel");
        AttributeValue(status, "IsVisible").Should().Be("{Binding IsVoiceStatusVisible}");
        status.Descendants().Should().Contain(element =>
            element.Name.LocalName == "ProgressBar" && AttributeValue(element, "Value") == "{Binding VoiceMeterValue}");
        status.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Button"
            && AttributeValue(element, "Command") == "{Binding CancelVoiceCommand}"
            && AttributeValue(element, "ToolTip.Tip") == "Cancel (Esc)");
    }

    [Fact]
    public void MainWindow_HeaderStatusIsAButtonWithATooltip()
    {
        var mainWindow = XDocument.Parse(LocalizedXaml.ReadAllText(FindMainWindowXamlPath())).Root!;

        var status = mainWindow.Descendants().Single(element => AttributeValue(element, "Name") == "HeaderStatusButton");

        status.Name.LocalName.Should().Be("Button");
        AttributeValue(status, "Classes").Should().Be("PillButton");
        AttributeValue(status, "Command").Should().Be("{Binding HeaderStatusCommand}");
        AttributeValue(status, "ToolTip.Tip").Should().Be("{Binding StatusToolTip}");
        status.Descendants().Should().Contain(element =>
            element.Name.LocalName == "TextBlock" && AttributeValue(element, "Text") == "{Binding StatusText, Mode=OneWay}");
    }

    private static string? AttributeValue(XElement element, string attributeName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)
            ?.Value;
    }

    private static string FindMainWindowXamlPath()
    {
        return FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Views", "MainWindow.axaml");
    }

    private static string FindProjectFilePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidateSegments = new[] { directory.FullName }
                .Concat(segments)
                .ToArray();
            var candidate = Path.Combine(candidateSegments);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate {Path.Combine(segments)} from the test output directory.");
    }
}

using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using System.Reflection;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

/// <summary>
/// Pins the Kam Studio shell: tokens, fonts, shared primitives, the rail with the Build ladder,
/// the top bar, the Tasks workbench and the Needs you rows.
/// </summary>
public sealed class KamStudioShellMetadataTests
{
    [Fact]
    public void Themes_UseKamStudioPalettes()
    {
        var dark = ReadColors("Colors.Dark.axaml");
        var light = ReadColors("Colors.Light.axaml");

        dark["PanelBg"].Should().Be("#0B0B0E");
        dark["CardBg"].Should().Be("#17171B");
        dark["RaisedBg"].Should().Be("#1E1E23");
        dark["CardBgHover"].Should().Be("#25252B");
        dark["BorderSubtle"].Should().Be("#26262C");
        dark["BorderMuted"].Should().Be("#36363E");
        dark["TextPrimary"].Should().Be("#EDEDF0");
        dark["Accent"].Should().Be("#8B7CFF");
        dark["AccentStrong"].Should().Be("#6E5BF0");
        dark["AccentInfo"].Should().Be("#60A5FA");

        light["PanelBg"].Should().Be("#F7F7F8");
        light["CardBg"].Should().Be("#FFFFFF");
        light["RaisedBg"].Should().Be("#F3F3F5");
        light["Accent"].Should().Be("#5B47E0");
        light["AccentError"].Should().Be("#C81E1E", "the prototype's #DC2626 is 4.4:1 on the light surface");
        light["AccentInfo"].Should().Be("#2563EB");

        // Every token exists in both themes, so a DynamicResource never falls back in one of them.
        dark.Keys.Should().BeEquivalentTo(light.Keys);
    }

    [Fact]
    public void Fonts_AreBundledGeistWithTheirLicence()
    {
        var fonts = Path.GetDirectoryName(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Assets", "Fonts", "OFL.txt"))!;
        Directory.GetFiles(fonts, "*.ttf").Select(Path.GetFileName).Should().BeEquivalentTo(
            "Geist-Regular.ttf", "Geist-Medium.ttf", "Geist-SemiBold.ttf", "Geist-Bold.ttf",
            "GeistMono-Regular.ttf", "GeistMono-Medium.ttf", "GeistMono-SemiBold.ttf");
        File.ReadAllText(Path.Combine(fonts, "OFL.txt")).Should().Contain("SIL Open Font License");

        var theme = File.ReadAllText(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Themes", "AppTheme.axaml"));
        theme.Should().Contain("<FontFamily x:Key=\"ContentControlThemeFontFamily\">fonts:Geist#Geist, $Default</FontFamily>");
        theme.Should().Contain("<FontFamily x:Key=\"MonoFontFamily\">fonts:Geist#Geist Mono");

        var program = File.ReadAllText(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Program.cs"));
        program.Should().Contain("new Uri(\"fonts:Geist\", UriKind.Absolute)");
        program.Should().Contain("avares://SmartVoiceAgent.Ui/Assets/Fonts");
    }

    [Fact]
    public void Controls_ExposeKamStudioPrimitives()
    {
        var selectors = XDocument.Load(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Themes", "Controls.axaml")).Root!
            .Descendants()
            .Where(element => element.Name.LocalName == "Style")
            .Select(element => AttributeValue(element, "Selector"))
            .ToArray();

        selectors.Should().Contain(new[]
        {
            "Button.Sm",
            "Border.Pill.Info",
            "Border.Risk",
            "Border.Risk.Read",
            "Border.Risk.Write",
            "Border.Risk.Shell",
            "Border.Pane",
            "Border.PaneHead",
            "Border.PaneRow",
            "TextBlock.KvKey",
            "TextBlock.KvValue",
            "Border.Note",
            "Border.Code",
            "Border.Kbd",
            "ToggleSwitch:checked /template/ Grid#PART_MovingKnobs",
            ":is(Control).PageColumn.Form",
            ":is(Control).PageColumn.List",
            ":is(Control).PageColumn.Wide",
            "Grid.PageHeader",
            "StackPanel.PageHeaderText",
            "StackPanel.EmptyState",
            "StackPanel.EmptyState TextBlock.EmptyTitle"
        });
        selectors.Should().NotContain(selector => selector!.Contains("Explain", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("InboxView.axaml")]
    [InlineData("PromptsView.axaml")]
    [InlineData("AgentsView.axaml")]
    [InlineData("LoopsView.axaml")]
    [InlineData("RunsView.axaml")]
    [InlineData("GraphsView.axaml")]
    [InlineData("ExtensionsView.axaml")]
    [InlineData("PluginsView.axaml")]
    [InlineData("IntegrationsView.axaml")]
    [InlineData("SettingsView.axaml")]
    [InlineData("RuntimeDiagnosticsView.axaml")]
    public void Pages_HaveOnePageHeaderInAPageColumnAndNoExplanationBox(string viewFileName)
    {
        var view = LoadXaml(viewFileName);

        // One header: the title and a one-line subtitle, no eyebrow above them.
        var header = view.Descendants().Single(element => AttributeValue(element, "Classes") == "PageHeader");
        var text = header.Descendants().Single(element => AttributeValue(element, "Classes") == "PageHeaderText");
        text.Elements().First().Should().Match<XElement>(element => AttributeValue(element, "Classes") == "PageTitle");
        text.Descendants().Should().ContainSingle(element => AttributeValue(element, "Classes") == "PageSubtitle");
        view.Descendants().Should().NotContain(element => AttributeValue(element, "Text") != null
            && (AttributeValue(element, "Text")!.Contains("Eyebrow", StringComparison.Ordinal)
                || AttributeValue(element, "Text")!.Contains(".Overline}", StringComparison.Ordinal)));

        // The page root takes the shared column, so content starts and centers the same way on every page.
        header.Ancestors().Should().Contain(element => HasClass(element, "PageColumn"));

        // No explanation strips; empty states have no box.
        view.Descendants().Should().NotContain(element => HasClass(element, "Explain"));
        view.Descendants()
            .Where(element => AttributeValue(element, "Classes") == "EmptyState")
            .Should().OnlyContain(empty => !empty.Ancestors().Any(ancestor =>
                HasClass(ancestor, "Pane") || HasClass(ancestor, "WorkbenchSection") || HasClass(ancestor, "Note")));
    }

    private static bool HasClass(XElement element, string className) =>
        (AttributeValue(element, "Classes") ?? string.Empty).Split(' ').Contains(className);

    [Fact]
    public void Rail_BuildGroupIsPlainNavigationWithoutBadges()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindView("MainWindow.axaml"));
        var mainWindow = XDocument.Parse(mainWindowText).Root!;

        var build = mainWindow.Descendants()
            .Where(element => element.Name.LocalName == "Button"
                && AttributeValue(element, "Classes.NavBtn") == "True"
                && AttributeValue(element, "Command") == "{Binding NavigateToPageCommand}")
            .ToArray();

        build.Select(step => AttributeValue(step, "AutomationProperties.Name")).Should().Equal(
            "Prompts", "Agents", "Loops", "Graphs", "Runs");
        build.Select(step => AttributeValue(step, "CommandParameter")).Should().Equal(
            "{x:Static vm:NavView.Prompts}", "{x:Static vm:NavView.Agents}", "{x:Static vm:NavView.Loops}",
            "{x:Static vm:NavView.Graphs}", "{x:Static vm:NavView.Runs}");
        build.Should().OnlyContain(step => AttributeValue(step, "ToolTip.Tip") != null
            && step.Descendants().Any(element => AttributeValue(element, "Classes") == "Icon"));

        // No step numbers, status badges, planned pages or spend card; Skills and Diagnostics live outside the rail.
        mainWindowText.Should().NotContainAny("LadderStep", "LadderLevel", "LadderBadge", "LadderLine", "SpendCard", "NavView.Builder");
        mainWindowText.Should().NotContainAny("NavView.Plugins", "NavView.Diagnostics", "NavigateToPluginsCommand", "NavigateToDiagnosticsCommand");

        mainWindow.Descendants().Should().Contain(element =>
            AttributeValue(element, "Classes") == "Sidebar"
            && AttributeValue(element, "Classes.Compact") == "{Binding Source={x:Static services:WindowStateManager.Instance}, Path=IsRailCompact}");
    }

    [Fact]
    public void TopBar_Is48PxWithSearchWakePhraseAndPushToTalk()
    {
        var mainWindowText = LocalizedXaml.ReadAllText(FindView("MainWindow.axaml"));
        var mainWindow = XDocument.Parse(mainWindowText).Root!;

        mainWindowText.Should().Contain("<Grid RowDefinitions=\"48,*\">");
        mainWindow.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Grid"
            && AttributeValue(element, "Classes") == "TopBar"
            && AttributeValue(element, "Classes.narrow") == "{Binding Source={x:Static services:WindowStateManager.Instance}, Path=IsNarrow}");
        mainWindow.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Button"
            && AttributeValue(element, "Classes") == "MicButton"
            && AttributeValue(element, "Command") == "{Binding TalkCommand}");
        mainWindowText.Should().Contain("<Style Selector=\"Grid.TopBar.narrow TextBlock.TitleSearchText, Grid.TopBar.narrow Border.Kbd, Grid.TopBar.narrow Panel.WakePill\">");

        // On Tasks the bar carries the thread title, the model picker and the chat menu; the thread has no header row.
        var thread = mainWindow.Descendants().Single(element => AttributeValue(element, "IsVisible") == "{Binding IsChatWorkbenchVisible}"
            && element.Ancestors().Any(ancestor => AttributeValue(ancestor, "Classes") == "TopBar"));
        thread.Descendants().Should().Contain(element =>
            AttributeValue(element, "Classes") == "ThreadTitle" && AttributeValue(element, "Text") == "{Binding SelectedAgentChatSession.Title}");
        thread.Descendants().Should().Contain(element => AttributeValue(element, "Classes") == "ModelPicker");
        thread.Descendants().Should().Contain(element =>
            element.Name.LocalName == "MenuItem" && AttributeValue(element, "Command") == "{Binding DeleteAgentChatCommand}");
        var tasks = LoadXaml("TasksView.axaml");
        tasks.Descendants().Should().NotContain(element =>
            AttributeValue(element, "Classes") == "PageTitle" || AttributeValue(element, "Classes") == "ModelPicker");
    }

    [Fact]
    public void Tasks_IsAThreePaneWorkbenchThatReflowsWithStyles()
    {
        var tasksText = LocalizedXaml.ReadAllText(FindView("TasksView.axaml"));
        var tasks = XDocument.Parse(tasksText).Root!;

        var workbench = tasks.Descendants().Single(element => AttributeValue(element, "Classes") == "Workbench");
        AttributeValue(workbench, "Classes.medium").Should().Contain("Path=!IsLogPanelVisible");
        AttributeValue(workbench, "Classes.stacked").Should().Contain("Path=IsRailCompact");

        // The panes take their place from styles only, so the breakpoints can move them.
        var panes = workbench.Elements().Where(element => AttributeValue(element, "Classes") is "TaskListPane" or "ThreadPane").ToArray();
        panes.Should().HaveCount(2);
        panes.Should().OnlyContain(pane => pane.Attributes().All(attribute =>
            !attribute.Name.LocalName.StartsWith("Grid.", StringComparison.Ordinal) && attribute.Name.LocalName != "Width"));
        tasksText.Should().Contain("<Setter Property=\"Width\" Value=\"270\"/>");
        tasksText.Should().Contain("<Setter Property=\"Width\" Value=\"240\"/>");

        // Tool steps carry a risk tag when the risk matters; the composer bar has the context meter and ends with a Send button, without key hints.
        tasks.Descendants().Should().Contain(element =>
            AttributeValue(element, "Classes") == "RiskTag"
            && AttributeValue(element, "Classes.Shell") == "{Binding IsShellRisk}"
            && AttributeValue(element, "IsVisible") == "{Binding ShowsRiskTag}");
        File.ReadAllText(FindView("TasksView.axaml")).Should().Contain("{DynamicResource Lang.Chat.Composer.SendLabel}");
        tasks.Descendants().Should().NotContain(element => AttributeValue(element, "Classes") == "Kbd");
        var meter = tasks.Descendants().Single(element => AttributeValue(element, "Classes") == "ContextMeter");
        AttributeValue(meter, "Classes.Warning").Should().Be("{Binding IsSelectedContextNearBudget}");
        AttributeValue(meter, "Value").Should().Be("{Binding SelectedContextPercent}");
        var meterButton = meter.Ancestors().First(element => element.Name.LocalName == "Button");
        AttributeValue(meterButton, "IsVisible").Should().Be("{Binding HasSelectedContextTokens}");
        meterButton.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Button"
            && AttributeValue(element, "Command") == "{Binding CompactAgentChatCommand}"
            && AttributeValue(element, "IsEnabled") == "{Binding CanCompactSelectedTask}");
        meterButton.Descendants().Should().Contain(element => AttributeValue(element, "Text") == "{Binding SelectedCompactionText}");

        var mainWindowText = LocalizedXaml.ReadAllText(FindView("MainWindow.axaml"));
        mainWindowText.Should().NotContain("Border.AgentAvatar");
    }

    [Fact]
    public void Thread_SetsKamsOwnNoticesApartFromAgentReplies()
    {
        var tasks = XDocument.Parse(File.ReadAllText(FindView("TasksView.axaml"))).Root!;

        var agentMessage = tasks.Descendants().Single(element => AttributeValue(element, "IsVisible") == "{Binding IsAgent}");
        agentMessage.Descendants().Should().Contain(element =>
            element.Name.LocalName == "TextBlock"
            && AttributeValue(element, "Text") == "{Binding Role}"
            && AttributeValue(element, "IsVisible") == "{Binding IsSystem}");
        agentMessage.Descendants().Should().Contain(element =>
            element.Name.LocalName == "Border"
            && AttributeValue(element, "Classes.Note") == "{Binding IsSystem}"
            && element.Elements().Any(child => child.Name.LocalName == "MarkdownView"));

        new SmartVoiceAgent.Ui.ViewModels.AgentChatMessageViewModel(
            SmartVoiceAgent.Ui.ViewModels.AgentChatMessageViewModel.SystemRole, "Compacted", "10:00").IsSystem.Should().BeTrue();
        new SmartVoiceAgent.Ui.ViewModels.AgentChatMessageViewModel(
            SmartVoiceAgent.Ui.ViewModels.AgentChatMessageViewModel.AgentRole, "Done", "10:00").IsSystem.Should().BeFalse();
    }

    [Fact]
    public void Thread_ShowsToolStepsAsCompactLinesInACenteredColumn()
    {
        var tasks = XDocument.Parse(LocalizedXaml.ReadAllText(FindView("TasksView.axaml"))).Root!;

        // Messages and the composer share one centered column.
        var scroll = tasks.Descendants().Single(element => AttributeValue(element, "Name") == "ChatScrollViewer");
        AttributeValue(scroll.Elements().First(), "MaxWidth").Should().Be("760");
        tasks.Descendants().Should().Contain(element =>
            element.Name.LocalName == "StackPanel" && AttributeValue(element, "Grid.Row") == "1" && AttributeValue(element, "MaxWidth") == "760");

        // One line template serves top-level steps, read groups and subagent cards.
        var line = tasks.Descendants().Single(element =>
            element.Name.LocalName == "DataTemplate" && AttributeValue(element, "Key") == "ToolStepLineTemplate");
        var lineBorder = line.Elements().Single();
        AttributeValue(lineBorder, "Classes").Should().Be("ToolLine");
        AttributeValue(lineBorder, "Classes.Approval").Should().Be("{Binding IsAwaitingApproval}");
        line.Descendants().Should().NotContain(element => AttributeValue(element, "Classes") == "Pill", "a step has a status glyph, not a pill");
        line.Descendants().Should().Contain(element =>
            AttributeValue(element, "Classes") == "ToolLineError" && AttributeValue(element, "IsVisible") == "{Binding ShowsToolError}");

        var childLists = tasks.Descendants()
            .Where(element => element.Name.LocalName == "ItemsControl" && AttributeValue(element, "ItemsSource") == "{Binding Children}")
            .ToArray();
        childLists.Should().HaveCount(2, "a read group and a subagent card each list their steps");
        childLists.Should().OnlyContain(list => AttributeValue(list, "ItemTemplate") == "{StaticResource ToolStepLineTemplate}");
        childLists.Should().OnlyContain(list => list.Ancestors().Any(ancestor =>
            AttributeValue(ancestor, "Classes") == "ToolChildren" && AttributeValue(ancestor, "IsVisible") == "{Binding IsExpanded}"));

        var group = tasks.Descendants().Single(element => AttributeValue(element, "IsVisible") == "{Binding IsToolGroup}");
        group.Descendants().Should().Contain(element => AttributeValue(element, "Text") == "{Binding GroupTitle}");
        var card = tasks.Descendants().Single(element => AttributeValue(element, "Classes") == "SubagentCard");
        AttributeValue(card, "IsVisible").Should().Be("{Binding IsSubagentTask}");
        card.Descendants().Should().Contain(element =>
            AttributeValue(element, "Command") == "{Binding $parent[local:TasksView].DataContext.OpenSubagentGraphCommand}");

        // The message list stays virtualized; nesting lives only inside groups and cards.
        var messageList = tasks.Descendants().Single(element => AttributeValue(element, "Name") == "ChatMessageList");
        messageList.Descendants().Should().Contain(element => element.Name.LocalName == "VirtualizingStackPanel");

        var mainWindowText = LocalizedXaml.ReadAllText(FindView("MainWindow.axaml"));
        mainWindowText.Should().NotContain("Border.ToolStep");
        mainWindowText.Should().Contain("<Style Selector=\"Border.ToolLine.Approval\">");
    }

    [Fact]
    public void NeedsYou_ListsApprovalsAsRowsInOnePanel()
    {
        var inbox = LoadXaml("InboxView.axaml");

        var row = inbox.Descendants().Single(element => AttributeValue(element, "Classes") == "PaneRow"
            && element.Descendants().Any(child => AttributeValue(child, "Classes") == "Risk"));
        row.Ancestors().Should().Contain(element => AttributeValue(element, "Classes") == "Pane");
        row.Descendants().Should().Contain(element => AttributeValue(element, "Classes") == "Risk");
        row.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => AttributeValue(element, "Classes"))
            .Should().Equal("SecondaryAction Sm", "GhostAction Sm", "SecondaryAction Sm", "PrimaryAction Sm");

        // Command mode's skill previews wait here too, now that the Activity panel is gone.
        inbox.Descendants().Should().Contain(element =>
            element.Name.LocalName == "ItemsControl"
            && AttributeValue(element, "ItemsSource") == "{Binding PendingSkillConfirmations}");
    }

    [Theory]
    [InlineData(1440, true, false, false)]
    [InlineData(1280, true, false, false)]
    [InlineData(1279, false, false, false)]
    [InlineData(979, false, true, false)]
    [InlineData(759, false, true, true)]
    public void Breakpoints_FollowKamStudio(double width, bool contextPane, bool narrow, bool railCompact)
    {
        // A standalone manager, so this test never races the tests that resize the shared instance.
        var manager = (WindowStateManager)Activator.CreateInstance(typeof(WindowStateManager), nonPublic: true)!;
        typeof(WindowStateManager)
            .GetProperty("WindowWidth", BindingFlags.Instance | BindingFlags.Public)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(manager, [width]);

        manager.IsLogPanelVisible.Should().Be(contextPane);
        manager.IsNarrow.Should().Be(narrow);
        manager.IsRailCompact.Should().Be(railCompact);
    }

    private static Dictionary<string, string> ReadColors(string fileName)
    {
        return XDocument.Load(FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Themes", fileName)).Root!
            .Elements()
            .Where(element => element.Name.LocalName == "Color")
            .ToDictionary(
                element => element.Attributes().Single(attribute => attribute.Name.LocalName == "Key").Value,
                element => element.Value.Trim());
    }

    private static XElement LoadXaml(string viewFileName) =>
        XDocument.Parse(LocalizedXaml.ReadAllText(FindView(viewFileName))).Root!;

    private static string FindView(string viewFileName) =>
        FindProjectFilePath("src", "Ui", "SmartVoiceAgent.Ui", "Views", viewFileName);

    private static string? AttributeValue(XElement element, string attributeName)
    {
        return element
            .Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == attributeName)
            ?.Value;
    }

    private static string FindProjectFilePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {Path.Combine(segments)}.");
    }
}

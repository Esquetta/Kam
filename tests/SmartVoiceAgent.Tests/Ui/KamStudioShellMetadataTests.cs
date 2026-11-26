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

        dark["PanelBg"].Should().Be("#0D0D12");
        dark["CardBg"].Should().Be("#13131A");
        dark["RaisedBg"].Should().Be("#1A1A23");
        dark["CardBgHover"].Should().Be("#20202B");
        dark["BorderSubtle"].Should().Be("#262632");
        dark["BorderMuted"].Should().Be("#363645");
        dark["TextPrimary"].Should().Be("#F2F1F7");
        dark["Accent"].Should().Be("#8B7CFF");
        dark["AccentStrong"].Should().Be("#6E5BF0");
        dark["AccentInfo"].Should().Be("#60A5FA");

        light["PanelBg"].Should().Be("#F5F4F9");
        light["CardBg"].Should().Be("#FFFFFF");
        light["RaisedBg"].Should().Be("#F0EFF6");
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
            "Border.Explain",
            "Border.Code",
            "Border.Kbd",
            "ToggleSwitch:checked /template/ Grid#PART_MovingKnobs"
        });
    }

    [Fact]
    public void Rail_LadderStepsOpenTheirPagesAndSayHowMuchIsBuilt()
    {
        var mainWindow = LoadXaml("MainWindow.axaml");

        var steps = mainWindow.Descendants()
            .Where(element => element.Name.LocalName == "Button" && AttributeValue(element, "Classes.LadderStep") == "True")
            .ToArray();

        steps.Select(step => AttributeValue(step, "AutomationProperties.Name")).Should().Equal(
            "Prompts", "Agents", "Loops", "Graphs", "Agent Builder");
        steps.Select(step => AttributeValue(step, "CommandParameter")).Should().Equal(
            "{x:Static vm:NavView.Prompts}", "{x:Static vm:NavView.Agents}", "{x:Static vm:NavView.Loops}",
            "{x:Static vm:NavView.Graphs}", "{x:Static vm:NavView.Builder}");
        steps.Select(step => step.Descendants().Single(element => AttributeValue(element, "Classes")?.StartsWith("LadderBadge", StringComparison.Ordinal) == true))
            .Select(badge => AttributeValue(badge, "Classes"))
            .Should().Equal(
                "LadderBadge Partial", "LadderBadge Partial", "LadderBadge Partial",
                "LadderBadge Partial", "LadderBadge Planned");
        steps.Should().OnlyContain(step => AttributeValue(step, "Command") == "{Binding NavigateToPageCommand}"
            && AttributeValue(step, "ToolTip.Tip") != null
            && AttributeValue(step, "AutomationProperties.HelpText") != null);

        // The spend meter has no data behind it yet: it shows the planned badge, no number, and opens Runs.
        var spend = mainWindow.Descendants().Single(element => AttributeValue(element, "Classes") == "SpendCard");
        spend.Name.LocalName.Should().Be("Button");
        AttributeValue(spend, "CommandParameter").Should().Be("{x:Static vm:NavView.Runs}");
        spend.Descendants().Should().Contain(element => AttributeValue(element, "Classes") == "LadderBadge Planned");
        spend.Descendants().Should().NotContain(element => element.Name.LocalName == "ProgressBar");
        AttributeValue(spend, "AutomationProperties.HelpText").Should().NotBeNullOrEmpty();

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

        // Tool steps carry a risk badge, and the composer ends with a Send button and key hints.
        tasks.Descendants().Should().Contain(element =>
            AttributeValue(element, "Classes") == "Risk"
            && AttributeValue(element, "Classes.Shell") == "{Binding IsShellRisk}");
        File.ReadAllText(FindView("TasksView.axaml")).Should().Contain("{DynamicResource Lang.Chat.Composer.SendLabel}");
        tasks.Descendants().Count(element => AttributeValue(element, "Classes") == "Kbd").Should().Be(3);

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
    public void NeedsYou_ListsApprovalsAsRowsInOnePanel()
    {
        var inbox = LoadXaml("InboxView.axaml");

        var row = inbox.Descendants().Single(element => AttributeValue(element, "Classes") == "PaneRow");
        row.Ancestors().Should().Contain(element => AttributeValue(element, "Classes") == "Pane");
        row.Descendants().Should().Contain(element => AttributeValue(element, "Classes") == "Risk");
        row.Descendants()
            .Where(element => element.Name.LocalName == "Button")
            .Select(element => AttributeValue(element, "Classes"))
            .Should().Equal("SecondaryAction Sm", "GhostAction Sm", "SecondaryAction Sm", "PrimaryAction Sm");
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

using System.Xml.Linq;
using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class LoopsAndPaletteMetadataTests
{
    [Fact]
    public void LoopsView_LimitBoxesSaveOnLostFocusNotOnEveryKeystroke()
    {
        var root = XDocument.Load(FindViewPath("LoopsView.axaml")).Root!;

        var boxes = root.Descendants()
            .Where(element => element.Name.LocalName == "TextBox" && (string?)element.Attribute("Classes") == "WithUnit")
            .ToArray();

        boxes.Should().HaveCount(3);
        boxes.Should().OnlyContain(box =>
            ((string?)box.Attribute("Text"))!.Contains("UpdateSourceTrigger=LostFocus", StringComparison.Ordinal)
            && (string?)box.Attribute("KeyDown") == "OnLimitKeyDown");
    }

    [Fact]
    public void LoopsView_IsOnePaneOfSettingRowsWithTheUnitInsideTheInput()
    {
        var root = XDocument.Parse(LocalizedXaml.ReadAllText(FindViewPath("LoopsView.axaml"))).Root!;

        // A section title with a ghost reset button, then one Pane of setting rows.
        var title = root.Descendants().Single(element => (string?)element.Attribute("Classes") == "SectionTitle");
        ((string?)title.Attribute("Text")).Should().Be("Limits");
        var reset = root.Descendants().Single(element => (string?)element.Attribute("Command") == "{Binding ResetCommand}");
        ((string?)reset.Attribute("Classes")).Should().Be("GhostAction Sm");
        ((string?)reset.Attribute("Content")).Should().Be("Reset to defaults");
        root.Descendants().Where(element => (string?)element.Attribute("Classes") == "Pane").Should().ContainSingle();

        var rows = root.Descendants().Where(element => (string?)element.Attribute("Classes") == "SettingRow").ToArray();
        rows.Should().HaveCount(3);
        rows.Select(row => row.Descendants().Single(element => (string?)element.Attribute("Classes") == "Secondary"))
            .Select(help => (string?)help.Attribute("Text"))
            .Should().Equal("1–200 · default 24", "1,000–200,000 · default 16,000", "0 = off · 8,000–1,000,000 · default 64,000");
        rows.Select(row => row.Descendants().Single(element => (string?)element.Attribute("Classes") == "UnitSuffix"))
            .Select(unit => (string?)unit.Attribute("Text"))
            .Should().Equal("steps", "chars", "tokens");
        rows.Should().OnlyContain(row => row.Descendants().Single(element => (string?)element.Attribute("Classes") == "UnitSuffix")
            .Parent!.Name.LocalName == "TextBox.InnerRightContent");
    }

    [Fact]
    public void Palette_WithNoQuery_KeepsEveryGroupVisibleBehindALongTaskList()
    {
        var palette = new CommandPaletteViewModel();
        var items = new List<PaletteItem>
        {
            new("Pages", "Settings", null, null, "IconSliders", () => { })
        };
        items.AddRange(Enumerable.Range(1, 60).Select(i => new PaletteItem("Tasks", $"Task {i}", null, null, "IconChat", () => { })));
        items.Add(new PaletteItem("Commands", "/review", "Review the diff", null, "IconTerminal", () => { }));

        palette.Open(items);

        palette.Results.Where(row => row.IsHeader).Select(row => row.Header).Should().Equal("Pages", "Tasks", "Commands");
        palette.Results.Should().Contain(row => row.Item != null && row.Item.Title == "/review");
    }

    private static string FindViewPath(string view)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Ui", "SmartVoiceAgent.Ui", "Views", view);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(view);
    }
}

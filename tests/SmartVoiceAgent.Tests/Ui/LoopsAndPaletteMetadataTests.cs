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
            .Where(element => element.Name.LocalName == "TextBox" && (string?)element.Attribute("Classes") == "LimitBox")
            .ToArray();

        boxes.Should().HaveCount(3);
        boxes.Should().OnlyContain(box =>
            ((string?)box.Attribute("Text"))!.Contains("UpdateSourceTrigger=LostFocus", StringComparison.Ordinal)
            && (string?)box.Attribute("KeyDown") == "OnLimitKeyDown");
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

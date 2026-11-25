using FluentAssertions;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class CommandPaletteViewModelTests
{
    private static PaletteItem Item(string group, string title, string? detail = null, Action? run = null) =>
        new(group, title, detail, null, "IconSearch", run ?? (() => { }));

    private static List<PaletteItem> Sample() =>
    [
        Item("Pages", "Open settings"),
        Item("Pages", "Skills", "Built-in abilities"),
        Item("Commands", "Settings reset"),
        Item("Commands", "İstatistikler"),
    ];

    private static string[] Titles(CommandPaletteViewModel vm) =>
        vm.Results.Where(r => r.IsItem).Select(r => r.Item!.Title).ToArray();

    [Fact]
    public void Open_ShowsEverythingWithHeadersAndSelectsFirstEntry()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());

        vm.IsOpen.Should().BeTrue();
        vm.Results.Select(r => r.IsHeader).Should().Equal(true, false, false, true, false, false);
        vm.SelectedItem!.Title.Should().Be("Open settings");
        vm.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void Query_PutsTitlePrefixFirstAndKeepsGroupOrder()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());

        vm.Query = "settings";
        Titles(vm).Should().Equal("Open settings", "Settings reset");

        vm.Query = "set";
        Titles(vm).Should().Equal("Open settings", "Settings reset");

        vm.Results[0].Header.Should().Be("Pages");
    }

    [Fact]
    public void Query_PrefixMatchBeatsContainsWithinGroup()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open([Item("A", "Reopen"), Item("A", "Open")]);

        vm.Query = "open";

        Titles(vm).Should().Equal("Open", "Reopen");
    }

    [Fact]
    public void Query_MatchesDetail()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());

        vm.Query = "abilities";

        Titles(vm).Should().Equal("Skills");
    }

    [Fact]
    public void Query_FoldsTurkishLettersAndCase()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());

        vm.Query = "istatistik";
        Titles(vm).Should().Equal("İstatistikler");

        vm.Query = "ISTATİSTİK";
        Titles(vm).Should().Equal("İstatistikler");

        vm.Query = "ıstatıstık";
        Titles(vm).Should().Equal("İstatistikler");
    }

    [Fact]
    public void Query_WithNoMatchLeavesEmptyList()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());

        vm.Query = "zzz";

        vm.Results.Should().BeEmpty();
        vm.IsEmpty.Should().BeTrue();
        vm.SelectedItem.Should().BeNull();
        vm.Invoking(v => v.MoveSelection(1)).Should().NotThrow();
    }

    [Fact]
    public void Results_AreCappedAtFiftyEntries()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Enumerable.Range(0, 80).Select(i => Item("G", $"Item {i}")).ToList());

        Titles(vm).Should().HaveCount(50);
    }

    [Fact]
    public void MoveSelection_WrapsAndSkipsHeaders()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());

        vm.MoveSelection(1);
        vm.SelectedItem!.Title.Should().Be("Skills");

        vm.MoveSelection(1);
        vm.SelectedItem!.Title.Should().Be("Settings reset");
        vm.Results[vm.SelectedIndex].IsHeader.Should().BeFalse();
        vm.Results[vm.SelectedIndex].IsSelected.Should().BeTrue();

        vm.MoveSelection(1);
        vm.MoveSelection(1);
        vm.SelectedItem!.Title.Should().Be("Open settings");

        vm.MoveSelection(-1);
        vm.SelectedItem!.Title.Should().Be("İstatistikler");
    }

    [Fact]
    public void ExecuteSelected_ClosesThenRunsItem()
    {
        var vm = new CommandPaletteViewModel();
        bool? openWhenRun = null;
        vm.Open([Item("A", "Go", run: () => openWhenRun = vm.IsOpen)]);

        vm.ExecuteSelected();

        openWhenRun.Should().BeFalse();
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void ExecuteCommand_RunsGivenItem()
    {
        var vm = new CommandPaletteViewModel();
        var ran = 0;
        var item = Item("A", "Go", run: () => ran++);
        vm.Open([item]);

        vm.ExecuteCommand(item);

        ran.Should().Be(1);
        vm.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Open_ResetsQuery()
    {
        var vm = new CommandPaletteViewModel();
        vm.Open(Sample());
        vm.Query = "set";
        vm.Close();

        vm.Open(Sample());

        vm.Query.Should().BeEmpty();
        Titles(vm).Should().HaveCount(4);
    }
}

using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace SmartVoiceAgent.Ui.ViewModels;

/// <summary>One entry in the command palette.</summary>
/// <param name="Group">Group header the entry is listed under.</param>
/// <param name="Title">Main text.</param>
/// <param name="Detail">Optional muted text after the title.</param>
/// <param name="Shortcut">Optional shortcut shown in a key chip.</param>
/// <param name="IconKey">Icon resource key such as <c>IconSearch</c>.</param>
/// <param name="Run">Action run when the entry is chosen.</param>
public sealed record PaletteItem(string Group, string Title, string? Detail, string? Shortcut, string IconKey, Action Run);

/// <summary>A row in the palette list: either a group header or an entry.</summary>
public sealed class PaletteRow : ReactiveObject
{
    private bool _isSelected;

    internal PaletteRow(string? header, PaletteItem? item)
    {
        Header = header;
        Item = item;
    }

    /// <summary>Group name when this row is a header; otherwise <c>null</c>.</summary>
    public string? Header { get; }

    /// <summary>The entry when this row is not a header.</summary>
    public PaletteItem? Item { get; }

    /// <summary>True for a group header row.</summary>
    public bool IsHeader => Item is null;

    /// <summary>True for an entry row.</summary>
    public bool IsItem => Item is not null;

    /// <summary>Header text, upper-cased for display.</summary>
    public string HeaderText => Header?.ToUpperInvariant() ?? string.Empty;

    /// <summary>True when the entry has detail text.</summary>
    public bool HasDetail => !string.IsNullOrEmpty(Item?.Detail);

    /// <summary>True when the entry has a shortcut.</summary>
    public bool HasShortcut => !string.IsNullOrEmpty(Item?.Shortcut);

    /// <summary>True when the keyboard selection is on this row.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

/// <summary>State of the Ctrl+K command palette: items, filter and keyboard selection.</summary>
public sealed class CommandPaletteViewModel : ReactiveObject
{
    private const int MaxRows = 50;

    private IReadOnlyList<PaletteItem> _items = [];
    private bool _isOpen;
    private string _query = string.Empty;
    private int _selectedIndex = -1;

    /// <summary>Rows shown: group headers followed by their entries.</summary>
    public ObservableCollection<PaletteRow> Results { get; } = [];

    /// <summary>True while the palette is on screen.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => this.RaiseAndSetIfChanged(ref _isOpen, value);
    }

    /// <summary>Search text; changing it refilters the list.</summary>
    public string Query
    {
        get => _query;
        set
        {
            value ??= string.Empty;
            if (value == _query)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _query, value);
            Refilter();
        }
    }

    /// <summary>Index into <see cref="Results"/> of the selected entry row, or -1.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value == _selectedIndex)
            {
                return;
            }

            if (_selectedIndex >= 0 && _selectedIndex < Results.Count)
            {
                Results[_selectedIndex].IsSelected = false;
            }

            this.RaiseAndSetIfChanged(ref _selectedIndex, value);
            if (value >= 0 && value < Results.Count)
            {
                Results[value].IsSelected = true;
            }

            this.RaisePropertyChanged(nameof(SelectedItem));
        }
    }

    /// <summary>The selected entry, or <c>null</c> when nothing matches.</summary>
    public PaletteItem? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < Results.Count ? Results[_selectedIndex].Item : null;

    /// <summary>True when the filter leaves no entries.</summary>
    public bool IsEmpty => Results.Count == 0;

    /// <summary>Opens the palette over the given entries with an empty query.</summary>
    /// <param name="items">Entries, listed in group order of first appearance.</param>
    public void Open(IReadOnlyList<PaletteItem> items)
    {
        _items = items;
        _query = string.Empty;
        this.RaisePropertyChanged(nameof(Query));
        Refilter();
        IsOpen = true;
    }

    /// <summary>Closes the palette.</summary>
    public void Close() => IsOpen = false;

    /// <summary>Moves the selection by <paramref name="delta"/> entries, skipping headers and wrapping.</summary>
    /// <param name="delta">Positive for down, negative for up.</param>
    public void MoveSelection(int delta)
    {
        var entries = Enumerable.Range(0, Results.Count).Where(i => Results[i].IsItem).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var position = entries.IndexOf(_selectedIndex);
        var next = position < 0
            ? (delta >= 0 ? 0 : entries.Count - 1)
            : (((position + delta) % entries.Count) + entries.Count) % entries.Count;
        SelectedIndex = entries[next];
    }

    /// <summary>Closes the palette, then runs the selected entry.</summary>
    public void ExecuteSelected()
    {
        if (SelectedItem is { } item)
        {
            ExecuteCommand(item);
        }
    }

    /// <summary>Closes the palette, then runs <paramref name="item"/>.</summary>
    /// <param name="item">The entry to run.</param>
    public void ExecuteCommand(PaletteItem item)
    {
        Close();
        item.Run();
    }

    private void Refilter()
    {
        var needle = Fold(_query.Trim());
        var matches = new List<(PaletteItem Item, int Rank, int Order)>();
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var title = Fold(item.Title);
            if (needle.Length == 0 || title.StartsWith(needle, StringComparison.Ordinal))
            {
                matches.Add((item, 0, i));
            }
            else if (title.Contains(needle, StringComparison.Ordinal)
                || (item.Detail is not null && Fold(item.Detail).Contains(needle, StringComparison.Ordinal)))
            {
                matches.Add((item, 1, i));
            }
        }

        var groupOrder = _items.Select(i => i.Group).Distinct().ToList();
        // With no query every group gets a share of the rows, so a long task list can't hide the groups after it.
        var perGroup = needle.Length == 0 ? Math.Max(1, MaxRows / Math.Max(1, groupOrder.Count)) : MaxRows;
        var ordered = matches
            .GroupBy(m => m.Item.Group)
            .OrderBy(g => groupOrder.IndexOf(g.Key))
            .SelectMany(g => g.OrderBy(m => m.Rank).ThenBy(m => m.Order).Take(perGroup))
            .Take(MaxRows)
            .ToList();

        _selectedIndex = -1;
        Results.Clear();
        string? lastGroup = null;
        foreach (var (item, _, _) in ordered)
        {
            if (item.Group != lastGroup)
            {
                Results.Add(new PaletteRow(item.Group, null));
                lastGroup = item.Group;
            }

            Results.Add(new PaletteRow(null, item));
        }

        this.RaisePropertyChanged(nameof(IsEmpty));
        MoveSelection(1);
        this.RaisePropertyChanged(nameof(SelectedIndex));
        this.RaisePropertyChanged(nameof(SelectedItem));
    }

    // Lower-cases with Turkish dotted/dotless i folded to i, and strips diacritics.
    private static string Fold(string text)
    {
        var decomposed = text.Replace('İ', 'i').Replace('ı', 'i').ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}

using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels;

/// <summary>
/// The file-system and shell calls the Prompts page makes, so tests can replace them.
/// </summary>
/// <param name="ReadHead">Reads at most the given number of characters from the start of a file.</param>
/// <param name="FileExists">Tells whether a file exists.</param>
/// <param name="WriteNewFile">Creates the folder when needed and writes a new file.</param>
/// <param name="CreateDirectory">Creates a folder when it is missing.</param>
/// <param name="Open">Opens a file or folder with the operating system.</param>
public sealed record PromptsFileHooks(
    Func<string, int, string> ReadHead,
    Func<string, bool> FileExists,
    Action<string, string> WriteNewFile,
    Action<string> CreateDirectory,
    Action<string> Open)
{
    /// <summary>Gets the hooks that touch the real disk and shell.</summary>
    public static PromptsFileHooks Default { get; } = new(
        (path, max) =>
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[max];
            var read = reader.ReadBlock(buffer, 0, max);
            return new string(buffer, 0, read);
        },
        File.Exists,
        (path, text) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(text);
        },
        path => Directory.CreateDirectory(path),
        path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }));
}

/// <summary>
/// A prompt in the library: one Markdown slash command.
/// </summary>
public sealed class PromptItemViewModel : ReactiveObject
{
    private bool _isSelected;

    internal PromptItemViewModel(AgentCommandInfo info, string sourceLabel, Action<PromptItemViewModel> select)
    {
        Info = info;
        SourceLabel = sourceLabel;
        SelectCommand = ReactiveCommand.Create(() => select(this));
    }

    /// <summary>Gets the command the catalog found.</summary>
    public AgentCommandInfo Info { get; }

    /// <summary>Gets the name with its slash, such as <c>/review</c>.</summary>
    public string Slash => "/" + Info.Name;

    /// <summary>Gets the one-line description.</summary>
    public string Description => Info.Description;

    /// <summary>Gets whether there is a description to show.</summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Info.Description);

    /// <summary>Gets the argument hint, such as <c>[file]</c>.</summary>
    public string ArgumentHint => Info.ArgumentHint;

    /// <summary>Gets whether the prompt has an argument hint.</summary>
    public bool HasArgumentHint => !string.IsNullOrWhiteSpace(Info.ArgumentHint);

    /// <summary>Gets the path of the Markdown file.</summary>
    public string FilePath => Info.FilePath;

    /// <summary>Gets the readable source: Yours, This workspace or the plugin name.</summary>
    public string SourceLabel { get; }

    /// <summary>Gets or sets whether this is the prompt shown on the right.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>Gets the command that selects this prompt.</summary>
    public ReactiveCommand<Unit, Unit> SelectCommand { get; }
}

/// <summary>
/// A group of prompts that share a source.
/// </summary>
/// <param name="Title">The group heading.</param>
/// <param name="Items">The prompts in the group.</param>
public sealed record PromptGroupViewModel(string Title, IReadOnlyList<PromptItemViewModel> Items);

/// <summary>
/// The Prompts page (Build step 1): the Markdown slash commands as a prompt library with search, a preview
/// and shortcuts to use, open and create prompts.
/// </summary>
public sealed class PromptsViewModel : ViewModelBase, IDisposable
{
    private const int PreviewLimit = 4000;
    private const int ReadLimit = PreviewLimit * 2;

    private readonly IAgentCommandCatalog? _commands;
    private readonly Action<string> _useInNewTask;
    private readonly PromptsFileHooks _files;
    private List<PromptItemViewModel> _all = [];
    private string _searchText = string.Empty;
    private PromptItemViewModel? _selected;
    private string _preview = string.Empty;
    private string _message = string.Empty;
    private readonly System.Reactive.Subjects.BehaviorSubject<bool> _hasSelection = new(false);

    /// <summary>
    /// Creates the page model and loads the list.
    /// </summary>
    /// <param name="commands">The command catalog, or <c>null</c> when the library is unavailable.</param>
    /// <param name="useInNewTask">Starts a new task with the given text in the composer.</param>
    public PromptsViewModel(IAgentCommandCatalog? commands, Action<string> useInNewTask)
        : this(commands, useInNewTask, PromptsFileHooks.Default)
    {
    }

    /// <summary>
    /// Creates the page model with replaceable file-system hooks.
    /// </summary>
    /// <param name="commands">The command catalog, or <c>null</c> when the library is unavailable.</param>
    /// <param name="useInNewTask">Starts a new task with the given text in the composer.</param>
    /// <param name="files">The file-system and shell calls.</param>
    public PromptsViewModel(IAgentCommandCatalog? commands, Action<string> useInNewTask, PromptsFileHooks files)
    {
        _commands = commands;
        _useInNewTask = useInNewTask ?? throw new ArgumentNullException(nameof(useInNewTask));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        Title = Loc.Get("Prompts.Title");

        var hasLibrary = Observable.Return(commands is not null);
        var hasSelection = _hasSelection.AsObservable();
        UseInNewTaskCommand = ReactiveCommand.Create(UseInNewTask, hasSelection);
        OpenFileCommand = ReactiveCommand.Create(OpenFile, hasSelection);
        NewPromptCommand = ReactiveCommand.Create(NewPrompt, hasLibrary);
        OpenFolderCommand = ReactiveCommand.Create(OpenFolder, hasLibrary);

        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    /// <summary>Gets the groups shown in the list, after the search.</summary>
    public ObservableCollection<PromptGroupViewModel> Groups { get; } = [];

    /// <summary>Gets or sets the text the list is filtered by.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _searchText, value);
            Rebuild();
        }
    }

    /// <summary>Gets the prompt shown on the right.</summary>
    public PromptItemViewModel? SelectedPrompt
    {
        get => _selected;
        private set => this.RaiseAndSetIfChanged(ref _selected, value);
    }

    /// <summary>Gets whether a prompt is selected.</summary>
    public bool HasSelection => _selected is not null;

    /// <summary>Gets the body of the selected file, or the reason it cannot be read.</summary>
    public string PreviewText
    {
        get => _preview;
        private set => this.RaiseAndSetIfChanged(ref _preview, value);
    }

    /// <summary>Gets the result of the last open or create action when it failed.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            this.RaiseAndSetIfChanged(ref _message, value);
            this.RaisePropertyChanged(nameof(HasMessage));
        }
    }

    /// <summary>Gets whether <see cref="Message"/> has text.</summary>
    public bool HasMessage => _message.Length > 0;

    /// <summary>Gets whether there is no catalog behind the page.</summary>
    public bool IsUnavailable => _commands is null;

    /// <summary>Gets whether the library has no prompts at all.</summary>
    public bool IsEmpty => _commands is not null && _all.Count == 0;

    /// <summary>Gets whether prompts exist and the page shows the two panes.</summary>
    public bool HasPrompts => _all.Count > 0;

    /// <summary>Gets whether the search hides every prompt.</summary>
    public bool HasNoMatches => _all.Count > 0 && Groups.Count == 0;

    /// <summary>Gets the folder where the user's prompts live.</summary>
    public string UserDirectory => _commands?.UserCommandsDirectory ?? string.Empty;

    /// <summary>Gets the command that starts a new task with <c>/name </c> typed in.</summary>
    public ReactiveCommand<Unit, Unit> UseInNewTaskCommand { get; }

    /// <summary>Gets the command that opens the selected file.</summary>
    public ReactiveCommand<Unit, Unit> OpenFileCommand { get; }

    /// <summary>Gets the command that creates a prompt file and opens it.</summary>
    public ReactiveCommand<Unit, Unit> NewPromptCommand { get; }

    /// <summary>Gets the command that opens the user's prompts folder.</summary>
    public ReactiveCommand<Unit, Unit> OpenFolderCommand { get; }

    /// <inheritdoc />
    public override void OnNavigatedTo() => Refresh();

    /// <summary>
    /// Reads the catalog again and keeps the selection when the prompt is still there.
    /// </summary>
    public void Refresh()
    {
        (string Name, string Source)? keep = _selected is null ? null : (_selected.Info.Name, _selected.Info.Source);
        IReadOnlyList<AgentCommandInfo> commands;
        try
        {
            commands = _commands?.GetCommands() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            commands = [];
        }

        _all = commands
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(c => new PromptItemViewModel(c, SourceLabelOf(c.Source), Select))
            .ToList();
        Rebuild();

        var match = keep is null
            ? null
            : _all.FirstOrDefault(p => p.Info.Name == keep.Value.Name && p.Info.Source == keep.Value.Source);
        Select(match ?? Groups.SelectMany(g => g.Items).FirstOrDefault());
        RaiseStateChanged();
    }

    /// <inheritdoc />
    public void Dispose() => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = Loc.Get("Prompts.Title");
        Refresh();
    }

    private IEnumerable<PromptItemViewModel> VisibleItems()
    {
        var query = _searchText.Trim();
        return query.Length == 0
            ? _all
            : _all.Where(p => p.Info.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                              || p.Info.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Rebuild()
    {
        Groups.Clear();
        foreach (var group in VisibleItems()
                     .GroupBy(p => p.Info.Source)
                     .OrderBy(g => SourceRank(g.Key))
                     .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToList();
            Groups.Add(new PromptGroupViewModel(items[0].SourceLabel, items));
        }

        this.RaisePropertyChanged(nameof(HasNoMatches));
    }

    private void RaiseStateChanged()
    {
        this.RaisePropertyChanged(nameof(IsEmpty));
        this.RaisePropertyChanged(nameof(HasPrompts));
        this.RaisePropertyChanged(nameof(HasNoMatches));
        this.RaisePropertyChanged(nameof(UserDirectory));
    }

    private void Select(PromptItemViewModel? item)
    {
        if (_selected is not null)
        {
            _selected.IsSelected = false;
        }

        SelectedPrompt = item;
        if (item is not null)
        {
            item.IsSelected = true;
        }

        _hasSelection.OnNext(item is not null);
        this.RaisePropertyChanged(nameof(HasSelection));
        Message = string.Empty;
        PreviewText = item is null ? string.Empty : LoadPreview(item.FilePath);
    }

    private string LoadPreview(string path)
    {
        try
        {
            var body = StripFrontMatter(_files.ReadHead(path, ReadLimit)).Trim();
            if (body.Length == 0)
            {
                return Loc.Get("Prompts.Preview.Empty");
            }

            return body.Length > PreviewLimit ? body[..PreviewLimit].TrimEnd() + "\n…" : body;
        }
        catch (Exception ex)
        {
            return Loc.Format("Prompts.Preview.Failed", ex.Message);
        }
    }

    /// <summary>
    /// Removes the YAML front matter block from the start of a Markdown file.
    /// </summary>
    /// <param name="text">The file text.</param>
    /// <returns>The text after the closing <c>---</c> line, or the text itself when there is no front matter.</returns>
    public static string StripFrontMatter(string text)
    {
        var content = text.TrimStart('﻿');
        if (!content.StartsWith("---", StringComparison.Ordinal))
        {
            return content;
        }

        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines[0].TrimEnd() != "---")
        {
            return content;
        }

        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == "---")
            {
                return string.Join('\n', lines.Skip(i + 1));
            }
        }

        return content;
    }

    private void UseInNewTask()
    {
        if (_selected is not null)
        {
            _useInNewTask("/" + _selected.Info.Name + " ");
        }
    }

    private void OpenFile()
    {
        if (_selected is not null)
        {
            OpenPath(_selected.FilePath);
        }
    }

    private void OpenFolder()
    {
        var folder = _commands?.UserCommandsDirectory;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        try
        {
            _files.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            Message = Loc.Format("Prompts.Message.OpenFailed", ex.Message);
            return;
        }

        OpenPath(folder);
    }

    private void NewPrompt()
    {
        var folder = _commands?.UserCommandsDirectory;
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        string path;
        try
        {
            path = Path.Combine(folder, "new-prompt.md");
            for (var n = 2; _files.FileExists(path); n++)
            {
                path = Path.Combine(folder, $"new-prompt-{n}.md");
            }

            _files.WriteNewFile(path, Template);
        }
        catch (Exception ex)
        {
            Message = Loc.Format("Prompts.Message.CreateFailed", ex.Message);
            return;
        }

        SearchText = string.Empty;
        Refresh();
        var created = _all.FirstOrDefault(p => string.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (created is not null)
        {
            Select(created);
        }

        OpenPath(path);
    }

    private void OpenPath(string path)
    {
        try
        {
            _files.Open(path);
            Message = string.Empty;
        }
        catch (Exception ex)
        {
            Message = Loc.Format("Prompts.Message.OpenFailed", ex.Message);
        }
    }

    private const string Template =
        "---\n" +
        "description: Describe what this prompt does\n" +
        "---\n" +
        "\n" +
        "Write the instructions here. $ARGUMENTS is replaced by whatever you type after the command.\n";

    private static int SourceRank(string source) => source switch
    {
        "user" => 0,
        "workspace" => 1,
        _ => 2
    };

    private static string SourceLabelOf(string source) => source switch
    {
        "user" => Loc.Get("Prompts.Source.User"),
        "workspace" => Loc.Get("Prompts.Source.Workspace"),
        _ when source.StartsWith("plugin:", StringComparison.Ordinal) => source["plugin:".Length..],
        _ => source
    };
}

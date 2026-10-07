using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// Which kind of extension the Installed tab lists.
    /// </summary>
    public enum ExtensionsFilter
    {
        /// <summary>Every kind.</summary>
        All,

        /// <summary>MCP servers.</summary>
        McpServers,

        /// <summary>Plugins.</summary>
        Plugins,

        /// <summary>Agent Skills.</summary>
        Skills,

        /// <summary>Markdown commands.</summary>
        Commands
    }

    /// <summary>
    /// The Extensions page: what is installed (MCP servers, plugins, Agent Skills and Markdown commands) and a
    /// Discover tab that adds well-known servers and plugin repositories in one click.
    /// </summary>
    public sealed class ExtensionsViewModel : ViewModelBase, IDisposable
    {
        private readonly IMcpHost? _mcpHost;
        private readonly IAgentPluginCatalog? _plugins;
        private readonly IAgentSkillCatalog? _skills;
        private readonly IAgentCommandCatalog? _commands;
        private readonly Action<string> _openPath;
        private readonly Action<Action> _post;
        private readonly Func<ExtensionRuntime, bool> _isRuntimeAvailable;
        private string _pluginSource = string.Empty;
        private string _skillSource = string.Empty;
        private string _searchText = string.Empty;
        private string _message = string.Empty;
        private Func<string>? _messageSource;
        private ExtensionsFilter _filter;
        private bool _isDiscoverTab;
        private bool _isBusy;

        /// <summary>
        /// Creates the page.
        /// </summary>
        /// <param name="mcpHost">The MCP host.</param>
        /// <param name="plugins">Installed plugins.</param>
        /// <param name="skills">Agent Skills.</param>
        /// <param name="commands">Markdown commands.</param>
        /// <param name="openPath">Opens a file, folder or web page in the system shell; tests replace it.</param>
        /// <param name="post">Runs an action on the UI thread; tests replace it.</param>
        /// <param name="isRuntimeAvailable">Whether Node.js, uv or git is installed; tests replace it.</param>
        public ExtensionsViewModel(
            IMcpHost? mcpHost,
            IAgentPluginCatalog? plugins,
            IAgentSkillCatalog? skills,
            IAgentCommandCatalog? commands,
            Action<string>? openPath = null,
            Action<Action>? post = null,
            Func<ExtensionRuntime, bool>? isRuntimeAvailable = null)
        {
            Title = Loc.Get("Extensions.Title");
            _mcpHost = mcpHost;
            _plugins = plugins;
            _skills = skills;
            _commands = commands;
            _openPath = openPath ?? OpenWithShell;
            _post = post ?? (action => Dispatcher.UIThread.Post(action));
            _isRuntimeAvailable = isRuntimeAvailable ?? ExtensionCatalog.IsAvailable;

            RefreshCommand = ReactiveCommand.Create(Refresh);
            ConnectServersCommand = ReactiveCommand.CreateFromTask(ConnectServersAsync);
            OpenMcpConfigCommand = ReactiveCommand.Create(OpenMcpConfig);
            InstallPluginCommand = ReactiveCommand.CreateFromTask(InstallPluginAsync);
            InstallSkillCommand = ReactiveCommand.Create(InstallSkill);
            BrowseSkillFolderCommand = ReactiveCommand.CreateFromTask(BrowseSkillFolderAsync);
            OpenPluginsFolderCommand = ReactiveCommand.Create(() => OpenFolder(_plugins?.PluginsDirectory));
            OpenSkillsFolderCommand = ReactiveCommand.Create(() => OpenFolder(_skills?.UserSkillsDirectory));
            OpenCommandsFolderCommand = ReactiveCommand.Create(() => OpenFolder(_commands?.UserCommandsDirectory));
            ShowInstalledCommand = ReactiveCommand.Create(() => { IsDiscoverTab = false; });
            ShowDiscoverCommand = ReactiveCommand.Create(() => { IsDiscoverTab = true; });
            SetFilterCommand = ReactiveCommand.Create<string>(value =>
            {
                if (Enum.TryParse<ExtensionsFilter>(value, out var filter))
                {
                    Filter = filter;
                }
            });

            foreach (var entry in ExtensionCatalog.McpServers)
            {
                CatalogServers.Add(new McpCatalogItemViewModel(entry, _isRuntimeAvailable(entry.Runtime), item => _ = AddCatalogServerAsync(item), _openPath));
            }

            foreach (var entry in ExtensionCatalog.Plugins)
            {
                CatalogPlugins.Add(new PluginCatalogItemViewModel(entry, _isRuntimeAvailable(ExtensionRuntime.Git), item => _ = InstallCatalogPluginAsync(item), _openPath));
            }

            if (_mcpHost is not null)
            {
                _mcpHost.StateChanged += OnMcpStateChanged;
            }

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Refresh();
        }

        /// <summary>Gets the MCP servers.</summary>
        public ObservableCollection<McpServerItemViewModel> McpServers { get; } = new();

        /// <summary>Gets the installed plugins.</summary>
        public ObservableCollection<ExtensionPluginItemViewModel> Plugins { get; } = new();

        /// <summary>Gets the Agent Skills.</summary>
        public ObservableCollection<AgentSkillItemViewModel> Skills { get; } = new();

        /// <summary>Gets the Markdown commands.</summary>
        public ObservableCollection<AgentCommandItemViewModel> Commands { get; } = new();

        /// <summary>Gets the MCP servers the Discover tab offers.</summary>
        public ObservableCollection<McpCatalogItemViewModel> CatalogServers { get; } = new();

        /// <summary>Gets the plugin repositories the Discover tab offers.</summary>
        public ObservableCollection<PluginCatalogItemViewModel> CatalogPlugins { get; } = new();

        /// <summary>Gets or sets how to ask the user for a folder; the view sets it, tests replace it.</summary>
        public Func<string, Task<string?>>? PickFolderAsync { get; set; }

        /// <summary>Gets whether no MCP server is configured.</summary>
        public bool HasNoMcpServers => McpServers.Count == 0;

        /// <summary>Gets whether no plugin is installed.</summary>
        public bool HasNoPlugins => Plugins.Count == 0;

        /// <summary>Gets whether no skill is available.</summary>
        public bool HasNoSkills => Skills.Count == 0;

        /// <summary>Gets whether no command is available.</summary>
        public bool HasNoCommands => Commands.Count == 0;

        /// <summary>Gets the number of installed extensions of every kind.</summary>
        public int InstalledCount => McpServers.Count + Plugins.Count + Skills.Count + Commands.Count;

        /// <summary>Gets the number of MCP servers.</summary>
        public int McpServerCount => McpServers.Count;

        /// <summary>Gets the number of plugins.</summary>
        public int PluginCount => Plugins.Count;

        /// <summary>Gets the number of skills.</summary>
        public int SkillCount => Skills.Count;

        /// <summary>Gets the number of commands.</summary>
        public int CommandCount => Commands.Count;

        /// <summary>Gets the number of MCP servers the search shows.</summary>
        public int VisibleMcpServerCount => McpServers.Count(item => item.IsVisible);

        /// <summary>Gets the number of plugins the search shows.</summary>
        public int VisiblePluginCount => Plugins.Count(item => item.IsVisible);

        /// <summary>Gets the number of skills the search shows.</summary>
        public int VisibleSkillCount => Skills.Count(item => item.IsVisible);

        /// <summary>Gets the number of commands the search shows.</summary>
        public int VisibleCommandCount => Commands.Count(item => item.IsVisible);

        /// <summary>Gets the line under the page title.</summary>
        public string SummaryText => string.Join(" · ",
            ExtensionCounts.McpServers(McpServers.Count),
            ExtensionCounts.Skills(Skills.Count),
            ExtensionCounts.Plugins(Plugins.Count),
            ExtensionCounts.Commands(Commands.Count));

        /// <summary>Gets or sets whether the Discover tab is showing instead of Installed.</summary>
        public bool IsDiscoverTab
        {
            get => _isDiscoverTab;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDiscoverTab, value);
                this.RaisePropertyChanged(nameof(IsInstalledTab));
            }
        }

        /// <summary>Gets whether the Installed tab is showing.</summary>
        public bool IsInstalledTab => !IsDiscoverTab;

        /// <summary>Gets or sets the text both tabs are filtered by.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value ?? string.Empty);
                ApplyFilter();
            }
        }

        /// <summary>Gets or sets which kind the Installed tab lists.</summary>
        public ExtensionsFilter Filter
        {
            get => _filter;
            set
            {
                this.RaiseAndSetIfChanged(ref _filter, value);
                this.RaisePropertyChanged(nameof(IsFilterAll));
                this.RaisePropertyChanged(nameof(IsFilterMcpServers));
                this.RaisePropertyChanged(nameof(IsFilterPlugins));
                this.RaisePropertyChanged(nameof(IsFilterSkills));
                this.RaisePropertyChanged(nameof(IsFilterCommands));
                ApplyFilter();
            }
        }

        /// <summary>Gets whether every kind is listed.</summary>
        public bool IsFilterAll => Filter == ExtensionsFilter.All;

        /// <summary>Gets whether only MCP servers are listed.</summary>
        public bool IsFilterMcpServers => Filter == ExtensionsFilter.McpServers;

        /// <summary>Gets whether only plugins are listed.</summary>
        public bool IsFilterPlugins => Filter == ExtensionsFilter.Plugins;

        /// <summary>Gets whether only skills are listed.</summary>
        public bool IsFilterSkills => Filter == ExtensionsFilter.Skills;

        /// <summary>Gets whether only commands are listed.</summary>
        public bool IsFilterCommands => Filter == ExtensionsFilter.Commands;

        /// <summary>Gets whether the MCP servers group shows.</summary>
        public bool ShowMcpSection => ShowsSection(ExtensionsFilter.McpServers, McpServers.Any(item => item.IsVisible));

        /// <summary>Gets whether the plugins group shows.</summary>
        public bool ShowPluginsSection => ShowsSection(ExtensionsFilter.Plugins, Plugins.Any(item => item.IsVisible));

        /// <summary>Gets whether the skills group shows.</summary>
        public bool ShowSkillsSection => ShowsSection(ExtensionsFilter.Skills, Skills.Any(item => item.IsVisible));

        /// <summary>Gets whether the commands group shows.</summary>
        public bool ShowCommandsSection => ShowsSection(ExtensionsFilter.Commands, Commands.Any(item => item.IsVisible));

        /// <summary>Gets whether the Installed tab has nothing to list.</summary>
        public bool HasNoInstalledMatches => !(ShowMcpSection || ShowPluginsSection || ShowSkillsSection || ShowCommandsSection);

        /// <summary>Gets whether the empty Installed tab is empty because of the search.</summary>
        public bool IsSearchWithoutMatches => HasNoInstalledMatches && HasSearch;

        /// <summary>Gets whether the empty Installed tab is empty because nothing is installed.</summary>
        public bool IsNothingInstalled => HasNoInstalledMatches && !HasSearch;

        /// <summary>Gets the line under "No matches".</summary>
        public string NoMatchesText => Loc.Format("Extensions.NoMatches.Body", SearchText.Trim());

        /// <summary>Gets whether no catalog server matches the search.</summary>
        public bool HasNoCatalogServerMatches => CatalogServers.All(item => !item.IsVisible);

        /// <summary>Gets whether any catalog plugin matches the search.</summary>
        public bool ShowCatalogPlugins => CatalogPlugins.Any(item => item.IsVisible);

        /// <summary>Gets or sets the folder, git URL or owner/repo to install a plugin from.</summary>
        public string PluginSource
        {
            get => _pluginSource;
            set => this.RaiseAndSetIfChanged(ref _pluginSource, value);
        }

        /// <summary>Gets or sets the folder to install a skill from.</summary>
        public string SkillSource
        {
            get => _skillSource;
            set => this.RaiseAndSetIfChanged(ref _skillSource, value);
        }

        /// <summary>Gets the result of the last action.</summary>
        public string Message
        {
            get => _message;
            private set
            {
                this.RaiseAndSetIfChanged(ref _message, value);
                this.RaisePropertyChanged(nameof(HasMessage));
            }
        }

        /// <summary>Gets whether there is a message to show.</summary>
        public bool HasMessage => !string.IsNullOrWhiteSpace(Message);

        /// <summary>Gets whether an install or connect is running.</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

        /// <summary>Re-reads every list.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Reads mcp.json again and starts every server that has not started yet.</summary>
        public ICommand ConnectServersCommand { get; }

        /// <summary>Opens mcp.json, creating it with an example when missing.</summary>
        public ICommand OpenMcpConfigCommand { get; }

        /// <summary>Installs a plugin from <see cref="PluginSource"/>.</summary>
        public ICommand InstallPluginCommand { get; }

        /// <summary>Installs a skill from <see cref="SkillSource"/>.</summary>
        public ICommand InstallSkillCommand { get; }

        /// <summary>Picks the folder to install a skill from.</summary>
        public ICommand BrowseSkillFolderCommand { get; }

        /// <summary>Opens the plugins folder.</summary>
        public ICommand OpenPluginsFolderCommand { get; }

        /// <summary>Opens the skills folder.</summary>
        public ICommand OpenSkillsFolderCommand { get; }

        /// <summary>Opens the commands folder.</summary>
        public ICommand OpenCommandsFolderCommand { get; }

        /// <summary>Shows the Installed tab.</summary>
        public ICommand ShowInstalledCommand { get; }

        /// <summary>Shows the Discover tab.</summary>
        public ICommand ShowDiscoverCommand { get; }

        /// <summary>Sets <see cref="Filter"/> from its name.</summary>
        public ReactiveCommand<string, Unit> SetFilterCommand { get; }

        /// <inheritdoc />
        public override void OnNavigatedTo() => Refresh();

        /// <summary>Re-reads every list.</summary>
        public void Refresh()
        {
            RefreshMcpServers();

            Replace(Plugins, Safe(() => _plugins?.GetPlugins()).Select(plugin => new ExtensionPluginItemViewModel(
                plugin,
                enabled => SetPluginEnabled(plugin.Name, enabled),
                () => UninstallPlugin(plugin.Name),
                () => _openPath(plugin.Directory))));

            Replace(Skills, Safe(() => _skills?.GetSkills()).Select(skill => new AgentSkillItemViewModel(
                skill,
                skill.Source == "user" ? () => UninstallSkill(skill.Name) : null,
                () => _openPath(skill.Directory))));

            Replace(Commands, Safe(() => _commands?.GetCommands()).Select(command => new AgentCommandItemViewModel(
                command,
                () => _openPath(command.FilePath))));

            RaiseCounts();
        }

        /// <summary>Reads mcp.json again and starts every server that has not started yet.</summary>
        public async Task ConnectServersAsync()
        {
            if (_mcpHost is null)
            {
                return;
            }

            await _mcpHost.ReloadAsync();
            await StartServersAsync();
        }

        /// <summary>
        /// Adds a server from the catalog to mcp.json, asking for a folder first when it needs one, then starts it.
        /// </summary>
        /// <param name="item">The catalog server.</param>
        public async Task AddCatalogServerAsync(McpCatalogItemViewModel item)
        {
            if (_mcpHost is null || item.IsAdded)
            {
                return;
            }

            var entry = item.Entry;
            string? folder = null;
            if (entry.NeedsFolder)
            {
                folder = PickFolderAsync is null ? null : await PickFolderAsync(Loc.Format("Extensions.Catalog.PickFolder", entry.Title));
                if (string.IsNullOrWhiteSpace(folder))
                {
                    return;
                }
            }

            try
            {
                new UserMcpServerSource(_mcpHost.UserConfigPath).AddServer(entry.Name, entry.ToConfigEntry(folder));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                ShowMessage(() => ex.Message);
                return;
            }

            await _mcpHost.ReloadAsync();
            RefreshMcpServers();
            RaiseCounts();
            ShowMessage(() => Loc.Format("Extensions.Message.ServerAdded", entry.Title));
            await StartServersAsync();
        }

        /// <summary>
        /// Installs a plugin repository from the catalog.
        /// </summary>
        /// <param name="item">The catalog plugin.</param>
        public async Task InstallCatalogPluginAsync(PluginCatalogItemViewModel item)
        {
            if (_plugins is null || item.IsAdded || item.IsInstalling)
            {
                return;
            }

            item.IsInstalling = true;
            try
            {
                await InstallPluginFromAsync(item.Entry.Source);
            }
            finally
            {
                item.IsInstalling = false;
            }
        }

        /// <summary>Installs a plugin from <see cref="PluginSource"/>.</summary>
        public async Task InstallPluginAsync()
        {
            if (_plugins is null || string.IsNullOrWhiteSpace(PluginSource))
            {
                ShowMessage(() => Loc.Get("Extensions.Message.EnterPluginSource"));
                return;
            }

            if (await InstallPluginFromAsync(PluginSource.Trim()))
            {
                PluginSource = string.Empty;
            }
        }

        /// <summary>Installs a skill from <see cref="SkillSource"/>.</summary>
        public void InstallSkill()
        {
            if (_skills is null || string.IsNullOrWhiteSpace(SkillSource))
            {
                ShowMessage(() => Loc.Get("Extensions.Message.EnterSkillSource"));
                return;
            }

            try
            {
                var skill = _skills.Install(SkillSource.Trim().Trim('"'));
                SkillSource = string.Empty;
                ShowMessage(() => Loc.Format("Extensions.Message.SkillInstalled", skill.Name));
                Refresh();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowMessage(() => ex.Message);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_mcpHost is not null)
            {
                _mcpHost.StateChanged -= OnMcpStateChanged;
            }

            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        private async Task StartServersAsync()
        {
            if (_mcpHost is null)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var tools = await _mcpHost.GetToolsAsync();
                RefreshMcpServers();
                RaiseCounts();
                var toolCount = tools.Count;
                var failed = McpServers.Count(server => server.IsFailed);
                var starting = McpServers.Count(server => server.IsStarting);
                if (failed > 0)
                {
                    ShowMessage(() => Loc.Format(
                        "Extensions.Message.ConnectedWithFailures",
                        ExtensionCounts.Tools(toolCount),
                        ExtensionCounts.Servers(failed)));
                }
                else if (starting > 0)
                {
                    ShowMessage(() => Loc.Format(
                        "Extensions.Message.StillStarting",
                        ExtensionCounts.Tools(toolCount),
                        ExtensionCounts.Servers(starting)));
                }
                else
                {
                    ShowMessage(() => Loc.Format("Extensions.Message.Connected", ExtensionCounts.Tools(toolCount)));
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task<bool> InstallPluginFromAsync(string source)
        {
            if (_plugins is null)
            {
                return false;
            }

            IsBusy = true;
            try
            {
                var result = await _plugins.InstallAsync(source);
                ShowMessage(() => result.Message);
                if (result.Success)
                {
                    await ReloadMcpQuietlyAsync();
                    Refresh();
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                ShowMessage(() => Loc.Format("Extensions.Message.PluginInstallFailed", ex.Message));
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task BrowseSkillFolderAsync()
        {
            if (PickFolderAsync is not null && await PickFolderAsync(Loc.Get("Extensions.Skills.SourcePlaceholder")) is { Length: > 0 } folder)
            {
                SkillSource = folder;
            }
        }

        private void SetPluginEnabled(string name, bool enabled)
        {
            _plugins?.SetEnabled(name, enabled);
            if (enabled)
            {
                ShowMessage(() => Loc.Format("Extensions.Message.PluginOn", name));
            }
            else
            {
                ShowMessage(() => Loc.Format("Extensions.Message.PluginOff", name));
            }

            _ = ReloadMcpQuietlyAsync().ContinueWith(_ => _post(Refresh), TaskScheduler.Default);
        }

        private void UninstallPlugin(string name)
        {
            if (_plugins?.Uninstall(name) == true)
            {
                ShowMessage(() => Loc.Format("Extensions.Message.PluginRemoved", name));
                _ = ReloadMcpQuietlyAsync().ContinueWith(_ => _post(Refresh), TaskScheduler.Default);
            }
        }

        private void UninstallSkill(string name)
        {
            if (_skills?.Uninstall(name) == true)
            {
                ShowMessage(() => Loc.Format("Extensions.Message.SkillRemoved", name));
                Refresh();
            }
        }

        private void SetServerEnabled(string name, bool enabled)
        {
            if (!EditUserConfig(source => source.SetServerEnabled(name, enabled)))
            {
                return;
            }

            if (enabled)
            {
                ShowMessage(() => Loc.Format("Extensions.Message.ServerOn", name));
            }
            else
            {
                ShowMessage(() => Loc.Format("Extensions.Message.ServerOff", name));
            }

            _ = ReloadMcpQuietlyAsync().ContinueWith(_ => _post(() =>
            {
                RefreshMcpServers();
                RaiseCounts();
                if (enabled)
                {
                    _ = StartServersAsync();
                }
            }), TaskScheduler.Default);
        }

        private void RemoveServer(string name)
        {
            if (!EditUserConfig(source => source.RemoveServer(name)))
            {
                return;
            }

            ShowMessage(() => Loc.Format("Extensions.Message.ServerRemoved", name));
            _ = ReloadMcpQuietlyAsync().ContinueWith(_ => _post(() =>
            {
                RefreshMcpServers();
                RaiseCounts();
            }), TaskScheduler.Default);
        }

        private bool EditUserConfig(Func<UserMcpServerSource, bool> edit)
        {
            if (_mcpHost is null)
            {
                return false;
            }

            try
            {
                return edit(new UserMcpServerSource(_mcpHost.UserConfigPath));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                ShowMessage(() => ex.Message);
                return false;
            }
        }

        private void OpenMcpConfig()
        {
            if (_mcpHost is null)
            {
                return;
            }

            try
            {
                new UserMcpServerSource(_mcpHost.UserConfigPath).EnsureExists();
                _openPath(_mcpHost.UserConfigPath);
                ShowMessage(() => Loc.Get("Extensions.Message.EditMcpConfig"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowMessage(() => ex.Message);
            }
        }

        private void OpenFolder(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                _openPath(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowMessage(() => ex.Message);
            }
        }

        private async Task ReloadMcpQuietlyAsync()
        {
            if (_mcpHost is not null)
            {
                await _mcpHost.ReloadAsync();
            }
        }

        private void ShowMessage(Func<string> source)
        {
            _messageSource = source;
            Message = source();
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => _post(() =>
        {
            Title = Loc.Get("Extensions.Title");
            if (_messageSource is not null)
            {
                Message = _messageSource();
            }

            foreach (var item in CatalogServers)
            {
                item.RefreshText();
            }

            foreach (var item in CatalogPlugins)
            {
                item.RefreshText();
            }

            Refresh();
        });

        private void OnMcpStateChanged(object? sender, EventArgs e) => _post(() =>
        {
            RefreshMcpServers();
            RaiseCounts();
        });

        private void RefreshMcpServers()
        {
            Replace(McpServers, Safe(() => _mcpHost?.Servers).Select(state => new McpServerItemViewModel(
                state,
                () => _ = RestartServerAsync(state.Definition.Name),
                enabled => SetServerEnabled(state.Definition.Name, enabled),
                () => RemoveServer(state.Definition.Name),
                OpenMcpConfig)));
        }

        private async Task RestartServerAsync(string name)
        {
            if (_mcpHost is null)
            {
                return;
            }

            await _mcpHost.RestartAsync(name);
            await StartServersAsync();
        }

        private void RaiseCounts()
        {
            var userServers = McpServers.Where(server => server.IsUserServer).Select(server => server.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var item in CatalogServers)
            {
                item.IsAdded = userServers.Contains(item.Entry.Name);
            }

            var plugins = Plugins.Select(plugin => plugin.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var item in CatalogPlugins)
            {
                item.IsAdded = plugins.Contains(item.Entry.InstalledPlugin);
            }

            this.RaisePropertyChanged(nameof(HasNoMcpServers));
            this.RaisePropertyChanged(nameof(HasNoPlugins));
            this.RaisePropertyChanged(nameof(HasNoSkills));
            this.RaisePropertyChanged(nameof(HasNoCommands));
            this.RaisePropertyChanged(nameof(InstalledCount));
            this.RaisePropertyChanged(nameof(McpServerCount));
            this.RaisePropertyChanged(nameof(PluginCount));
            this.RaisePropertyChanged(nameof(SkillCount));
            this.RaisePropertyChanged(nameof(CommandCount));
            this.RaisePropertyChanged(nameof(SummaryText));
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = SearchText.Trim();
            foreach (var item in McpServers)
            {
                item.IsVisible = Matches(query, item.Name, item.Detail, item.SourceText);
            }

            foreach (var item in Plugins)
            {
                item.IsVisible = Matches(query, item.Name, item.Description, item.ComponentsText);
            }

            foreach (var item in Skills)
            {
                item.IsVisible = Matches(query, item.Name, item.Description, item.SourceText);
            }

            foreach (var item in Commands)
            {
                item.IsVisible = Matches(query, item.Usage, item.Description, item.SourceText);
            }

            foreach (var item in CatalogServers)
            {
                item.IsVisible = Matches(query, item.Title, item.Description, item.Entry.Name);
            }

            foreach (var item in CatalogPlugins)
            {
                item.IsVisible = Matches(query, item.Title, item.Description, item.Entry.Source);
            }

            this.RaisePropertyChanged(nameof(VisibleMcpServerCount));
            this.RaisePropertyChanged(nameof(VisiblePluginCount));
            this.RaisePropertyChanged(nameof(VisibleSkillCount));
            this.RaisePropertyChanged(nameof(VisibleCommandCount));
            this.RaisePropertyChanged(nameof(ShowMcpSection));
            this.RaisePropertyChanged(nameof(ShowPluginsSection));
            this.RaisePropertyChanged(nameof(ShowSkillsSection));
            this.RaisePropertyChanged(nameof(ShowCommandsSection));
            this.RaisePropertyChanged(nameof(HasNoInstalledMatches));
            this.RaisePropertyChanged(nameof(IsSearchWithoutMatches));
            this.RaisePropertyChanged(nameof(IsNothingInstalled));
            this.RaisePropertyChanged(nameof(NoMatchesText));
            this.RaisePropertyChanged(nameof(HasNoCatalogServerMatches));
            this.RaisePropertyChanged(nameof(ShowCatalogPlugins));
        }

        private bool ShowsSection(ExtensionsFilter kind, bool hasVisibleItems)
        {
            if (Filter != ExtensionsFilter.All && Filter != kind)
            {
                return false;
            }

            // A kind picked on its own shows even when empty, so its empty text and folder button stay reachable.
            return hasVisibleItems || (Filter == kind && !HasSearch);
        }

        private static bool Matches(string query, params string?[] fields) =>
            query.Length == 0 || fields.Any(field => field?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true);

        private IReadOnlyList<T> Safe<T>(Func<IReadOnlyList<T>?> read)
        {
            try
            {
                return read() ?? [];
            }
            catch (Exception ex)
            {
                ShowMessage(() => ex.Message);
                return [];
            }
        }

        private static void Replace<T>(ObservableCollection<T> collection, IEnumerable<T> items)
        {
            collection.Clear();
            foreach (var item in items)
            {
                collection.Add(item);
            }
        }

        private static void OpenWithShell(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// A row on the Installed tab that the search can hide.
    /// </summary>
    public abstract class ExtensionRowViewModel : ReactiveObject
    {
        private bool _isVisible = true;

        /// <summary>Gets or sets whether the row matches the search.</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }
    }

    /// <summary>
    /// One MCP server on the Extensions page.
    /// </summary>
    public sealed class McpServerItemViewModel : ExtensionRowViewModel
    {
        private readonly Action<bool>? _setEnabled;
        private bool _isEnabled;

        /// <summary>
        /// Creates the item.
        /// </summary>
        /// <param name="state">The server state.</param>
        /// <param name="restart">Restarts the server.</param>
        /// <param name="setEnabled">Turns a server from mcp.json on or off.</param>
        /// <param name="remove">Removes a server from mcp.json.</param>
        /// <param name="openConfig">Opens mcp.json.</param>
        public McpServerItemViewModel(
            McpServerState state,
            Action restart,
            Action<bool>? setEnabled = null,
            Action? remove = null,
            Action? openConfig = null)
        {
            var definition = state.Definition;
            Name = definition.Name;
            Detail = definition.Transport == McpTransportKind.Http
                ? $"HTTP · {SafeHost(definition.Url)}"
                : $"stdio · {string.Join(' ', new[] { definition.Command ?? string.Empty }.Concat(definition.Arguments)).Trim()}";
            IsUserServer = definition.Source == "user";
            SourceText = IsUserServer ? "mcp.json" : definition.Source;
            (StatusText, StatusClass) = state.Status switch
            {
                McpServerStatus.Ready => (ExtensionCounts.Tools(state.ToolCount), "Success"),
                McpServerStatus.Connecting => (Loc.Get("Extensions.Mcp.Status.Starting"), "Accent"),
                McpServerStatus.Failed => (Loc.Get("Extensions.Mcp.Status.Failed"), "Danger"),
                McpServerStatus.Disabled => (Loc.Get("Common.Off"), "Neutral"),
                _ => (Loc.Get("Extensions.Mcp.Status.Idle"), "Neutral")
            };
            Error = state.Error ?? string.Empty;
            IsFailed = state.Status == McpServerStatus.Failed;
            _isEnabled = state.Status != McpServerStatus.Disabled;
            CanToggle = IsUserServer && setEnabled is not null;
            CanRemove = IsUserServer && remove is not null;
            _setEnabled = setEnabled;
            RestartCommand = ReactiveCommand.Create(restart);
            RemoveCommand = ReactiveCommand.Create(remove ?? (() => { }));
            OpenConfigCommand = ReactiveCommand.Create(openConfig ?? (() => { }));
        }

        /// <summary>Gets the server name; its tools are named mcp__{name}__{tool}.</summary>
        public string Name { get; }

        /// <summary>Gets how Kam reaches the server.</summary>
        public string Detail { get; }

        /// <summary>Gets where the entry comes from.</summary>
        public string SourceText { get; }

        /// <summary>Gets whether the entry is in the user's mcp.json.</summary>
        public bool IsUserServer { get; }

        /// <summary>Gets the status text.</summary>
        public string StatusText { get; }

        /// <summary>Gets the status class: Success, Accent, Danger or Neutral.</summary>
        public string StatusClass { get; }

        /// <summary>Gets whether the server is ready.</summary>
        public bool IsReady => StatusClass == "Success";

        /// <summary>Gets whether the server failed to start.</summary>
        public bool IsFailed { get; }

        /// <summary>Gets whether the server is starting.</summary>
        public bool IsStarting => StatusClass == "Accent";

        /// <summary>Gets the start error, if any.</summary>
        public string Error { get; }

        /// <summary>Gets whether there is an error to show.</summary>
        public bool HasError => Error.Length > 0;

        /// <summary>Gets whether the server can be turned on and off here.</summary>
        public bool CanToggle { get; }

        /// <summary>Gets whether the server can be removed here.</summary>
        public bool CanRemove { get; }

        /// <summary>Gets or sets whether the server runs.</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _isEnabled, value);
                _setEnabled?.Invoke(value);
            }
        }

        /// <summary>Restarts the server.</summary>
        public ICommand RestartCommand { get; }

        /// <summary>Removes the server from mcp.json.</summary>
        public ICommand RemoveCommand { get; }

        /// <summary>Opens mcp.json.</summary>
        public ICommand OpenConfigCommand { get; }

        private static string SafeHost(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath.TrimEnd('/') : url ?? string.Empty;
    }

    /// <summary>
    /// One plugin on the Extensions page.
    /// </summary>
    public sealed class ExtensionPluginItemViewModel : ExtensionRowViewModel
    {
        private readonly Action<bool> _setEnabled;
        private bool _isEnabled;

        /// <summary>
        /// Creates the item.
        /// </summary>
        /// <param name="plugin">The plugin.</param>
        /// <param name="setEnabled">Turns the plugin on or off.</param>
        /// <param name="uninstall">Removes the plugin.</param>
        /// <param name="openFolder">Opens its folder.</param>
        public ExtensionPluginItemViewModel(AgentPluginInfo plugin, Action<bool> setEnabled, Action uninstall, Action openFolder)
        {
            _setEnabled = setEnabled;
            _isEnabled = plugin.Enabled;
            Name = plugin.Name;
            Description = string.IsNullOrWhiteSpace(plugin.Description) ? Loc.Get("Extensions.NoDescription") : plugin.Description;
            VersionText = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(plugin.Version) ? null : "v" + plugin.Version,
                string.IsNullOrWhiteSpace(plugin.Author) ? null : plugin.Author
            }.Where(part => part is not null));

            var parts = new List<string>();
            AddPart(parts, plugin.SkillDirectories.Count, ExtensionCounts.Skills);
            AddPart(parts, plugin.CommandFiles.Count, ExtensionCounts.Commands);
            AddPart(parts, plugin.McpServers.Count, ExtensionCounts.McpServers);
            AddPart(parts, plugin.AgentCount, ExtensionCounts.Agents);
            ComponentsText = parts.Count == 0 ? Loc.Get("Extensions.Plugins.NothingToLoad") : string.Join(" · ", parts);
            Error = plugin.Error ?? string.Empty;
            UninstallCommand = ReactiveCommand.Create(uninstall);
            OpenFolderCommand = ReactiveCommand.Create(openFolder);
        }

        /// <summary>Gets the plugin name.</summary>
        public string Name { get; }

        /// <summary>Gets the description.</summary>
        public string Description { get; }

        /// <summary>Gets the version and author.</summary>
        public string VersionText { get; }

        /// <summary>Gets what the plugin contributes.</summary>
        public string ComponentsText { get; }

        /// <summary>Gets a problem with the plugin, if any.</summary>
        public string Error { get; }

        /// <summary>Gets whether there is an error to show.</summary>
        public bool HasError => Error.Length > 0;

        /// <summary>Gets or sets whether the plugin is on.</summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _isEnabled, value);
                _setEnabled(value);
            }
        }

        /// <summary>Removes the plugin.</summary>
        public ICommand UninstallCommand { get; }

        /// <summary>Opens the plugin folder.</summary>
        public ICommand OpenFolderCommand { get; }

        private static void AddPart(List<string> parts, int count, Func<int, string> format)
        {
            if (count > 0)
            {
                parts.Add(format(count));
            }
        }
    }

    /// <summary>
    /// One Agent Skill on the Extensions page.
    /// </summary>
    public sealed class AgentSkillItemViewModel : ExtensionRowViewModel
    {
        /// <summary>
        /// Creates the item.
        /// </summary>
        /// <param name="skill">The skill.</param>
        /// <param name="uninstall">Removes the skill; <c>null</c> when it is not in the user's folder.</param>
        /// <param name="openFolder">Opens its folder.</param>
        public AgentSkillItemViewModel(AgentSkillInfo skill, Action? uninstall, Action openFolder)
        {
            Name = skill.Name;
            Description = string.IsNullOrWhiteSpace(skill.Description) ? Loc.Get("Extensions.NoDescription") : skill.Description;
            SourceText = skill.Source switch
            {
                "user" => Loc.Get("Extensions.Source.UserSkills"),
                "workspace" => Loc.Get("Extensions.Source.Workspace"),
                "imported" => Loc.Get("Extensions.Source.Imported"),
                _ when skill.Source.StartsWith("plugin:", StringComparison.Ordinal) =>
                    Loc.Format("Extensions.Source.Plugin", skill.Source["plugin:".Length..]),
                _ => skill.Source
            };
            CanUninstall = uninstall is not null;
            UninstallCommand = ReactiveCommand.Create(uninstall ?? (() => { }));
            OpenFolderCommand = ReactiveCommand.Create(openFolder);
        }

        /// <summary>Gets the skill name.</summary>
        public string Name { get; }

        /// <summary>Gets when the agent should use it.</summary>
        public string Description { get; }

        /// <summary>Gets where it was found.</summary>
        public string SourceText { get; }

        /// <summary>Gets whether it can be removed here.</summary>
        public bool CanUninstall { get; }

        /// <summary>Removes the skill.</summary>
        public ICommand UninstallCommand { get; }

        /// <summary>Opens the skill folder.</summary>
        public ICommand OpenFolderCommand { get; }
    }

    /// <summary>
    /// One Markdown command on the Extensions page.
    /// </summary>
    public sealed class AgentCommandItemViewModel : ExtensionRowViewModel
    {
        /// <summary>
        /// Creates the item.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="openFile">Opens its file.</param>
        public AgentCommandItemViewModel(AgentCommandInfo command, Action openFile)
        {
            Usage = string.IsNullOrWhiteSpace(command.ArgumentHint) ? "/" + command.Name : $"/{command.Name} {command.ArgumentHint}";
            Description = command.Description;
            SourceText = command.Source switch
            {
                "user" => Loc.Get("Extensions.Source.UserCommands"),
                "workspace" => Loc.Get("Extensions.Source.Workspace"),
                _ when command.Source.StartsWith("plugin:", StringComparison.Ordinal) =>
                    Loc.Format("Extensions.Source.Plugin", command.Source["plugin:".Length..]),
                _ => command.Source
            };
            OpenFileCommand = ReactiveCommand.Create(openFile);
        }

        /// <summary>Gets how to type the command.</summary>
        public string Usage { get; }

        /// <summary>Gets what it does.</summary>
        public string Description { get; }

        /// <summary>Gets where it was found.</summary>
        public string SourceText { get; }

        /// <summary>Opens the command file.</summary>
        public ICommand OpenFileCommand { get; }
    }

    /// <summary>
    /// A card on the Discover tab.
    /// </summary>
    public abstract class CatalogItemViewModel : ExtensionRowViewModel
    {
        private readonly string _descriptionKey;
        private readonly ExtensionRuntime _runtime;
        private string _description = string.Empty;
        private string _runtimeText = string.Empty;
        private string _missingText = string.Empty;
        private string _getRuntimeText = string.Empty;
        private bool _isAdded;

        /// <summary>
        /// Creates the card.
        /// </summary>
        /// <param name="id">The id its description is stored under.</param>
        /// <param name="title">The display name.</param>
        /// <param name="icon">The icon resource.</param>
        /// <param name="runtime">What it needs on this computer.</param>
        /// <param name="isRuntimeAvailable">Whether that is installed.</param>
        /// <param name="homepage">Where it is documented.</param>
        /// <param name="openPath">Opens a web page.</param>
        protected CatalogItemViewModel(
            string id,
            string title,
            string icon,
            ExtensionRuntime runtime,
            bool isRuntimeAvailable,
            string homepage,
            Action<string> openPath)
        {
            _descriptionKey = "Extensions.Catalog." + id;
            _runtime = runtime;
            Title = title;
            Icon = icon;
            IsRuntimeMissing = !isRuntimeAvailable;
            OpenHomepageCommand = ReactiveCommand.Create(() => openPath(homepage));
            GetRuntimeCommand = ReactiveCommand.Create(() =>
            {
                if (ExtensionCatalog.InstallPage(runtime) is { } page)
                {
                    openPath(page);
                }
            });
            RefreshText();
        }

        /// <summary>Gets the display name.</summary>
        public string Title { get; }

        /// <summary>Gets the icon resource.</summary>
        public string Icon { get; }

        /// <summary>Gets what it does.</summary>
        public string Description
        {
            get => _description;
            private set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        /// <summary>Gets what it needs, such as Node.js.</summary>
        public string RuntimeText
        {
            get => _runtimeText;
            private set => this.RaiseAndSetIfChanged(ref _runtimeText, value);
        }

        /// <summary>Gets whether what it needs was not found on this computer.</summary>
        public bool IsRuntimeMissing { get; }

        /// <summary>Gets whether what it needs is installed.</summary>
        public bool IsRuntimeReady => !IsRuntimeMissing;

        /// <summary>Gets the warning shown when what it needs is missing.</summary>
        public string MissingText
        {
            get => _missingText;
            private set => this.RaiseAndSetIfChanged(ref _missingText, value);
        }

        /// <summary>Gets the label of the link to download what it needs.</summary>
        public string GetRuntimeText
        {
            get => _getRuntimeText;
            private set => this.RaiseAndSetIfChanged(ref _getRuntimeText, value);
        }

        /// <summary>Gets or sets whether it is already installed.</summary>
        public bool IsAdded
        {
            get => _isAdded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isAdded, value);
                this.RaisePropertyChanged(nameof(CanAdd));
            }
        }

        /// <summary>Gets whether the Add button shows.</summary>
        public virtual bool CanAdd => !IsAdded;

        /// <summary>Opens its documentation.</summary>
        public ICommand OpenHomepageCommand { get; }

        /// <summary>Opens the download page for what it needs.</summary>
        public ICommand GetRuntimeCommand { get; }

        /// <summary>Re-reads the text in the current language.</summary>
        public void RefreshText()
        {
            Description = Loc.Get(_descriptionKey);
            var runtime = _runtime switch
            {
                ExtensionRuntime.Node => Loc.Get("Extensions.Runtime.Node"),
                ExtensionRuntime.Uv => Loc.Get("Extensions.Runtime.Uv"),
                ExtensionRuntime.Git => Loc.Get("Extensions.Runtime.Git"),
                _ => Loc.Get("Extensions.Runtime.None")
            };
            RuntimeText = runtime;
            MissingText = Loc.Format("Extensions.Runtime.Missing", runtime);
            GetRuntimeText = Loc.Format("Extensions.Runtime.Get", runtime);
        }
    }

    /// <summary>
    /// An MCP server card on the Discover tab.
    /// </summary>
    public sealed class McpCatalogItemViewModel : CatalogItemViewModel
    {
        /// <summary>
        /// Creates the card.
        /// </summary>
        /// <param name="entry">The catalog entry.</param>
        /// <param name="isRuntimeAvailable">Whether what it needs is installed.</param>
        /// <param name="add">Adds it to mcp.json.</param>
        /// <param name="openPath">Opens a web page.</param>
        public McpCatalogItemViewModel(McpCatalogEntry entry, bool isRuntimeAvailable, Action<McpCatalogItemViewModel> add, Action<string> openPath)
            : base(entry.Name, entry.Title, entry.Icon, entry.Runtime, isRuntimeAvailable, entry.Homepage, openPath)
        {
            Entry = entry;
            AddCommand = ReactiveCommand.Create(() => add(this));
        }

        /// <summary>Gets the catalog entry.</summary>
        public McpCatalogEntry Entry { get; }

        /// <summary>Gets whether adding it asks for a folder.</summary>
        public bool NeedsFolder => Entry.NeedsFolder;

        /// <summary>Adds it to mcp.json.</summary>
        public ICommand AddCommand { get; }
    }

    /// <summary>
    /// A plugin repository card on the Discover tab.
    /// </summary>
    public sealed class PluginCatalogItemViewModel : CatalogItemViewModel
    {
        private bool _isInstalling;

        /// <summary>
        /// Creates the card.
        /// </summary>
        /// <param name="entry">The catalog entry.</param>
        /// <param name="isGitAvailable">Whether git is installed.</param>
        /// <param name="install">Installs it.</param>
        /// <param name="openPath">Opens a web page.</param>
        public PluginCatalogItemViewModel(PluginCatalogEntry entry, bool isGitAvailable, Action<PluginCatalogItemViewModel> install, Action<string> openPath)
            : base(entry.Id, entry.Title, entry.Icon, ExtensionRuntime.Git, isGitAvailable, entry.Homepage, openPath)
        {
            Entry = entry;
            AddCommand = ReactiveCommand.Create(() => install(this));
        }

        /// <summary>Gets the catalog entry.</summary>
        public PluginCatalogEntry Entry { get; }

        /// <summary>Gets or sets whether it is being installed.</summary>
        public bool IsInstalling
        {
            get => _isInstalling;
            set
            {
                this.RaiseAndSetIfChanged(ref _isInstalling, value);
                this.RaisePropertyChanged(nameof(CanAdd));
            }
        }

        /// <inheritdoc />
        public override bool CanAdd => !IsAdded && !IsInstalling;

        /// <summary>Installs it.</summary>
        public ICommand AddCommand { get; }
    }

    /// <summary>
    /// Counted nouns for the Extensions page, such as "3 MCP servers", in the current language.
    /// </summary>
    internal static class ExtensionCounts
    {
        /// <summary>Formats a number of MCP servers.</summary>
        /// <param name="count">The number.</param>
        public static string McpServers(int count) => count == 1
            ? Loc.Format("Extensions.Count.McpServer.One", count)
            : Loc.Format("Extensions.Count.McpServer.Other", count);

        /// <summary>Formats a number of skills.</summary>
        /// <param name="count">The number.</param>
        public static string Skills(int count) => count == 1
            ? Loc.Format("Extensions.Count.Skill.One", count)
            : Loc.Format("Extensions.Count.Skill.Other", count);

        /// <summary>Formats a number of plugins.</summary>
        /// <param name="count">The number.</param>
        public static string Plugins(int count) => count == 1
            ? Loc.Format("Extensions.Count.Plugin.One", count)
            : Loc.Format("Extensions.Count.Plugin.Other", count);

        /// <summary>Formats a number of slash commands.</summary>
        /// <param name="count">The number.</param>
        public static string Commands(int count) => count == 1
            ? Loc.Format("Extensions.Count.Command.One", count)
            : Loc.Format("Extensions.Count.Command.Other", count);

        /// <summary>Formats a number of tools.</summary>
        /// <param name="count">The number.</param>
        public static string Tools(int count) => count == 1
            ? Loc.Format("Extensions.Count.Tool.One", count)
            : Loc.Format("Extensions.Count.Tool.Other", count);

        /// <summary>Formats a number of servers.</summary>
        /// <param name="count">The number.</param>
        public static string Servers(int count) => count == 1
            ? Loc.Format("Extensions.Count.Server.One", count)
            : Loc.Format("Extensions.Count.Server.Other", count);

        /// <summary>Formats a number of agents.</summary>
        /// <param name="count">The number.</param>
        public static string Agents(int count) => count == 1
            ? Loc.Format("Extensions.Count.Agent.One", count)
            : Loc.Format("Extensions.Count.Agent.Other", count);
    }
}

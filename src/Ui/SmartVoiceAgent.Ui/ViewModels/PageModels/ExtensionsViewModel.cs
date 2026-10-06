using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents.Extensions;
using SmartVoiceAgent.Infrastructure.Agent.Mcp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// The Extensions page: MCP servers, Agent Skills, plugins and Markdown commands the agent can use.
    /// </summary>
    public sealed class ExtensionsViewModel : ViewModelBase, IDisposable
    {
        private readonly IMcpHost? _mcpHost;
        private readonly IAgentPluginCatalog? _plugins;
        private readonly IAgentSkillCatalog? _skills;
        private readonly IAgentCommandCatalog? _commands;
        private readonly Action<string> _openPath;
        private readonly Action<Action> _post;
        private string _pluginSource = string.Empty;
        private string _skillSource = string.Empty;
        private string _message = string.Empty;
        private bool _isBusy;

        /// <summary>
        /// Creates the page.
        /// </summary>
        /// <param name="mcpHost">The MCP host.</param>
        /// <param name="plugins">Installed plugins.</param>
        /// <param name="skills">Agent Skills.</param>
        /// <param name="commands">Markdown commands.</param>
        /// <param name="openPath">Opens a file or folder in the system shell; tests replace it.</param>
        /// <param name="post">Runs an action on the UI thread; tests replace it.</param>
        public ExtensionsViewModel(
            IMcpHost? mcpHost,
            IAgentPluginCatalog? plugins,
            IAgentSkillCatalog? skills,
            IAgentCommandCatalog? commands,
            Action<string>? openPath = null,
            Action<Action>? post = null)
        {
            Title = "Extensions";
            _mcpHost = mcpHost;
            _plugins = plugins;
            _skills = skills;
            _commands = commands;
            _openPath = openPath ?? OpenWithShell;
            _post = post ?? (action => Dispatcher.UIThread.Post(action));

            RefreshCommand = ReactiveCommand.Create(Refresh);
            ConnectServersCommand = ReactiveCommand.CreateFromTask(ConnectServersAsync);
            ReloadServersCommand = ReactiveCommand.CreateFromTask(ReloadServersAsync);
            OpenMcpConfigCommand = ReactiveCommand.Create(OpenMcpConfig);
            InstallPluginCommand = ReactiveCommand.CreateFromTask(InstallPluginAsync);
            InstallSkillCommand = ReactiveCommand.Create(InstallSkill);
            OpenPluginsFolderCommand = ReactiveCommand.Create(() => OpenFolder(_plugins?.PluginsDirectory));
            OpenSkillsFolderCommand = ReactiveCommand.Create(() => OpenFolder(_skills?.UserSkillsDirectory));
            OpenCommandsFolderCommand = ReactiveCommand.Create(() => OpenFolder(_commands?.UserCommandsDirectory));

            if (_mcpHost is not null)
            {
                _mcpHost.StateChanged += OnMcpStateChanged;
            }

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

        /// <summary>Gets whether no MCP server is configured.</summary>
        public bool HasNoMcpServers => McpServers.Count == 0;

        /// <summary>Gets whether no plugin is installed.</summary>
        public bool HasNoPlugins => Plugins.Count == 0;

        /// <summary>Gets whether no skill is available.</summary>
        public bool HasNoSkills => Skills.Count == 0;

        /// <summary>Gets whether no command is available.</summary>
        public bool HasNoCommands => Commands.Count == 0;

        /// <summary>Gets the line under the page title.</summary>
        public string SummaryText =>
            $"{Count(McpServers.Count, "MCP server")} · {Count(Skills.Count, "skill")} · {Count(Plugins.Count, "plugin")} · {Count(Commands.Count, "command")}";

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

        /// <summary>Re-reads every list.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>Starts every server that has not started yet.</summary>
        public ICommand ConnectServersCommand { get; }

        /// <summary>Reads mcp.json again.</summary>
        public ICommand ReloadServersCommand { get; }

        /// <summary>Opens mcp.json, creating it with an example when missing.</summary>
        public ICommand OpenMcpConfigCommand { get; }

        /// <summary>Installs a plugin from <see cref="PluginSource"/>.</summary>
        public ICommand InstallPluginCommand { get; }

        /// <summary>Installs a skill from <see cref="SkillSource"/>.</summary>
        public ICommand InstallSkillCommand { get; }

        /// <summary>Opens the plugins folder.</summary>
        public ICommand OpenPluginsFolderCommand { get; }

        /// <summary>Opens the skills folder.</summary>
        public ICommand OpenSkillsFolderCommand { get; }

        /// <summary>Opens the commands folder.</summary>
        public ICommand OpenCommandsFolderCommand { get; }

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

        /// <summary>Starts every server that has not started yet.</summary>
        public async Task ConnectServersAsync()
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
                var failed = McpServers.Count(server => server.IsFailed);
                Message = failed == 0
                    ? $"Connected. {Count(tools.Count, "tool")} available to the agent."
                    : $"{Count(tools.Count, "tool")} available. {Count(failed, "server")} failed to start; see the error below it.";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Reads mcp.json again.</summary>
        public async Task ReloadServersAsync()
        {
            if (_mcpHost is null)
            {
                return;
            }

            await _mcpHost.ReloadAsync();
            RefreshMcpServers();
            RaiseCounts();
            Message = "Reloaded mcp.json. Servers start on the next chat turn.";
        }

        /// <summary>Installs a plugin from <see cref="PluginSource"/>.</summary>
        public async Task InstallPluginAsync()
        {
            if (_plugins is null || string.IsNullOrWhiteSpace(PluginSource))
            {
                Message = "Enter a plugin folder, a git URL or owner/repo.";
                return;
            }

            IsBusy = true;
            try
            {
                var result = await _plugins.InstallAsync(PluginSource.Trim());
                Message = result.Message;
                if (result.Success)
                {
                    PluginSource = string.Empty;
                    await ReloadMcpQuietlyAsync();
                    Refresh();
                }
            }
            catch (Exception ex)
            {
                Message = $"Could not install the plugin: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Installs a skill from <see cref="SkillSource"/>.</summary>
        public void InstallSkill()
        {
            if (_skills is null || string.IsNullOrWhiteSpace(SkillSource))
            {
                Message = "Enter a folder that contains SKILL.md.";
                return;
            }

            try
            {
                var skill = _skills.Install(SkillSource.Trim().Trim('"'));
                SkillSource = string.Empty;
                Message = $"Installed the {skill.Name} skill. The agent can use it from the next message.";
                Refresh();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                Message = ex.Message;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_mcpHost is not null)
            {
                _mcpHost.StateChanged -= OnMcpStateChanged;
            }
        }

        private void SetPluginEnabled(string name, bool enabled)
        {
            _plugins?.SetEnabled(name, enabled);
            Message = enabled
                ? $"Turned on {name}. Its skills, commands and servers are available from the next message."
                : $"Turned off {name}.";
            _ = ReloadMcpQuietlyAsync().ContinueWith(_ => _post(Refresh), TaskScheduler.Default);
        }

        private void UninstallPlugin(string name)
        {
            if (_plugins?.Uninstall(name) == true)
            {
                Message = $"Removed {name}.";
                _ = ReloadMcpQuietlyAsync().ContinueWith(_ => _post(Refresh), TaskScheduler.Default);
            }
        }

        private void UninstallSkill(string name)
        {
            if (_skills?.Uninstall(name) == true)
            {
                Message = $"Removed the {name} skill.";
                Refresh();
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
                Message = "Edit mcp.json, save it, then press Reload.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Message = ex.Message;
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
                Message = ex.Message;
            }
        }

        private async Task ReloadMcpQuietlyAsync()
        {
            if (_mcpHost is not null)
            {
                await _mcpHost.ReloadAsync();
            }
        }

        private void OnMcpStateChanged(object? sender, EventArgs e) => _post(() =>
        {
            RefreshMcpServers();
            RaiseCounts();
        });

        private void RefreshMcpServers()
        {
            Replace(McpServers, Safe(() => _mcpHost?.Servers).Select(state => new McpServerItemViewModel(
                state,
                () => _ = RestartServerAsync(state.Definition.Name))));
        }

        private async Task RestartServerAsync(string name)
        {
            if (_mcpHost is null)
            {
                return;
            }

            await _mcpHost.RestartAsync(name);
            await ConnectServersAsync();
        }

        private void RaiseCounts()
        {
            this.RaisePropertyChanged(nameof(HasNoMcpServers));
            this.RaisePropertyChanged(nameof(HasNoPlugins));
            this.RaisePropertyChanged(nameof(HasNoSkills));
            this.RaisePropertyChanged(nameof(HasNoCommands));
            this.RaisePropertyChanged(nameof(SummaryText));
        }

        private IReadOnlyList<T> Safe<T>(Func<IReadOnlyList<T>?> read)
        {
            try
            {
                return read() ?? [];
            }
            catch (Exception ex)
            {
                Message = ex.Message;
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

        private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

        private static void OpenWithShell(string path)
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// One MCP server on the Extensions page.
    /// </summary>
    public sealed class McpServerItemViewModel
    {
        /// <summary>
        /// Creates the item.
        /// </summary>
        /// <param name="state">The server state.</param>
        /// <param name="restart">Restarts the server.</param>
        public McpServerItemViewModel(McpServerState state, Action restart)
        {
            var definition = state.Definition;
            Name = definition.Name;
            Detail = definition.Transport == McpTransportKind.Http
                ? $"HTTP · {SafeHost(definition.Url)}"
                : $"stdio · {string.Join(' ', new[] { definition.Command ?? string.Empty }.Concat(definition.Arguments)).Trim()}";
            SourceText = definition.Source == "user" ? "mcp.json" : definition.Source;
            (StatusText, StatusClass) = state.Status switch
            {
                McpServerStatus.Ready => ($"{state.ToolCount} tool{(state.ToolCount == 1 ? string.Empty : "s")}", "Success"),
                McpServerStatus.Connecting => ("Starting", "Accent"),
                McpServerStatus.Failed => ("Failed", "Danger"),
                McpServerStatus.Disabled => ("Off", "Neutral"),
                _ => ("Starts on first use", "Neutral")
            };
            Error = state.Error ?? string.Empty;
            IsFailed = state.Status == McpServerStatus.Failed;
            RestartCommand = ReactiveCommand.Create(restart);
        }

        /// <summary>Gets the server name; its tools are named mcp__{name}__{tool}.</summary>
        public string Name { get; }

        /// <summary>Gets how Kam reaches the server.</summary>
        public string Detail { get; }

        /// <summary>Gets where the entry comes from.</summary>
        public string SourceText { get; }

        /// <summary>Gets the status pill text.</summary>
        public string StatusText { get; }

        /// <summary>Gets the status pill class: Success, Accent, Danger or Neutral.</summary>
        public string StatusClass { get; }

        /// <summary>Gets whether the pill is green.</summary>
        public bool IsReady => StatusClass == "Success";

        /// <summary>Gets whether the server failed to start.</summary>
        public bool IsFailed { get; }

        /// <summary>Gets whether the server is starting.</summary>
        public bool IsStarting => StatusClass == "Accent";

        /// <summary>Gets the start error, if any.</summary>
        public string Error { get; }

        /// <summary>Gets whether there is an error to show.</summary>
        public bool HasError => Error.Length > 0;

        /// <summary>Restarts the server.</summary>
        public ICommand RestartCommand { get; }

        private static string SafeHost(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host + uri.AbsolutePath.TrimEnd('/') : url ?? string.Empty;
    }

    /// <summary>
    /// One plugin on the Extensions page.
    /// </summary>
    public sealed class ExtensionPluginItemViewModel : ReactiveObject
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
            Description = string.IsNullOrWhiteSpace(plugin.Description) ? "No description." : plugin.Description;
            VersionText = string.Join(" · ", new[]
            {
                string.IsNullOrWhiteSpace(plugin.Version) ? null : "v" + plugin.Version,
                string.IsNullOrWhiteSpace(plugin.Author) ? null : plugin.Author
            }.Where(part => part is not null));

            var parts = new List<string>();
            AddPart(parts, plugin.SkillDirectories.Count, "skill");
            AddPart(parts, plugin.CommandFiles.Count, "command");
            AddPart(parts, plugin.McpServers.Count, "MCP server");
            AddPart(parts, plugin.AgentCount, "agent");
            ComponentsText = parts.Count == 0 ? "Nothing to load" : string.Join(" · ", parts);
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

        private static void AddPart(List<string> parts, int count, string noun)
        {
            if (count > 0)
            {
                parts.Add($"{count} {noun}{(count == 1 ? string.Empty : "s")}");
            }
        }
    }

    /// <summary>
    /// One Agent Skill on the Extensions page.
    /// </summary>
    public sealed class AgentSkillItemViewModel
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
            Description = string.IsNullOrWhiteSpace(skill.Description) ? "No description." : skill.Description;
            SourceText = skill.Source switch
            {
                "user" => "Your skills",
                "workspace" => "Workspace",
                "imported" => "Imported",
                _ when skill.Source.StartsWith("plugin:", StringComparison.Ordinal) => "Plugin " + skill.Source["plugin:".Length..],
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
    public sealed class AgentCommandItemViewModel
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
                "user" => "Your commands",
                "workspace" => "Workspace",
                _ when command.Source.StartsWith("plugin:", StringComparison.Ordinal) => "Plugin " + command.Source["plugin:".Length..],
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
}

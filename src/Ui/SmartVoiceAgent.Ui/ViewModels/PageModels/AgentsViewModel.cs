using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Agents.CliAgents;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// The Agents page (Build ladder step 2): the coding-agent CLIs Kam can hand work to and the role agents the main
    /// agent created for recent tasks, with a detail pane for whichever row is selected. Saved, reusable agents do
    /// not exist yet, and the page says so.
    /// </summary>
    public sealed class AgentsViewModel : ViewModelBase, IDisposable
    {
        private const int MaxObservationLength = 200;

        private readonly IRuntimeAgentRunStore? _runs;
        private readonly ICliAgentDetector? _cliAgents;
        private readonly Action _openIntegrations;
        private readonly Action<Action> _post;
        private readonly Func<DateTimeOffset> _clock;
        private IReadOnlyList<RuntimeAgentRun> _lastRuns = [];
        private IReadOnlyList<CliAgentStatus>? _lastStatuses;
        private AgentsListItem? _selected;
        private string? _selectedKey;
        private bool _userSelected;
        private int _refreshVersion;
        private bool _isChecking;

        /// <summary>
        /// Creates the page and starts a refresh.
        /// </summary>
        /// <param name="runs">Where role-agent runs are recorded; null shows none, as in tests.</param>
        /// <param name="cliAgents">Detects the coding-agent CLIs; null shows them as not checked.</param>
        /// <param name="openIntegrations">Opens the Integrations page, where coding agents are set up.</param>
        public AgentsViewModel(IRuntimeAgentRunStore? runs, ICliAgentDetector? cliAgents, Action openIntegrations)
            : this(runs, cliAgents, openIntegrations, null, null)
        {
        }

        /// <summary>
        /// Creates the page with its UI-thread hand-off and clock replaced, which tests do.
        /// </summary>
        /// <param name="runs">Where role-agent runs are recorded; null shows none.</param>
        /// <param name="cliAgents">Detects the coding-agent CLIs; null shows them as not checked.</param>
        /// <param name="openIntegrations">Opens the Integrations page.</param>
        /// <param name="post">Runs an action on the UI thread; null uses the Avalonia dispatcher.</param>
        /// <param name="clock">The current time; null uses the system clock.</param>
        public AgentsViewModel(
            IRuntimeAgentRunStore? runs,
            ICliAgentDetector? cliAgents,
            Action openIntegrations,
            Action<Action>? post,
            Func<DateTimeOffset>? clock)
        {
            ArgumentNullException.ThrowIfNull(openIntegrations);

            Title = Loc.Get("Agents.Title");
            _runs = runs;
            _cliAgents = cliAgents;
            _openIntegrations = openIntegrations;
            _post = post ?? (action => Dispatcher.UIThread.Post(action));
            _clock = clock ?? (() => DateTimeOffset.Now);

            OpenIntegrationsCommand = ReactiveCommand.Create(() => _openIntegrations());
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

            CodingAgents =
            [
                new AgentsCodingAgentItem(CliAgentKind.Codex, SelectItem, OpenIntegrationsCommand),
                new AgentsCodingAgentItem(CliAgentKind.ClaudeCode, SelectItem, OpenIntegrationsCommand)
            ];
            ApplyData();

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            _ = RefreshAsync();
        }

        /// <summary>Gets the coding-agent CLIs, Codex first.</summary>
        public ObservableCollection<AgentsCodingAgentItem> CodingAgents { get; }

        /// <summary>Gets the recent role-agent runs, newest first.</summary>
        public ObservableCollection<AgentsRoleRunItem> RoleAgents { get; } = new();

        /// <summary>Gets whether any role agent has run.</summary>
        public bool HasRoleAgents => RoleAgents.Count > 0;

        /// <summary>Gets whether no role agent has run yet.</summary>
        public bool HasNoRoleAgents => RoleAgents.Count == 0;

        /// <summary>Gets the selected role-agent run, or null when a coding agent is selected.</summary>
        public AgentsRoleRunItem? SelectedRole => _selected as AgentsRoleRunItem;

        /// <summary>Gets the selected coding agent, or null when a role-agent run is selected.</summary>
        public AgentsCodingAgentItem? SelectedCoding => _selected as AgentsCodingAgentItem;

        /// <summary>Gets whether the coding-agent check is running.</summary>
        public bool IsChecking
        {
            get => _isChecking;
            private set => this.RaiseAndSetIfChanged(ref _isChecking, value);
        }

        /// <summary>Gets the command that opens the Integrations page.</summary>
        public ICommand OpenIntegrationsCommand { get; }

        /// <summary>Gets the command that reloads runs and checks the CLIs again.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>
        /// Reloads the role-agent runs and checks the CLIs again whenever the page is shown, unless a check
        /// is still running (the first visit right after the constructor started one).
        /// </summary>
        public override void OnNavigatedTo()
        {
            if (!IsChecking)
            {
                _ = RefreshAsync();
            }
        }

        /// <summary>
        /// Reads the run store and asks the CLIs for their state off the UI thread, then publishes the result on it.
        /// </summary>
        public async Task RefreshAsync()
        {
            var version = ++_refreshVersion;
            IsChecking = _cliAgents is not null;

            var loadRuns = Task.Run<IReadOnlyList<RuntimeAgentRun>>(() =>
            {
                try
                {
                    return _runs?.List(50) ?? [];
                }
                catch (Exception)
                {
                    return [];
                }
            });
            var loadStatuses = DetectAsync();

            var loadedRuns = await loadRuns.ConfigureAwait(false);
            var statuses = await loadStatuses.ConfigureAwait(false);

            _post(() =>
            {
                if (version != _refreshVersion)
                {
                    return;
                }

                _lastRuns = loadedRuns;
                _lastStatuses = statuses;
                IsChecking = false;
                ApplyData();
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        internal static string Shorten(string? text, int maxLength = MaxObservationLength)
        {
            var flat = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return flat.Length <= maxLength ? flat : flat[..(maxLength - 1)] + "…";
        }

        private async Task<IReadOnlyList<CliAgentStatus>?> DetectAsync()
        {
            if (_cliAgents is null)
            {
                return null;
            }

            try
            {
                return await Task.Run(() => _cliAgents.DetectAsync()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return [];
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e) => _post(() =>
        {
            Title = Loc.Get("Agents.Title");
            ApplyData();
        });

        private void ApplyData()
        {
            var now = _clock();

            foreach (var item in CodingAgents)
            {
                item.Apply(_lastStatuses?.FirstOrDefault(status => status.Kind == item.Kind), _lastStatuses is not null);
            }

            RoleAgents.Clear();
            foreach (var run in _lastRuns.OrderByDescending(run => run.StartedAt))
            {
                RoleAgents.Add(new AgentsRoleRunItem(run, now, SelectItem));
            }

            this.RaisePropertyChanged(nameof(HasRoleAgents));
            this.RaisePropertyChanged(nameof(HasNoRoleAgents));

            var remembered = !_userSelected
                ? null
                : RoleAgents.Cast<AgentsListItem>().Concat(CodingAgents).FirstOrDefault(item => item.Key == _selectedKey);
            Select(remembered ?? RoleAgents.FirstOrDefault() ?? (AgentsListItem)CodingAgents[0]);
        }

        private void SelectItem(AgentsListItem item)
        {
            _userSelected = true;
            Select(item);
        }

        private void Select(AgentsListItem item)
        {
            if (_selected is not null)
            {
                _selected.IsSelected = false;
            }

            _selected = item;
            _selectedKey = item.Key;
            item.IsSelected = true;
            this.RaisePropertyChanged(nameof(SelectedRole));
            this.RaisePropertyChanged(nameof(SelectedCoding));
        }
    }

    /// <summary>
    /// A row in the Agents list that can be selected.
    /// </summary>
    public abstract class AgentsListItem : ReactiveObject
    {
        private bool _isSelected;

        /// <summary>Gets a key that identifies the row across refreshes.</summary>
        public abstract string Key { get; }

        /// <summary>Gets or sets whether the row is the selected one.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        /// <summary>Gets the command that selects the row.</summary>
        public ICommand SelectCommand { get; protected init; } = null!;
    }

    /// <summary>
    /// A coding-agent CLI in the Agents list and its detail pane.
    /// </summary>
    public sealed class AgentsCodingAgentItem : AgentsListItem
    {
        private CliAgentStatus? _status;
        private bool _checked;

        internal AgentsCodingAgentItem(CliAgentKind kind, Action<AgentsListItem> select, ICommand openIntegrations)
        {
            Kind = kind;
            OpenIntegrationsCommand = openIntegrations;
            SelectCommand = ReactiveCommand.Create(() => select(this));
        }

        /// <summary>Gets which CLI this is.</summary>
        public CliAgentKind Kind { get; }

        /// <inheritdoc />
        public override string Key => "cli:" + Kind;

        /// <summary>Gets the command that opens the Integrations page.</summary>
        public ICommand OpenIntegrationsCommand { get; }

        /// <summary>Gets the product name.</summary>
        public string Name => Kind == CliAgentKind.Codex ? "Codex CLI" : "Claude Code";

        /// <summary>Gets the executable Kam looks for on PATH.</summary>
        public string Command => Kind == CliAgentKind.Codex ? "codex" : "claude";

        /// <summary>Gets the two-letter mark shown in the row.</summary>
        public string Initials => Kind == CliAgentKind.Codex ? "CX" : "CC";

        /// <summary>Gets a sentence about what the CLI is and what Kam does with it.</summary>
        public string Description => Loc.Get(Kind == CliAgentKind.Codex ? "Agents.Cli.Codex.About" : "Agents.Cli.Claude.About");

        /// <summary>Gets the sandbox modes Kam can run the CLI in, or a note that it cannot yet.</summary>
        public string SandboxText => Loc.Get(Kind == CliAgentKind.Codex ? "Agents.Cli.Codex.Sandbox" : "Agents.Cli.Claude.Sandbox");

        /// <summary>Gets whether the CLI is on PATH, as text.</summary>
        public string InstalledText => Loc.Get(!_checked
            ? "Agents.Cli.NotChecked"
            : _status?.IsInstalled == true ? "Agents.Cli.OnPath" : "Agents.Cli.NotOnPath");

        /// <summary>Gets the sign-in state as text.</summary>
        public string SignInText => (_status?.IsInstalled == true ? _status.SignIn : CliAgentSignIn.Unknown) switch
        {
            CliAgentSignIn.Subscription => Loc.Get("Agents.Cli.SignIn.Subscription"),
            CliAgentSignIn.ApiKey => Loc.Get("Agents.Cli.SignIn.ApiKey"),
            CliAgentSignIn.SignedOut => Loc.Get("Agents.Cli.SignIn.SignedOut"),
            _ => Loc.Get("Agents.Cli.SignIn.Unknown")
        };

        /// <summary>Gets the reported version, such as "v0.161.0", or a dash.</summary>
        public string VersionText => string.IsNullOrWhiteSpace(_status?.Version) ? "—" : "v" + _status!.Version;

        /// <summary>Gets the short status for the chip.</summary>
        public string StatusText => Loc.Get(!_checked
            ? "Agents.Cli.Chip.NotChecked"
            : _status is not { IsInstalled: true }
                ? "Agents.Cli.Chip.NotInstalled"
                : _status.SignIn == CliAgentSignIn.SignedOut ? "Agents.Cli.Chip.SignedOut" : "Agents.Cli.Chip.Ready");

        /// <summary>Gets whether the CLI is installed and not signed out.</summary>
        public bool IsReady => _status is { IsInstalled: true } && _status.SignIn != CliAgentSignIn.SignedOut;

        /// <summary>Gets whether the CLI is installed but signed out.</summary>
        public bool IsSignedOut => _status is { IsInstalled: true, SignIn: CliAgentSignIn.SignedOut };

        internal void Apply(CliAgentStatus? status, bool wasChecked)
        {
            _status = status;
            _checked = wasChecked;
            this.RaisePropertyChanged(string.Empty);
        }
    }

    /// <summary>
    /// One recorded role-agent run in the Agents list and its detail pane.
    /// </summary>
    public sealed class AgentsRoleRunItem : AgentsListItem
    {
        private readonly RuntimeAgentRun _run;

        internal AgentsRoleRunItem(RuntimeAgentRun run, DateTimeOffset now, Action<AgentsListItem> select)
        {
            _run = run;
            SelectCommand = ReactiveCommand.Create(() => select(this));
            Initials = BuildInitials(AgentName);
            RelativeTimeText = AgentChatSessionViewModel.FormatRelativeTime(run.StartedAt, now);
            StatusText = Loc.Get(run.Status switch
            {
                RuntimeAgentRunStatus.Running => "Agents.Status.Running",
                RuntimeAgentRunStatus.Succeeded => "Agents.Status.Succeeded",
                RuntimeAgentRunStatus.Failed => "Agents.Status.Failed",
                _ => "Agents.Status.Canceled"
            });
            StartedText = run.StartedAt.ToLocalTime().ToString("g", LocalizationService.Instance.Culture);
            FinishedText = run.CompletedAt is { } done
                ? done.ToLocalTime().ToString("g", LocalizationService.Instance.Culture)
                : Loc.Get(run.Status == RuntimeAgentRunStatus.Running ? "Agents.Run.StillRunning" : "Agents.Run.NoEnd");
            DurationText = run.CompletedAt is { } end ? FormatDuration(end - run.StartedAt) : "—";
            Observations = (run.ToolObservations ?? [])
                .Select(observation => new AgentsToolObservationItem(
                    observation.SkillId,
                    AgentsViewModel.Shorten(observation.Summary),
                    observation.Success))
                .ToList();
        }

        /// <inheritdoc />
        public override string Key => "run:" + _run.RunId;

        /// <summary>Gets the agent's name.</summary>
        public string AgentName => string.IsNullOrWhiteSpace(_run.AgentName) ? _run.Role : _run.AgentName;

        /// <summary>Gets the role the main agent gave it.</summary>
        public string Role => _run.Role;

        /// <summary>Gets one or two letters for the avatar.</summary>
        public string Initials { get; }

        /// <summary>Gets the task text the agent was given.</summary>
        public string Task => _run.Task;

        /// <summary>Gets the model id the run used.</summary>
        public string ModelId => _run.ModelId;

        /// <summary>Gets the status text.</summary>
        public string StatusText { get; }

        /// <summary>Gets whether the run is still going.</summary>
        public bool IsRunning => _run.Status == RuntimeAgentRunStatus.Running;

        /// <summary>Gets whether the run finished.</summary>
        public bool IsSucceeded => _run.Status == RuntimeAgentRunStatus.Succeeded;

        /// <summary>Gets whether the run failed.</summary>
        public bool IsFailed => _run.Status == RuntimeAgentRunStatus.Failed;

        /// <summary>Gets how long ago the run started, such as "5m".</summary>
        public string RelativeTimeText { get; }

        /// <summary>Gets the start time.</summary>
        public string StartedText { get; }

        /// <summary>Gets the finish time, or a note that the run has not finished.</summary>
        public string FinishedText { get; }

        /// <summary>Gets how long the run took, or a dash while it has not finished.</summary>
        public string DurationText { get; }

        /// <summary>Gets what the agent's tools returned, as short plain text.</summary>
        public IReadOnlyList<AgentsToolObservationItem> Observations { get; }

        /// <summary>Gets whether the run recorded any tool observation.</summary>
        public bool HasObservations => Observations.Count > 0;

        /// <summary>Gets the agent's final answer.</summary>
        public string Response => _run.Response ?? string.Empty;

        /// <summary>Gets whether the run has a final answer.</summary>
        public bool HasResponse => !string.IsNullOrWhiteSpace(_run.Response);

        /// <summary>Gets the error text of a failed run.</summary>
        public string ErrorMessage => _run.ErrorMessage ?? string.Empty;

        /// <summary>Gets whether the run recorded an error.</summary>
        public bool HasError => !string.IsNullOrWhiteSpace(_run.ErrorMessage);

        /// <summary>Gets the latest progress message of a run that has no answer yet.</summary>
        public string LastMessage => _run.LastMessage ?? string.Empty;

        /// <summary>Gets whether a progress message is shown because there is neither answer nor error.</summary>
        public bool HasLastMessageOnly => !HasResponse && !HasError && !string.IsNullOrWhiteSpace(_run.LastMessage);

        /// <summary>Gets whether the run has neither answer, error nor progress message.</summary>
        public bool HasNoOutput => !HasResponse && !HasError && string.IsNullOrWhiteSpace(_run.LastMessage);

        /// <summary>Builds the avatar letters from an agent name: two words give their first letters, one word its first two.</summary>
        /// <param name="name">The agent name.</param>
        public static string BuildInitials(string? name)
        {
            var words = (name ?? string.Empty).Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
            return words.Length switch
            {
                0 => "A",
                1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
                _ => (words[0][..1] + words[1][..1]).ToUpperInvariant()
            };
        }

        internal static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero)
            {
                span = TimeSpan.Zero;
            }

            if (span < TimeSpan.FromMinutes(1))
            {
                return Loc.Format("Agents.Duration.Seconds", (int)span.TotalSeconds);
            }

            return span < TimeSpan.FromHours(1)
                ? Loc.Format("Agents.Duration.Minutes", (int)span.TotalMinutes, span.Seconds)
                : Loc.Format("Agents.Duration.Hours", (int)span.TotalHours, span.Minutes);
        }
    }

    /// <summary>
    /// One tool result a role agent recorded.
    /// </summary>
    /// <param name="ToolName">The skill or tool id.</param>
    /// <param name="Summary">A short plain-text summary of the result.</param>
    /// <param name="Success">Whether the tool call worked.</param>
    public sealed record AgentsToolObservationItem(string ToolName, string Summary, bool Success)
    {
        /// <summary>Gets whether the tool call failed.</summary>
        public bool Failed => !Success;
    }
}

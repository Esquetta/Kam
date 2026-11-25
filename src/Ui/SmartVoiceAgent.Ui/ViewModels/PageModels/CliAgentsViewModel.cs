using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
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
    /// The Integrations card for coding-agent CLIs: which CLIs are installed, their versions and sign-in state,
    /// and whether the agent may hand tasks to them.
    /// </summary>
    public sealed class CliAgentsViewModel : ReactiveObject
    {
        private readonly ICliAgentDetector? _detector;
        private bool _isChecking;

        /// <summary>
        /// Creates the card and starts a check when a detector is available.
        /// </summary>
        /// <param name="settings">Stores the on/off switches.</param>
        /// <param name="detector">Asks the CLIs for their state; null skips the check, as in tests.</param>
        public CliAgentsViewModel(ISettingsService settings, ICliAgentDetector? detector)
        {
            _detector = detector;
            Agents =
            [
                new CliAgentRowViewModel(CliAgentKind.Codex, settings),
                new CliAgentRowViewModel(CliAgentKind.ClaudeCode, settings)
            ];
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

            if (_detector is not null)
            {
                _ = RefreshAsync();
            }
        }

        /// <summary>Gets one row per supported CLI.</summary>
        public ObservableCollection<CliAgentRowViewModel> Agents { get; }

        /// <summary>Gets whether a check is running.</summary>
        public bool IsChecking
        {
            get => _isChecking;
            private set => this.RaiseAndSetIfChanged(ref _isChecking, value);
        }

        /// <summary>Gets the command that checks the CLIs again.</summary>
        public ICommand RefreshCommand { get; }

        /// <summary>
        /// Asks every CLI for its version and sign-in state.
        /// </summary>
        public async Task RefreshAsync()
        {
            if (_detector is null || IsChecking)
            {
                return;
            }

            IsChecking = true;
            foreach (var row in Agents)
            {
                row.Apply(null, checking: true);
            }

            IReadOnlyList<CliAgentStatus> statuses;
            try
            {
                statuses = await _detector.DetectAsync();
            }
            catch (Exception)
            {
                statuses = [];
            }

            foreach (var row in Agents)
            {
                row.Apply(statuses.FirstOrDefault(status => status.Kind == row.Kind), checking: false);
            }

            IsChecking = false;
        }

        /// <summary>
        /// Re-reads the text built in code after the interface language changes.
        /// </summary>
        public void RefreshLocalizedText()
        {
            foreach (var row in Agents)
            {
                row.RefreshLocalizedText();
            }
        }
    }

    /// <summary>
    /// One CLI in the coding-agent card.
    /// </summary>
    public sealed class CliAgentRowViewModel : ReactiveObject
    {
        private readonly ISettingsService _settings;
        private CliAgentStatus? _status;
        private bool _checking;

        /// <summary>
        /// Creates the row.
        /// </summary>
        /// <param name="kind">Which CLI.</param>
        /// <param name="settings">Stores the on/off switch.</param>
        public CliAgentRowViewModel(CliAgentKind kind, ISettingsService settings)
        {
            Kind = kind;
            _settings = settings;
        }

        /// <summary>Gets which CLI this row shows.</summary>
        public CliAgentKind Kind { get; }

        /// <summary>Gets the CLI's product name.</summary>
        public string Name => Kind == CliAgentKind.Codex ? "Codex CLI" : "Claude Code";

        /// <summary>Gets the command that starts the CLI.</summary>
        public string Command => Kind == CliAgentKind.Codex ? "codex" : "claude";

        /// <summary>Gets the reported version, such as "v0.161.0", or empty.</summary>
        public string VersionText => string.IsNullOrWhiteSpace(_status?.Version) ? string.Empty : "v" + _status!.Version;

        /// <summary>Gets the status badge.</summary>
        public IntegrationBadge Badge => (_checking, _status) switch
        {
            (true, _) => IntegrationBadge.Neutral(Loc.Get("Integrations.CliAgents.Status.Checking")),
            (_, null) => IntegrationBadge.Neutral(Loc.Get("Integrations.CliAgents.Status.NotChecked")),
            (_, { IsInstalled: false }) => IntegrationBadge.Neutral(Loc.Get("Integrations.CliAgents.Status.NotInstalled")),
            (_, { SignIn: CliAgentSignIn.Subscription }) => IntegrationBadge.Success(Loc.Get(Kind == CliAgentKind.Codex
                ? "Integrations.CliAgents.Status.ChatGpt"
                : "Integrations.CliAgents.Status.ClaudePlan")),
            (_, { SignIn: CliAgentSignIn.ApiKey }) => IntegrationBadge.Warning(Loc.Get("Integrations.CliAgents.Status.ApiKey")),
            (_, { SignIn: CliAgentSignIn.SignedOut }) => IntegrationBadge.Warning(Loc.Get("Integrations.CliAgents.Status.SignedOut")),
            _ => IntegrationBadge.Neutral(Loc.Get("Integrations.CliAgents.Status.Unknown"))
        };

        /// <summary>Gets the badge text.</summary>
        public string StatusText => Badge.Text;

        /// <summary>Gets what the user should know or do next.</summary>
        public string Note
        {
            get
            {
                if (Kind == CliAgentKind.ClaudeCode)
                {
                    return Loc.Get("Integrations.CliAgents.Claude.NotOffered");
                }

                return _status switch
                {
                    null => Loc.Get("Integrations.CliAgents.Codex.Ready"),
                    { IsInstalled: false } => Loc.Get("Integrations.CliAgents.Codex.Install"),
                    { SignIn: CliAgentSignIn.SignedOut } => Loc.Get("Integrations.CliAgents.Codex.SignIn"),
                    { SignIn: CliAgentSignIn.ApiKey } => Loc.Get("Integrations.CliAgents.Codex.ApiKey"),
                    _ => Loc.Get("Integrations.CliAgents.Codex.Ready")
                };
            }
        }

        /// <summary>
        /// Gets whether the switch can be changed. Claude Code stays off for now; Codex can be switched on once found,
        /// and off at any time.
        /// </summary>
        public bool CanToggle => Kind == CliAgentKind.Codex && (IsEnabled || _status?.IsInstalled == true);

        /// <summary>Gets or sets whether the agent may hand tasks to this CLI.</summary>
        public bool IsEnabled
        {
            get => Kind == CliAgentKind.Codex && _settings.CodexCliEnabled;
            set
            {
                if (Kind != CliAgentKind.Codex || _settings.CodexCliEnabled == value)
                {
                    return;
                }

                _settings.CodexCliEnabled = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(CanToggle));
            }
        }

        internal void Apply(CliAgentStatus? status, bool checking)
        {
            _checking = checking;
            if (!checking)
            {
                _status = status;
            }

            this.RaisePropertyChanged(nameof(VersionText));
            this.RaisePropertyChanged(nameof(CanToggle));
            RefreshLocalizedText();
        }

        internal void RefreshLocalizedText()
        {
            this.RaisePropertyChanged(nameof(Badge));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(Note));
        }
    }
}

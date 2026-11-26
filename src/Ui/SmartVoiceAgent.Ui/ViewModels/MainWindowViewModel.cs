using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.SlashCommands;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private TrayIconService? _trayIconService;
        private ICommandInputService? _commandInput;
        private CancellationTokenSource? _resultListenerCts;
        private IVoiceAgentHostControl? _hostControl;
        private ISkillHealthService? _skillHealthService;
        private ISkillEvalHarness? _skillEvalHarness;
        private ISkillEvalCaseCatalog? _skillEvalCaseCatalog;
        private ISkillConfirmationService? _skillConfirmationService;
        private ISkillPolicyManager? _skillPolicyManager;
        private ISkillImportService? _skillImportService;
        private ISkillTestService? _skillTestService;
        private ISkillExecutionHistoryService? _skillExecutionHistoryService;
        private ISkillPlannerTraceStore? _skillPlannerTraceStore;
        private ISlashCommandService? _slashCommandService;
        private IAgentCommandCatalog? _agentCommands;
        private IMcpHost? _mcpHost;
        private ICliAgentDetector? _cliAgentDetector;
        private IAgentPluginCatalog? _pluginCatalog;
        private IAgentSkillCatalog? _skillCatalog;
        private IRuntimeAgentRunStore? _runtimeAgentRunStore;
        private ISubagentRunTracker? _subagentRuns;
        private IApplicationUpdateService? _applicationUpdateService;
        private IApplicationRestartPlanner? _applicationRestartPlanner;
        private IApplicationVersionProvider? _applicationVersionProvider;
        private IApplicationUpdateSession? _applicationUpdateSession;
        private IGitHubAppClient? _githubAppClient;
        private IGitHubAppClientFactory? _githubAppClientFactory;
        private readonly IModelConnectionTestService _modelConnectionTestService = new ModelConnectionTestService();
        private readonly ISettingsService _pageSettingsService;
        private readonly Dictionary<NavView, ViewModelBase> _viewModelCache = [];

        /// <summary>
        /// What the title bar status shows; the text follows the interface language.
        /// </summary>
        public enum HeaderStatus
        {
            /// <summary>The app is still connecting the agent or the command loop.</summary>
            Starting,

            /// <summary>The agent has a model and waits for a message.</summary>
            AgentReady,

            /// <summary>An agent turn is running.</summary>
            AgentWorking,

            /// <summary>The agent is on but Settings have no usable model.</summary>
            NeedsModel,

            /// <summary>The agent is off (<c>AgentRuntime:Enabled=false</c>) and the command loop runs commands.</summary>
            CommandModeOn,

            /// <summary>The agent is off and the command loop is paused.</summary>
            CommandModePaused,

            /// <summary>The background host failed to start.</summary>
            FailedToStart
        }

        private HeaderStatus _headerStatus = HeaderStatus.Starting;
        private string? _hostFailureReason;

        /* ========================= */
        /* CACHED BRUSHES */
        /* ========================= */
        // Static brushes to avoid repeated allocations
        private static readonly IBrush OnlineStatusColor = new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));
        private static readonly IBrush OfflineStatusColor = new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#EF4444"));
        private static readonly IBrush WorkingStatusColor = new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#8B7CFF"));
        private static readonly IBrush AttentionStatusColor = new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#F59E0B"));
        private static readonly IBrush IdleStatusColor = new ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#8A8A99"));

        /* ========================= */
        /* NAVIGATION */
        /* ========================= */

        private ViewModelBase? _currentViewModel;
        public ViewModelBase? CurrentViewModel
        {
            get => _currentViewModel;
            private set => this.RaiseAndSetIfChanged(ref _currentViewModel, value);
        }

        private NavView _activeView = NavView.Coordinator;
        public NavView ActiveView
        {
            get => _activeView;
            private set
            {
                this.RaiseAndSetIfChanged(ref _activeView, value);
                this.RaisePropertyChanged(nameof(IsChatWorkbenchVisible));
                this.RaisePropertyChanged(nameof(IsPageHostVisible));
                this.RaisePropertyChanged(nameof(IsInboxVisible));
            }
        }

        public bool IsChatWorkbenchVisible => ActiveView == NavView.Coordinator;

        public bool IsPageHostVisible => ActiveView is not (NavView.Coordinator or NavView.Inbox);

        /* ========================= */
        /* SYSTEM STATUS (HEADER) */
        /* ========================= */

        private bool _isHostRunning = true;
        // Note: StatusText and StatusColor backing fields are inherited from ViewModelBase

        public bool IsHostRunning
        {
            get => _isHostRunning;
            private set
            {
                // Always notify, even if value is the same
                _isHostRunning = value;
                this.RaisePropertyChanged(nameof(IsHostRunning));
                UpdateStatusProperties();
            }
        }

        /// <summary>
        /// Gets the title bar status: the agent's state, or the command loop's when the agent is off.
        /// </summary>
        public override string StatusText
        {
            get => base.StatusText;
            protected set => base.StatusText = value;
        }

        /// <summary>
        /// Gets the color of the title bar status dot.
        /// </summary>
        public override IBrush StatusColor
        {
            get => base.StatusColor;
            protected set => base.StatusColor = value;
        }

        /// <summary>
        /// Gets what the title bar status shows.
        /// </summary>
        public HeaderStatus CurrentHeaderStatus => _headerStatus;

        /// <summary>
        /// Gets whether the title bar shows a status. It shows nothing while the agent is ready.
        /// </summary>
        public bool ShowsHeaderStatus => _headerStatus != HeaderStatus.AgentReady;

        /// <summary>
        /// Gets whether the title bar status asks the user to act (no model yet), so it shows in the warning color.
        /// </summary>
        public bool IsHeaderStatusWarning => _headerStatus == HeaderStatus.NeedsModel;

        /// <summary>
        /// Gets the title bar status tooltip, which says what the state means and what a click does.
        /// </summary>
        public string? StatusToolTip => _headerStatus switch
        {
            HeaderStatus.AgentReady => Loc.Format(
                "Workbench.Status.AgentReadyTip",
                AiRuntimeConfigurationMapper.ResolveChatProfile(_pageSettingsService)?.ModelId),
            HeaderStatus.AgentWorking => Loc.Get("Workbench.Status.AgentWorkingTip"),
            HeaderStatus.NeedsModel => Loc.Get("Workbench.Status.NeedsModelTip"),
            HeaderStatus.CommandModeOn => Loc.Get("Workbench.Status.CommandModeOnTip"),
            HeaderStatus.CommandModePaused => Loc.Get("Workbench.Status.CommandModePausedTip"),
            HeaderStatus.FailedToStart => _hostFailureReason,
            _ => null
        };

        /// <summary>
        /// Acts on the title bar status: opens Settings when a model is missing, pauses or resumes the
        /// command loop when the agent is off, and otherwise shows the chat.
        /// </summary>
        public ICommand HeaderStatusCommand { get; }

        /// <summary>
        /// Gets whether the agent has no model to talk to yet, so the workbench shows the setup card.
        /// </summary>
        public bool NeedsModelSetup => _headerStatus == HeaderStatus.NeedsModel;

        /// <summary>Gets whether an agent turn is running, so the top bar's status dot uses the accent color.</summary>
        public bool IsHeaderStatusWorking => _headerStatus == HeaderStatus.AgentWorking;

        /// <summary>
        /// Opens Settings with the chat model's provider set to the one picked on the setup card
        /// ("OpenRouter", "OpenAI", "Anthropic" or "Ollama").
        /// </summary>
        public ICommand SetUpModelCommand { get; }

        private void UpdateStatusProperties()
        {
            _headerStatus = ResolveHeaderStatus();
            base.StatusText = FormatHeaderStatus(_headerStatus);
            base.StatusColor = _headerStatus switch
            {
                HeaderStatus.AgentReady or HeaderStatus.CommandModeOn => OnlineStatusColor,
                HeaderStatus.AgentWorking => WorkingStatusColor,
                HeaderStatus.NeedsModel => AttentionStatusColor,
                HeaderStatus.FailedToStart => OfflineStatusColor,
                _ => IdleStatusColor
            };

            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(StatusColor));
            this.RaisePropertyChanged(nameof(StatusToolTip));
            this.RaisePropertyChanged(nameof(CurrentHeaderStatus));
            this.RaisePropertyChanged(nameof(ShowsHeaderStatus));
            this.RaisePropertyChanged(nameof(IsHeaderStatusWarning));
            this.RaisePropertyChanged(nameof(IsHeaderStatusWorking));
            this.RaisePropertyChanged(nameof(NeedsModelSetup));
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private HeaderStatus ResolveHeaderStatus()
        {
            if (_hostFailureReason is not null)
            {
                return HeaderStatus.FailedToStart;
            }

            if (_agentRuntime is not null)
            {
                if (IsAgentTurnRunning)
                {
                    return HeaderStatus.AgentWorking;
                }

                return AiRuntimeConfigurationMapper.ResolveChatProfile(_pageSettingsService) is null
                    ? HeaderStatus.NeedsModel
                    : HeaderStatus.AgentReady;
            }

            if (_hostControl is null)
            {
                return HeaderStatus.Starting;
            }

            return IsHostRunning ? HeaderStatus.CommandModeOn : HeaderStatus.CommandModePaused;
        }

        private void SetUpModel(string? provider)
        {
            NavigateTo(NavView.Settings);
            if (!string.IsNullOrWhiteSpace(provider) && GetOrCreateViewModel(NavView.Settings) is SettingsViewModel settings)
            {
                settings.ChatProvider = provider;
            }
        }

        private void OnHeaderStatusClicked()
        {
            switch (_headerStatus)
            {
                case HeaderStatus.NeedsModel:
                    NavigateTo(NavView.Settings);
                    break;
                case HeaderStatus.CommandModeOn:
                case HeaderStatus.CommandModePaused:
                    _ = ToggleHostAsync();
                    break;
                case HeaderStatus.FailedToStart:
                    NavigateTo(NavView.Diagnostics);
                    break;
                case HeaderStatus.AgentReady:
                case HeaderStatus.AgentWorking:
                    NavigateTo(NavView.Coordinator);
                    break;
            }
        }

        /// <summary>
        /// Shows a host start failure in the title bar status and the activity log.
        /// </summary>
        /// <param name="reason">Short reason shown to the user.</param>
        public void ReportHostStartFailure(string reason)
        {
            _hostFailureReason = string.IsNullOrWhiteSpace(reason) ? Loc.Get("Workbench.Status.AgentFailed") : reason;
            if (Dispatcher.UIThread.CheckAccess())
            {
                UpdateStatusProperties();
            }
            else
            {
                Dispatcher.UIThread.Post(UpdateStatusProperties);
            }

            AddLog($"Agent host failed to start: {reason}");
        }

        private static string FormatHeaderStatus(HeaderStatus status)
        {
            return status switch
            {
                HeaderStatus.AgentReady => Loc.Get("Workbench.Status.AgentReady"),
                HeaderStatus.AgentWorking => Loc.Get("Workbench.Status.AgentWorking"),
                HeaderStatus.NeedsModel => Loc.Get("Workbench.Status.NeedsModel"),
                HeaderStatus.CommandModeOn => Loc.Get("Workbench.Status.CommandModeOn"),
                HeaderStatus.CommandModePaused => Loc.Get("Workbench.Status.CommandModePaused"),
                HeaderStatus.FailedToStart => Loc.Get("Workbench.Status.AgentFailed"),
                _ => Loc.Get("Workbench.Status.Starting")
            };
        }

        /* ========================= */
        /* COMMANDS */
        /* ========================= */

        public ICommand NavigateToCoordinatorCommand { get; }
        public ICommand NavigateToDiagnosticsCommand { get; }
        public ICommand NavigateToPluginsCommand { get; }
        public ICommand NavigateToExtensionsCommand { get; }
        public ICommand NavigateToIntegrationsCommand { get; }
        public ICommand NavigateToSettingsCommand { get; }

        /* ========================= */
        /* LOGGING */
        /* ========================= */

        private ObservableCollection<string> _logEntries = new();
        public ObservableCollection<string> LogEntries
        {
            get => _logEntries;
            set => this.RaiseAndSetIfChanged(ref _logEntries, value);
        }

        /* ========================= */
        /* THEME */
        /* ========================= */

        private bool _isDarkMode;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDarkMode, value);
                ApplyTheme(value);
                var theme = value ? "dark" : "light";
                if (_pageSettingsService.Theme != theme)
                {
                    _pageSettingsService.Theme = theme;
                }
            }
        }

        /* ========================= */
        /* ASSISTANT ORB */
        /* ========================= */

        private IBrush _currentOrbColor = Brush.Parse("#00D4FF");
        public IBrush CurrentOrbColor
        {
            get => _currentOrbColor;
            set => this.RaiseAndSetIfChanged(ref _currentOrbColor, value);
        }

        private double _taskProgress;
        public double TaskProgress
        {
            get => _taskProgress;
            set => this.RaiseAndSetIfChanged(ref _taskProgress, value);
        }

        /* ========================= */
        /* COMMAND INPUT */
        /* ========================= */

        private string _commandInputText = string.Empty;
        public string CommandInputText
        {
            get => _commandInputText;
            set
            {
                var normalizedValue = value ?? string.Empty;
                if (_commandInputText == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _commandInputText, normalizedValue);
                this.RaisePropertyChanged(nameof(HasComposerText));
                RefreshSlashCommandSuggestions();
            }
        }

        /// <summary>Gets whether the composer holds text; an empty composer shows a muted send button.</summary>
        public bool HasComposerText => !string.IsNullOrWhiteSpace(CommandInputText);

        public ICommand SubmitCommand { get; }

        public ICommand SelectSlashCommandCommand { get; }

        public ICommand NewAgentChatCommand { get; }

        public ICommand SelectAgentChatCommand { get; }

        public ICommand RemoveComposerAttachmentCommand { get; }

        public ICommand ClearComposerAttachmentsCommand { get; }

        /// <summary>
        /// Gets the command that copies a starter prompt into the composer.
        /// </summary>
        public ICommand UseComposerSuggestionCommand { get; }

        private ObservableCollection<ComposerAttachmentViewModel> _composerAttachments = new();
        public ObservableCollection<ComposerAttachmentViewModel> ComposerAttachments
        {
            get => _composerAttachments;
            set
            {
                this.RaiseAndSetIfChanged(ref _composerAttachments, value);
                this.RaisePropertyChanged(nameof(HasComposerAttachments));
            }
        }

        public bool HasComposerAttachments => ComposerAttachments.Count > 0;

        private ObservableCollection<AgentChatSessionViewModel> _agentChatSessions = new();
        public ObservableCollection<AgentChatSessionViewModel> AgentChatSessions
        {
            get => _agentChatSessions;
            set => this.RaiseAndSetIfChanged(ref _agentChatSessions, value);
        }

        private AgentChatSessionViewModel? _selectedAgentChatSession;
        public AgentChatSessionViewModel? SelectedAgentChatSession
        {
            get => _selectedAgentChatSession;
            private set
            {
                if (_selectedAgentChatSession == value)
                {
                    return;
                }

                if (_selectedAgentChatSession is not null)
                {
                    _selectedAgentChatSession.IsSelected = false;
                }

                this.RaiseAndSetIfChanged(ref _selectedAgentChatSession, value);

                if (_selectedAgentChatSession is not null)
                {
                    _selectedAgentChatSession.IsSelected = true;
                }

                this.RaisePropertyChanged(nameof(HasAgentChatMessages));
                RefreshAgentChatModelOptions();
                WatchSelectedSessionContext(_selectedAgentChatSession);
            }
        }

        public bool HasAgentChatMessages => SelectedAgentChatSession?.Messages.Count > 0;

        public string AgentChatSessionCountText =>
            AgentChatSessions.Count == 1
                ? Loc.Get("Workbench.Thread.OneThread")
                : Loc.Format("Workbench.Thread.Threads", AgentChatSessions.Count);

        private ObservableCollection<SlashCommandSuggestionViewModel> _slashCommandSuggestions = new();
        public ObservableCollection<SlashCommandSuggestionViewModel> SlashCommandSuggestions
        {
            get => _slashCommandSuggestions;
            set => this.RaiseAndSetIfChanged(ref _slashCommandSuggestions, value);
        }

        private bool _isSlashCommandPaletteVisible;
        public bool IsSlashCommandPaletteVisible
        {
            get => _isSlashCommandPaletteVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSlashCommandPaletteVisible, value);
        }

        private int _selectedSlashCommandIndex = -1;
        public int SelectedSlashCommandIndex
        {
            get => _selectedSlashCommandIndex;
            private set
            {
                if (_selectedSlashCommandIndex == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedSlashCommandIndex, value);
                UpdateSlashCommandSelection();
            }
        }

        private ObservableCollection<PendingSkillConfirmationViewModel> _pendingSkillConfirmations = new();
        public ObservableCollection<PendingSkillConfirmationViewModel> PendingSkillConfirmations
        {
            get => _pendingSkillConfirmations;
            set => this.RaiseAndSetIfChanged(ref _pendingSkillConfirmations, value);
        }

        private bool _hasPendingSkillConfirmations;
        public bool HasPendingSkillConfirmations
        {
            get => _hasPendingSkillConfirmations;
            private set => this.RaiseAndSetIfChanged(ref _hasPendingSkillConfirmations, value);
        }

        /* ========================= */
        /* CONSTRUCTOR */
        /* ========================= */

        public MainWindowViewModel()
            : this(new JsonSettingsService())
        {
        }

        /// <summary>
        /// Creates the view model over a specific settings store.
        /// </summary>
        /// <param name="settingsService">Settings the pages and the chat model picker read.</param>
        public MainWindowViewModel(ISettingsService settingsService)
        {
            _pageSettingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));

            // Commands
            NavigateToCoordinatorCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Coordinator));
            NavigateToDiagnosticsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Diagnostics));
            NavigateToPluginsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Plugins));
            NavigateToExtensionsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Extensions));
            NavigateToIntegrationsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Integrations));
            NavigateToSettingsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Settings));
            HeaderStatusCommand = ReactiveCommand.Create(OnHeaderStatusClicked);
            SetUpModelCommand = ReactiveCommand.Create<string?>(SetUpModel);
            SubmitCommand = ReactiveCommand.CreateFromTask(SubmitCommandInputAsync);
            SelectSlashCommandCommand = ReactiveCommand.Create<SlashCommandSuggestionViewModel>(SelectSlashCommand);
            NewAgentChatCommand = ReactiveCommand.Create(CreateNewAgentChat);
            SelectAgentChatCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(SelectAgentChat);
            RemoveComposerAttachmentCommand = ReactiveCommand.Create<ComposerAttachmentViewModel?>(RemoveComposerAttachment);
            ClearComposerAttachmentsCommand = ReactiveCommand.Create(ClearComposerAttachments);
            UseComposerSuggestionCommand = ReactiveCommand.Create<string?>(UseComposerSuggestion);
            ApproveToolCallCommand = ReactiveCommand.Create<AgentChatMessageViewModel?>(step => ResolveToolApproval(step, approved: true, alwaysAllow: false));
            AlwaysAllowToolCallCommand = ReactiveCommand.Create<AgentChatMessageViewModel?>(step => ResolveToolApproval(step, approved: true, alwaysAllow: true));
            DenyToolCallCommand = ReactiveCommand.Create<AgentChatMessageViewModel?>(step => ResolveToolApproval(step, approved: false, alwaysAllow: false));
            InitializeInbox();
            InitializeBuild();
            InitializeTaskContext();
            InitializeAppearance();
            InitializePaneWidths();
            InitializeRailGroups();
            StopAgentTurnCommand = ReactiveCommand.Create(StopAgentTurn);
            ClearQueuedAgentMessagesCommand = ReactiveCommand.Create(ClearQueuedAgentMessages);
            InitializeChatExperience();
            InitializeAgentChatSessions();
            InitializeVoice();

            // Text built in code follows the interface language; Cleanup unsubscribes.
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            Dispatcher.UIThread.Post(() =>
            {
                // Theme and accent color as saved in Settings > Appearance
                ApplySavedAppearance();

                // Initialize View - CoordinatorViewModel will be created by SetVoiceAgentHostControl
                // when the host control is available. For now, leave CurrentViewModel null
                // or create a placeholder if needed.
                ActiveView = NavView.Coordinator;

                // Initial system log only - no simulation
                AddLog("AGENT_RUNTIME_READY");
                AddLog("WORKSPACE_READY");
            }, DispatcherPriority.Background);
        }

        /* ========================= */
        /* HOST CONTROL */
        /* ========================= */

        /// <summary>
        /// Sets the VoiceAgent host control service
        /// </summary>
        public void SetVoiceAgentHostControl(IVoiceAgentHostControl hostControl)
        {
            _hostControl = hostControl;
            
            // Subscribe to state changes
            _hostControl.StateChanged += OnHostStateChanged;
            
            // Set initial state
            IsHostRunning = _hostControl.IsRunning;
            
            // Always recreate CoordinatorViewModel with host control if on Coordinator view
            // or if no view model is set yet
            if (CurrentViewModel is CoordinatorViewModel || CurrentViewModel == null)
            {
                CurrentViewModel = new CoordinatorViewModel(_hostControl, this);
                _viewModelCache[NavView.Coordinator] = CurrentViewModel;
                ActiveView = NavView.Coordinator;
            }
        }

        public void SetSkillHealthService(ISkillHealthService skillHealthService)
        {
            _skillHealthService = skillHealthService;
        }

        public void SetSkillEvalServices(
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog)
        {
            _skillEvalHarness = skillEvalHarness;
            _skillEvalCaseCatalog = skillEvalCaseCatalog;
        }

        public void SetSkillPolicyManager(ISkillPolicyManager skillPolicyManager)
        {
            _skillPolicyManager = skillPolicyManager;
        }

        public void SetSkillImportService(ISkillImportService skillImportService)
        {
            _skillImportService = skillImportService;
        }

        public void SetSkillTestService(ISkillTestService skillTestService)
        {
            _skillTestService = skillTestService;
        }

        public void SetSkillConfirmationService(ISkillConfirmationService skillConfirmationService)
        {
            if (_skillConfirmationService is not null)
            {
                _skillConfirmationService.PendingChanged -= OnPendingSkillConfirmationsChanged;
            }

            _skillConfirmationService = skillConfirmationService;
            _skillConfirmationService.PendingChanged += OnPendingSkillConfirmationsChanged;
            RefreshPendingSkillConfirmations();
        }

        public void SetSkillExecutionHistoryService(ISkillExecutionHistoryService skillExecutionHistoryService)
        {
            _skillExecutionHistoryService = skillExecutionHistoryService;
        }

        public void SetSkillPlannerTraceStore(ISkillPlannerTraceStore skillPlannerTraceStore)
        {
            _skillPlannerTraceStore = skillPlannerTraceStore;
        }

        public void SetSlashCommandService(ISlashCommandService slashCommandService)
        {
            _slashCommandService = slashCommandService;
            RefreshSlashCommandSuggestions();
        }

        public void SetRuntimeAgentRunStore(IRuntimeAgentRunStore runtimeAgentRunStore)
        {
            _runtimeAgentRunStore = runtimeAgentRunStore;
        }

        /// <summary>Connects the Runs and Graphs pages to the subagent runs tasks start.</summary>
        /// <param name="subagentRuns">The subagent run tracker.</param>
        public void SetSubagentRunTracker(ISubagentRunTracker subagentRuns)
        {
            _subagentRuns = subagentRuns;
        }

        public void SetApplicationUpdateServices(
            IApplicationUpdateService applicationUpdateService,
            IApplicationRestartPlanner applicationRestartPlanner,
            IApplicationVersionProvider applicationVersionProvider,
            IApplicationUpdateSession applicationUpdateSession)
        {
            _applicationUpdateService = applicationUpdateService;
            _applicationRestartPlanner = applicationRestartPlanner;
            _applicationVersionProvider = applicationVersionProvider;
            _applicationUpdateSession = applicationUpdateSession;
        }

        public void SetGitHubAppClient(IGitHubAppClient githubAppClient)
        {
            _githubAppClient = githubAppClient;
        }

        public void SetGitHubAppClientFactory(IGitHubAppClientFactory githubAppClientFactory)
        {
            _githubAppClientFactory = githubAppClientFactory;
        }

        /// <summary>
        /// Supplies the detector the Integrations page uses to show the coding-agent CLIs.
        /// </summary>
        public void SetCliAgentDetector(ICliAgentDetector detector)
        {
            _cliAgentDetector = detector;
            if (_viewModelCache.Remove(NavView.Integrations, out var stale)
                && stale is IDisposable disposable
                && !ReferenceEquals(stale, CurrentViewModel))
            {
                // The cached page listens to LanguageChanged; dispose it so it does not outlive the cache.
                disposable.Dispose();
            }
        }
        
        private void OnHostStateChanged(object? sender, bool isRunning)
        {
            
            // Update state immediately on UI thread with maximum priority
            Dispatcher.UIThread.Post(() =>
            {
                
                // Use the property setter to ensure proper notification
                IsHostRunning = isRunning;
                
                AddLog(isRunning ? "🟢 VoiceAgent Host started" : "🔴 VoiceAgent Host stopped");
                
                // Also update the CoordinatorViewModel if it's active
                if (CurrentViewModel is CoordinatorViewModel coordinator)
                {
                    coordinator.SyncWithHostState(isRunning);
                }
            }, DispatcherPriority.Send);
        }

        /// <summary>
        /// Toggles the VoiceAgent Host on/off
        /// </summary>
        public async Task ToggleHostAsync()
        {
            
            if (_hostControl == null)
            {
                return;
            }

            
            if (_hostControl.IsRunning)
            {
                await _hostControl.StopAsync();
            }
            else
            {
                await _hostControl.StartAsync();
            }
        }

        /* ========================= */
        /* NAVIGATION */
        /* ========================= */

        private void NavigateTo(NavView view)
        {
            // Ensure we are on UI thread
             if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => NavigateTo(view));
                return;
            }

            if (ActiveView == view && CurrentViewModel != null)
                return;

            CurrentViewModel?.OnNavigatedFrom();
            ActiveView = view;

            CurrentViewModel = GetOrCreateViewModel(view);

            CurrentViewModel?.OnNavigatedTo();
            AddLog($"NAVIGATED_TO: {view.ToString().ToUpper()}");
        }

        private ViewModelBase? GetOrCreateViewModel(NavView view)
        {
            if (_viewModelCache.TryGetValue(view, out var cachedViewModel))
            {
                return cachedViewModel;
            }

            ViewModelBase? viewModel = view switch
            {
                NavView.Coordinator => new CoordinatorViewModel(_hostControl, this),
                NavView.Diagnostics => new RuntimeDiagnosticsViewModel(
                    _pageSettingsService,
                    _hostControl,
                    _skillHealthService,
                    _modelConnectionTestService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog,
                    _skillExecutionHistoryService,
                    _skillPlannerTraceStore,
                    _applicationUpdateService,
                    _applicationRestartPlanner,
                    _applicationVersionProvider,
                    _githubAppClient,
                    _applicationUpdateSession,
                    CopyRuntimeDiagnosticsText),
                NavView.Plugins => CreatePluginsViewModel(),
                NavView.Extensions => new ExtensionsViewModel(_mcpHost, _pluginCatalog, _skillCatalog, _agentCommands),
                NavView.Integrations => new IntegrationsViewModel(
                    _pageSettingsService,
                    _githubAppClientFactory,
                    cliAgentDetector: _cliAgentDetector),
                NavView.Settings => new SettingsViewModel(_pageSettingsService, this),
                NavView.Prompts => new PromptsViewModel(_agentCommands, UsePromptInNewTask),
                NavView.Agents => new AgentsViewModel(_runtimeAgentRunStore, _cliAgentDetector, () => NavigateTo(NavView.Integrations)),
                NavView.Loops => new LoopsViewModel(_pageSettingsService),
                NavView.Runs => new RunsViewModel(_agentSessionStore, _runtimeAgentRunStore, OpenTaskById, LiveTaskStatus, _subagentRuns, OpenSubagentGraph),
                NavView.Graphs => new GraphsViewModel(_subagentRuns, _agentSessionStore, OpenTaskById, LiveTaskStatus),
                _ => null
            };

            if (viewModel is not null)
            {
                _viewModelCache[view] = viewModel;
            }

            return viewModel;
        }

        private PluginsViewModel CreatePluginsViewModel()
        {
            if (_skillHealthService is not null
                && _skillEvalHarness is not null
                && _skillEvalCaseCatalog is not null
                && _skillPolicyManager is not null
                && _skillImportService is not null
                && _skillTestService is not null)
            {
                return new PluginsViewModel(
                    _skillHealthService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog,
                    _skillPolicyManager,
                    _skillImportService,
                    _skillTestService);
            }

            if (_skillHealthService is not null
                && _skillEvalHarness is not null
                && _skillEvalCaseCatalog is not null
                && _skillPolicyManager is not null)
            {
                return new PluginsViewModel(
                    _skillHealthService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog,
                    _skillPolicyManager);
            }

            if (_skillHealthService is not null
                && _skillEvalHarness is not null
                && _skillEvalCaseCatalog is not null
                && _skillImportService is not null)
            {
                return new PluginsViewModel(
                    _skillHealthService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog,
                    _skillImportService);
            }

            if (_skillHealthService is not null
                && _skillEvalHarness is not null
                && _skillEvalCaseCatalog is not null)
            {
                return new PluginsViewModel(
                    _skillHealthService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog);
            }

            if (_skillHealthService is not null && _skillPolicyManager is not null)
            {
                return new PluginsViewModel(_skillHealthService, _skillPolicyManager);
            }

            if (_skillHealthService is not null && _skillImportService is not null)
            {
                return new PluginsViewModel(_skillHealthService, _skillImportService);
            }

            if (_skillHealthService is not null && _skillTestService is not null)
            {
                return new PluginsViewModel(_skillHealthService, _skillTestService);
            }

            return _skillHealthService is null
                ? new PluginsViewModel()
                : new PluginsViewModel(_skillHealthService);
        }

        /* ========================= */
        /* THEME */
        /* ========================= */


        private void ApplyTheme(bool isDark)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (global::Avalonia.Application.Current is not { } app)
                    return;

                app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
                AddLog($"THEME_CHANGED: {(isDark ? "DARK" : "LIGHT")}");
            });
        }

        public void ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
        }

        /* ========================= */
        /* TRAY ICON */
        /* ========================= */

        /// <summary>
        /// Supplies the services the Extensions page manages.
        /// </summary>
        public void SetExtensionServices(
            IMcpHost? mcpHost,
            IAgentPluginCatalog? plugins,
            IAgentSkillCatalog? skills,
            IAgentCommandCatalog? commands)
        {
            _mcpHost = mcpHost;
            _pluginCatalog = plugins;
            _skillCatalog = skills;
            _agentCommands ??= commands;
            _viewModelCache.Remove(NavView.Extensions);
        }

        public void SetTrayIconService(TrayIconService service)
        {
            _trayIconService = service;

            // Wire up tray icon menu events
            service.ShowWindowRequested += (s, e) =>
            {
                ShowMainWindow();
            };

            service.NavigateRequested += (s, view) =>
            {
                NavigateTo(view);
            };

            service.ToggleVoiceRequested += (s, e) => ToggleWakeWordCommand.Execute(null);
            service.TalkRequested += (s, e) => ToggleTalk();

            service.NewTaskRequested += (s, e) => StartNewTask();

            service.AboutRequested += (s, e) =>
            {
                // Show about info in coordinator or log
                AddLog("Kam - AI Workstation Assistant v1.0");
                AddLog("Built with .NET 9.0 and Avalonia UI");
            };

            service.ExitRequested += (s, e) =>
            {
                if (global::Avalonia.Application.Current?.ApplicationLifetime
                    is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            };

            // Sync initial voice state
            service.SetVoiceEnabled(IsWakeWordEnabled);
        }

        public void SetCommandInputService(ICommandInputService commandInput)
        {
            _commandInput = commandInput;
            _commandInput.OnResult += OnCommandResult;
            
            // Start listening for results
            _resultListenerCts = new CancellationTokenSource();
        }

        private void InitializeAgentChatSessions()
        {
            var session = AgentChatSessionViewModel.CreateWorkspaceChat(DateTimeOffset.Now);
            AgentChatSessions.Add(session);
            SelectedAgentChatSession = session;
            RaiseAgentChatStateChanged();
        }

        private void CreateNewAgentChat()
        {
            // A new chat matches no search yet, so the search is cleared to show it.
            AgentChatSearchText = string.Empty;
            var session = AgentChatSessionViewModel.CreateNewChat(DateTimeOffset.Now);
            AgentChatSessions.Insert(0, session);
            SelectAgentChat(session);
            RaiseAgentChatStateChanged();
        }

        private void UseComposerSuggestion(string? suggestion)
        {
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                return;
            }

            CommandInputText = suggestion;
        }

        private void SelectAgentChat(AgentChatSessionViewModel? session)
        {
            if (session is null || !AgentChatSessions.Contains(session))
            {
                return;
            }

            SelectedAgentChatSession = session;
            RaiseAgentChatStateChanged();

            if (session.NeedsHistoryLoad)
            {
                _ = LoadAgentSessionHistoryAsync(session);
            }
        }

        private void AddAgentChatMessage(string role, string content)
        {
            if (SelectedAgentChatSession is not null)
            {
                AddAgentChatMessage(SelectedAgentChatSession, role, content);
            }
        }

        private void AddAgentChatMessage(AgentChatSessionViewModel session, string role, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            session.AddMessage(new AgentChatMessageViewModel(
                role,
                content,
                AgentChatMessageViewModel.FormatTime(DateTime.Now)));

            if (role.Equals(AgentChatMessageViewModel.UserRole, StringComparison.OrdinalIgnoreCase)
                && !session.HasCustomTitle
                && session.HasPlaceholderTitle)
            {
                session.Title = content.Length <= 40
                    ? content
                    : content[..40].TrimEnd() + "...";
            }

            session.Summary = content;
            session.MarkActivity(DateTimeOffset.Now);
            RaiseAgentChatStateChanged();
        }

        private void RaiseAgentChatStateChanged()
        {
            this.RaisePropertyChanged(nameof(HasAgentChatMessages));
            this.RaisePropertyChanged(nameof(AgentChatSessionCountText));
        }

        public async Task SubmitCommandInputAsync()
        {
            if (string.IsNullOrWhiteSpace(CommandInputText))
                return;

            var input = CommandInputText.Trim();
            AddLog($"> {input}");

            // A message sent while a turn runs waits for it instead of being dropped.
            if (TryQueueAgentInput(input))
            {
                return;
            }

            AddAgentChatMessage(AgentChatMessageViewModel.UserRole, input);

            if (TryExecuteLocalSlashCommand(input))
            {
                AddAgentChatMessage(AgentChatMessageViewModel.AgentRole, Loc.Get("Workbench.Chat.HandledLocally"));
                CommandInputText = string.Empty;
                SlashCommandSuggestions.Clear();
                IsSlashCommandPaletteVisible = false;
                return;
            }

            // /compact belongs to the agent runtime, which summarizes the selected thread.
            if (_agentRuntime is not null
                && SelectedAgentChatSession is not null
                && input.Equals("/compact", StringComparison.OrdinalIgnoreCase))
            {
                CommandInputText = string.Empty;
                SlashCommandSuggestions.Clear();
                IsSlashCommandPaletteVisible = false;
                await RunAgentTurnAsync(SelectedAgentChatSession, input);
                return;
            }

            // Markdown commands from the user, the workspace and plugins run as agent turns.
            if (_agentRuntime is not null
                && SelectedAgentChatSession is not null
                && _agentCommands is not null
                && !IsBuiltInSlashCommand(input)
                && _agentCommands.TryExpand(input, out var commandPrompt))
            {
                CommandInputText = string.Empty;
                SlashCommandSuggestions.Clear();
                IsSlashCommandPaletteVisible = false;
                await RunAgentTurnAsync(SelectedAgentChatSession, commandPrompt);
                return;
            }

            if (_slashCommandService?.IsSlashCommand(input) == true)
            {
                var slashResult = await _slashCommandService.ExecuteAsync(input);
                AddLog(slashResult.Success
                    ? $"✅ {slashResult.Message}"
                    : $"❌ Error: {slashResult.Message}");
                AddAgentChatMessage(AgentChatMessageViewModel.AgentRole, slashResult.Message);
                CommandInputText = string.Empty;
                SlashCommandSuggestions.Clear();
                IsSlashCommandPaletteVisible = false;
                return;
            }

            if (_agentRuntime is not null && SelectedAgentChatSession is not null)
            {
                var message = BuildCommandSubmission(input, ComposerAttachments);
                CommandInputText = string.Empty;
                ClearComposerAttachments();
                await RunAgentTurnAsync(SelectedAgentChatSession, message);
                return;
            }

            if (_commandInput is null)
            {
                AddLog("COMMAND_INPUT_UNAVAILABLE");
                AddAgentChatMessage(AgentChatMessageViewModel.AgentRole, Loc.Get("Workbench.Chat.CommandInputUnavailable"));
                return;
            }

            _commandInput.SubmitCommand(BuildCommandSubmission(input, ComposerAttachments));
            CommandInputText = string.Empty;
            ClearComposerAttachments();
        }

        public void AddComposerAttachmentPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var attachment = ComposerAttachmentViewModel.Create(path);
            if (ComposerAttachments.Any(existing =>
                existing.FullPath.Equals(attachment.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            ComposerAttachments.Add(attachment);
            this.RaisePropertyChanged(nameof(HasComposerAttachments));
        }

        public void AddComposerAttachmentPaths(IEnumerable<string?> paths)
        {
            foreach (var path in paths)
            {
                AddComposerAttachmentPath(path);
            }
        }

        private void RemoveComposerAttachment(ComposerAttachmentViewModel? attachment)
        {
            if (attachment is null)
            {
                return;
            }

            ComposerAttachments.Remove(attachment);
            this.RaisePropertyChanged(nameof(HasComposerAttachments));
        }

        private void ClearComposerAttachments()
        {
            if (ComposerAttachments.Count == 0)
            {
                return;
            }

            ComposerAttachments.Clear();
            this.RaisePropertyChanged(nameof(HasComposerAttachments));
        }

        private static string BuildCommandSubmission(
            string input,
            IReadOnlyCollection<ComposerAttachmentViewModel> attachments)
        {
            if (attachments.Count == 0)
            {
                return input;
            }

            var lines = new List<string>
            {
                input,
                string.Empty,
                "Context files:"
            };
            lines.AddRange(attachments.Select(attachment => $"- {attachment.FullPath}"));
            return string.Join(Environment.NewLine, lines);
        }

        private bool IsBuiltInSlashCommand(string input)
        {
            var token = input.TrimStart().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return _slashCommandService?.GetCommands().Any(command =>
                command.Name.Equals(token, StringComparison.OrdinalIgnoreCase)
                || command.Aliases.Any(alias => alias.Equals(token, StringComparison.OrdinalIgnoreCase))) == true;
        }

        private void RefreshSlashCommandSuggestions()
        {
            SlashCommandSuggestions.Clear();

            if (_slashCommandService is null
                || !_slashCommandService.IsSlashCommand(CommandInputText))
            {
                IsSlashCommandPaletteVisible = false;
                SelectedSlashCommandIndex = -1;
                return;
            }

            foreach (var command in _slashCommandService.GetSuggestions(CommandInputText).Take(8))
            {
                SlashCommandSuggestions.Add(new SlashCommandSuggestionViewModel(command));
            }

            IsSlashCommandPaletteVisible = SlashCommandSuggestions.Count > 0;
            SelectedSlashCommandIndex = IsSlashCommandPaletteVisible ? 0 : -1;
        }

        private void SelectSlashCommand(SlashCommandSuggestionViewModel? suggestion)
        {
            if (suggestion is null)
            {
                return;
            }

            CommandInputText = suggestion.Name + " ";
            IsSlashCommandPaletteVisible = false;
            SelectedSlashCommandIndex = -1;
        }

        public bool AcceptFirstSlashCommandSuggestion()
        {
            return AcceptSelectedSlashCommandSuggestion();
        }

        public bool AcceptSelectedSlashCommandSuggestion()
        {
            var suggestion = SlashCommandSuggestions.FirstOrDefault();
            if (SelectedSlashCommandIndex >= 0 && SelectedSlashCommandIndex < SlashCommandSuggestions.Count)
            {
                suggestion = SlashCommandSuggestions[SelectedSlashCommandIndex];
            }

            if (suggestion is null)
            {
                return false;
            }

            SelectSlashCommand(suggestion);
            return true;
        }

        public bool MoveSlashCommandSelection(int delta)
        {
            if (!IsSlashCommandPaletteVisible || SlashCommandSuggestions.Count == 0)
            {
                return false;
            }

            var nextIndex = SelectedSlashCommandIndex < 0
                ? 0
                : SelectedSlashCommandIndex + delta;

            if (nextIndex < 0)
            {
                nextIndex = SlashCommandSuggestions.Count - 1;
            }
            else if (nextIndex >= SlashCommandSuggestions.Count)
            {
                nextIndex = 0;
            }

            SelectedSlashCommandIndex = nextIndex;
            return true;
        }

        public void HideSlashCommandSuggestions()
        {
            IsSlashCommandPaletteVisible = false;
            SelectedSlashCommandIndex = -1;
        }

        private void UpdateSlashCommandSelection()
        {
            for (var index = 0; index < SlashCommandSuggestions.Count; index++)
            {
                SlashCommandSuggestions[index].IsSelected = index == SelectedSlashCommandIndex;
            }
        }

        private bool TryExecuteLocalSlashCommand(string input)
        {
            var commandName = input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.ToLowerInvariant();

            switch (commandName)
            {
                case "/clear":
                    AddLog("INPUT_CLEARED");
                    return true;
                case "/settings":
                    NavigateTo(NavView.Settings);
                    return true;
                case "/integrations":
                    NavigateTo(NavView.Integrations);
                    return true;
                case "/extensions":
                    NavigateTo(NavView.Extensions);
                    return true;
                case "/diagnostics":
                    NavigateTo(NavView.Diagnostics);
                    return true;
                case "/coordinator":
                case "/home":
                    NavigateTo(NavView.Coordinator);
                    return true;
                case "/theme":
                    ToggleTheme();
                    AddLog($"THEME_SET: {(IsDarkMode ? "DARK" : "LIGHT")}");
                    return true;
                case "/voice":
                    ToggleWakeWordCommand.Execute(null);
                    return true;
                default:
                    return false;
            }
        }

        private void OnCommandResult(object? sender, CommandResultEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (e.Success)
                {
                    AddLog($"✅ {e.Result}");
                    AddAgentChatMessage(AgentChatMessageViewModel.AgentRole, e.Result);
                    ReadCommandLoopReplyAloud(e.Result);
                }
                else
                {
                    AddLog($"❌ Error: {e.Result}");
                    var error = Loc.Format("Workbench.Chat.Error", e.Result);
                    AddAgentChatMessage(AgentChatMessageViewModel.AgentRole, error);
                    ReadCommandLoopReplyAloud(error);
                }
            });
        }

        private void OnPendingSkillConfirmationsChanged(object? sender, EventArgs e)
        {
            RefreshPendingSkillConfirmations();
        }

        private void RefreshPendingSkillConfirmations()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshPendingSkillConfirmations);
                return;
            }

            PendingSkillConfirmations.Clear();

            if (_skillConfirmationService is not null)
            {
                foreach (var request in _skillConfirmationService.GetPending())
                {
                    PendingSkillConfirmations.Add(new PendingSkillConfirmationViewModel(
                        request,
                        ApproveSkillConfirmationAsync,
                        RejectSkillConfirmation));
                }
            }

            HasPendingSkillConfirmations = PendingSkillConfirmations.Count > 0;
        }

        private void CopySkillExecutionText(string label, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _ = CopySkillExecutionTextAsync(label, text);
        }

        private async Task CopySkillExecutionTextAsync(string label, string text)
        {
            try
            {
                var clipboard = (global::Avalonia.Application.Current?.ApplicationLifetime
                    as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard;
                if (clipboard is null)
                {
                    AddLog("COPY_FAILED: clipboard unavailable");
                    return;
                }

                await clipboard.SetTextAsync(text);
                AddLog($"COPIED_{label.ToUpperInvariant()}");
            }
            catch (Exception ex)
            {
                AddLog($"COPY_FAILED: {ex.Message}");
            }
        }

        private void CopyRuntimeDiagnosticsText(string label, string text)
        {
            CopySkillExecutionText(label, text);
        }

        private async Task ApproveSkillConfirmationAsync(PendingSkillConfirmationViewModel item)
        {
            if (_skillConfirmationService is null)
                return;

            AddLog($"CONFIRMING_SKILL: {item.SkillId}");

            var result = await _skillConfirmationService.ApproveAsync(item.Id);
            if (result.Success)
            {
                AddLog($"✅ {result.Message}");
            }
            else
            {
                var error = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? result.Message
                    : result.ErrorMessage;
                AddLog($"❌ Confirmation failed: {error}");
            }
        }

        private void RejectSkillConfirmation(PendingSkillConfirmationViewModel item)
        {
            if (_skillConfirmationService?.Reject(item.Id) == true)
            {
                AddLog($"CONFIRMATION_REJECTED: {item.SkillId}");
            }
        }

        /* ========================= */
        /* LOGGING */
        /* ========================= */

        public void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            var activityEntry = ActivityLogEntryViewModel.Create(timestamp, message);
            var logEntry = string.IsNullOrWhiteSpace(activityEntry.SourceText)
                ? $"[{timestamp}] {activityEntry.MessageText}"
                : $"[{timestamp}] [{activityEntry.SourceText}] {activityEntry.MessageText}";

            // Always use dispatcher to ensure UI updates properly
            Dispatcher.UIThread.Post(() =>
            {
                // Add to end (chat style - new messages at bottom)
                LogEntries.Add(logEntry);

                // Keep only last 100 messages
                if (LogEntries.Count > 100)
                {
                    LogEntries.RemoveAt(0);
                }

                _trayIconService?.UpdateToolTip($"Kam - {activityEntry.MessageText}");
            });
        }

        /// <summary>
        /// Event raised when status properties change and UI needs immediate refresh
        /// </summary>
        public event EventHandler? StatusChanged;

        /* ========================= */
        /* LANGUAGE */
        /* ========================= */

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(RefreshLocalizedText);
                return;
            }

            RefreshLocalizedText();
        }

        /// <summary>
        /// Rebuilds the text this view model makes in code after the interface language changes:
        /// the title bar, counters, thread rows, approval cards and tool steps, the model and approval
        /// pickers, slash command suggestions and the context meter. Rows and cards do not listen for the
        /// change themselves; this refreshes them. Messages and log lines already written stay as they are.
        /// </summary>
        public void RefreshLocalizedText()
        {
            UpdateStatusProperties();
            RaiseVoiceStateChanged();
            this.RaisePropertyChanged(nameof(QueuedAgentMessagesText));
            RaiseAgentChatStateChanged();
            RaiseSelectedContext();

            var now = DateTimeOffset.Now;
            foreach (var session in AgentChatSessions)
            {
                session.RefreshLocalizedText(now);
            }

            foreach (var option in ApprovalModes)
            {
                option.RefreshLocalizedText();
            }

            RefreshAgentChatModelOptions();

            foreach (var suggestion in SlashCommandSuggestions)
            {
                suggestion.RefreshLocalizedText();
            }

            var coordinators = _viewModelCache.Values.OfType<CoordinatorViewModel>().ToList();
            if (CurrentViewModel is CoordinatorViewModel current && !coordinators.Contains(current))
            {
                coordinators.Add(current);
            }

            foreach (var coordinator in coordinators)
            {
                coordinator.RefreshLocalizedText();
            }
        }

        private void ShowMainWindow()
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                if (desktop.MainWindow != null)
                {
                    desktop.MainWindow.Show();
                    desktop.MainWindow.WindowState = WindowState.Normal;
                    desktop.MainWindow.Activate();
                }
            }
        }

        /* ========================= */
        /* SIMULATION - DISABLED */
        /* ========================= */

        /* ========================= */
        /* CLEANUP */
        /* ========================= */

        public void Cleanup()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _resultListenerCts?.Cancel();
            _resultListenerCts?.Dispose();
            
            if (_commandInput != null)
            {
                _commandInput.OnResult -= OnCommandResult;
            }

            if (_skillConfirmationService is not null)
            {
                _skillConfirmationService.PendingChanged -= OnPendingSkillConfirmationsChanged;
            }

            CleanupVoice();
            foreach (var viewModel in _viewModelCache.Values.OfType<IDisposable>().Distinct())
            {
                viewModel.Dispose();
            }

            _viewModelCache.Clear();

            if (_pageSettingsService is IDisposable disposableSettingsService)
            {
                disposableSettingsService.Dispose();
            }

            if (_modelConnectionTestService is IDisposable disposableModelConnectionTestService)
            {
                disposableModelConnectionTestService.Dispose();
            }
        }
    }

    public sealed class PendingSkillConfirmationViewModel
    {
        public PendingSkillConfirmationViewModel(
            SkillConfirmationRequest request,
            Func<PendingSkillConfirmationViewModel, Task> approve,
            Action<PendingSkillConfirmationViewModel> reject)
        {
            Id = request.Id;
            SkillId = request.SkillId;
            UserCommand = request.UserCommand;
            Reason = request.Reason;
            Preview = request.Preview;
            HasPreview = !string.IsNullOrWhiteSpace(request.Preview);
            CreatedAtText = request.CreatedAt.ToLocalTime().ToString("HH:mm:ss", LocalizationService.Instance.Culture);
            ApproveCommand = ReactiveCommand.CreateFromTask(() => approve(this));
            RejectCommand = ReactiveCommand.Create(() => reject(this));
        }

        public Guid Id { get; }

        public string SkillId { get; }

        public string UserCommand { get; }

        public string Reason { get; }

        public string Preview { get; }

        public bool HasPreview { get; }

        public string CreatedAtText { get; }

        public ICommand ApproveCommand { get; }

        public ICommand RejectCommand { get; }
    }

    /// <summary>
    /// A slash command offered in the composer palette. Built-in commands show their description and
    /// category in the interface language; commands from plugins and Markdown files show theirs as written.
    /// </summary>
    public sealed class SlashCommandSuggestionViewModel
        : ReactiveObject
    {
        private const string SummaryKeyPrefix = "Workbench.Slash.";
        private const string CategoryKeyPrefix = "Workbench.SlashCategory.";

        private readonly SlashCommandDefinition _definition;

        public SlashCommandSuggestionViewModel(SlashCommandDefinition definition)
        {
            _definition = definition;
            Name = definition.Name;
            Usage = definition.Usage;
        }

        public string Name { get; }

        /// <summary>
        /// Gets what the command does, in the interface language for built-in commands.
        /// </summary>
        public string Summary => Translate(GetSummaryKey(_definition.Name), _definition.Summary);

        public string Usage { get; }

        /// <summary>
        /// Gets the command's group, such as "Runtime", in the interface language.
        /// </summary>
        public string Category => Translate(CategoryKeyPrefix + _definition.Category, _definition.Category);

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        /// <summary>
        /// Returns the text key of a built-in command's description: "/github app run" is
        /// <c>Workbench.Slash.Github.App.Run</c> and "/github-app" is <c>Workbench.Slash.GithubApp</c>.
        /// </summary>
        /// <param name="commandName">The command name with its slash.</param>
        public static string GetSummaryKey(string commandName)
        {
            var segments = (commandName ?? string.Empty)
                .TrimStart('/')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.Concat(segment
                    .Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => char.ToUpperInvariant(part[0]) + part[1..])));

            return SummaryKeyPrefix + string.Join('.', segments);
        }

        /// <summary>
        /// Re-reads the description and category after the interface language changes.
        /// </summary>
        public void RefreshLocalizedText()
        {
            this.RaisePropertyChanged(nameof(Summary));
            this.RaisePropertyChanged(nameof(Category));
        }

        /// <summary>
        /// Translates <paramref name="english"/> only when it is the built-in English text of
        /// <paramref name="key"/>, so a plugin command that reuses a built-in name keeps its own text.
        /// </summary>
        private static string Translate(string key, string english)
        {
            return string.Equals(EnglishText.Service.Get(key), english, StringComparison.Ordinal)
                ? LocalizationService.Instance.Get(key)
                : english;
        }
    }

    /// <summary>
    /// The English interface text, for values that are compared in English whatever the current language.
    /// </summary>
    internal static class EnglishText
    {
        /// <summary>
        /// Gets a localization service that always reads English.
        /// </summary>
        public static LocalizationService Service { get; } = new();
    }

    public sealed class ComposerAttachmentViewModel
    {
        private ComposerAttachmentViewModel(
            string fileName,
            string fullPath,
            string displayPath)
        {
            FileName = fileName;
            FullPath = fullPath;
            DisplayPath = displayPath;
        }

        public string FileName { get; }

        public string FullPath { get; }

        public string DisplayPath { get; }

        public static ComposerAttachmentViewModel Create(string path)
        {
            var fullPath = path.Trim();
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = fullPath;
            }

            var directoryName = Path.GetFileName(Path.GetDirectoryName(fullPath));
            var displayPath = string.IsNullOrWhiteSpace(directoryName)
                ? fileName
                : $"...{Path.DirectorySeparatorChar}{directoryName}{Path.DirectorySeparatorChar}{fileName}";

            return new ComposerAttachmentViewModel(fileName, fullPath, displayPath);
        }
    }

    public sealed class ActivityLogEntryViewModel
    {
        private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
        private static readonly IBrush SystemBrush = new SolidColorBrush(Color.Parse("#A1A1AA"));
        private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#10B981"));
        private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
        private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#EF4444"));
        private static readonly IBrush CommandBrush = new SolidColorBrush(Color.Parse("#22D3EE"));

        private ActivityLogEntryViewModel(
            string timeText,
            string categoryText,
            string sourceText,
            string messageText,
            IBrush accentBrush)
        {
            TimeText = timeText;
            CategoryText = categoryText;
            SourceText = sourceText;
            MessageText = messageText;
            AccentBrush = accentBrush;
        }

        public string TimeText { get; }

        public string CategoryText { get; }

        public string SourceText { get; }

        public bool HasSourceText => !string.IsNullOrWhiteSpace(SourceText);

        public string MessageText { get; }

        public IBrush AccentBrush { get; }

        public static ActivityLogEntryViewModel Create(string timeText, string message)
        {
            var trimmed = (message ?? string.Empty).Trim();
            var upper = trimmed.ToUpperInvariant();

            if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                return new ActivityLogEntryViewModel(
                    timeText,
                    "Command",
                    string.Empty,
                    trimmed[2..].Trim(),
                    CommandBrush);
            }

            var (sourceText, messageText) = SplitSource(CleanMessage(trimmed));
            var displaySource = string.IsNullOrWhiteSpace(sourceText)
                ? InferSource(messageText)
                : sourceText;
            var displayMessage = FormatMessage(messageText);

            if (ContainsAny(upper, "ERROR", "FAILED", "FAILURE", "BLOCKED")
                || trimmed.StartsWith("X ", StringComparison.Ordinal)
                || trimmed.StartsWith("❌", StringComparison.Ordinal))
            {
                return new ActivityLogEntryViewModel(timeText, "Error", displaySource, displayMessage, ErrorBrush);
            }

            if (ContainsAny(upper, "WARN", "UNAVAILABLE", "NOT AVAILABLE", "RATE_LIMIT", "QUOTA", "BALANCE", "AI_PROVIDER_AUTH")
                || trimmed.StartsWith("⚠", StringComparison.Ordinal))
            {
                return new ActivityLogEntryViewModel(timeText, "Warning", displaySource, displayMessage, WarningBrush);
            }

            if (ContainsAny(upper, "READY", "SUCCESS", "OK", "ONLINE", "STARTED", "ENABLED", "COMPLETED")
                || trimmed.StartsWith("✅", StringComparison.Ordinal)
                || trimmed.StartsWith("🟢", StringComparison.Ordinal))
            {
                return new ActivityLogEntryViewModel(timeText, "Ready", displaySource, displayMessage, SuccessBrush);
            }

            if (ContainsAny(upper, "NAVIGATED", "THEME", "VOICE", "WORKSPACE", "AGENT_RUNTIME", "COPIED", "INPUT_CLEARED"))
            {
                return new ActivityLogEntryViewModel(timeText, "System", displaySource, displayMessage, SystemBrush);
            }

            return new ActivityLogEntryViewModel(timeText, "Event", displaySource, displayMessage, InfoBrush);
        }

        private static string CleanMessage(string value)
        {
            foreach (var prefix in new[] { "✅", "❌", "⚠️", "⚠", "🟢", "🔴", "🎤", "🛑" })
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return value[prefix.Length..].Trim();
                }
            }

            return value;
        }

        private static (string SourceText, string MessageText) SplitSource(string value)
        {
            if (!value.StartsWith("[", StringComparison.Ordinal))
            {
                return (string.Empty, value);
            }

            var closingBracket = value.IndexOf("]", StringComparison.Ordinal);
            if (closingBracket <= 1 || closingBracket >= value.Length - 1)
            {
                return (string.Empty, value);
            }

            var source = value[1..closingBracket].Trim();
            var message = value[(closingBracket + 1)..].Trim();

            return string.IsNullOrWhiteSpace(message)
                ? (string.Empty, value)
                : (FormatSource(source), FormatTechnicalMessage(message));
        }

        private static string FormatSource(string value)
        {
            var source = value.Trim();
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            if (ContainsAny(source, "Lifetime"))
            {
                return "App";
            }

            if (ContainsAny(source, "VoiceAgentHostedService"))
            {
                return "Runtime";
            }

            if (ContainsAny(source, "AgentRegistry", "AgentFactory", "AgentBuilder", "AgentOrchestrator"))
            {
                return "Agents";
            }

            if (IsRuntimeAgentSource(source))
            {
                return ToSentenceStart(FormatAgentName(source));
            }

            if (ContainsAny(source, "CommunicationAgentTools", "TaskAgentTools", "Todoist", "Mcp"))
            {
                return "Integrations";
            }

            if (ContainsAny(source, "MultiSTT", "WakeWord", "Whisper", "Speech", "Voice"))
            {
                return "Voice";
            }

            if (ContainsAny(source, "Email", "Mail", "Sms"))
            {
                return "Mail";
            }

            if (ContainsAny(source, "GitHub"))
            {
                return "GitHub";
            }

            if (ContainsAny(source, "SlashCommand", "CommandInput"))
            {
                return "Commands";
            }

            if (ContainsAny(source, "Settings", "Configuration"))
            {
                return "Settings";
            }

            return "System";
        }

        private static string InferSource(string value)
        {
            if (!ContainsAny(value, "AI_PROVIDER_", "RATE_LIMIT", "BALANCE", "QUOTA"))
            {
                return string.Empty;
            }

            if (ContainsAny(value, "claude", "anthropic"))
            {
                return "Claude";
            }

            if (ContainsAny(value, "gemini", "google"))
            {
                return "Gemini";
            }

            if (ContainsAny(value, "ollama"))
            {
                return "Ollama";
            }

            if (ContainsAny(value, "gpt", "openai", "codex"))
            {
                return "Codex";
            }

            return "AI";
        }

        private static string FormatTechnicalMessage(string value)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }

            if (ContainsAny(trimmed, "ISmsService not registered", "SMS functionality will be unavailable"))
            {
                return "SMS integration is not configured.";
            }

            if (ContainsAny(trimmed, "Todoist MCP is not configured"))
            {
                return "Todoist integration is not configured.";
            }

            if (ContainsAny(trimmed, "HuggingFace API key not configured"))
            {
                return "Speech provider is not configured.";
            }

            if (ContainsAny(trimmed, "Whisper model not found"))
            {
                return "Local speech model is not installed.";
            }

            if (ContainsAny(trimmed, "MultiSTTService initialized with 0 providers"))
            {
                return "No speech providers are available.";
            }

            if (ContainsAny(trimmed, "WakeWordDetectionService initialized", "wake word:"))
            {
                return "Wake word listener ready.";
            }

            if (ContainsAny(trimmed, "EmailService configured"))
            {
                return "Mail provider configured.";
            }

            if (ContainsAny(trimmed, "Created new template", "predefined templates"))
            {
                return "Mail templates ready.";
            }

            if (TryFormatAgentRegistration(trimmed, out var agentRegistration))
            {
                return agentRegistration;
            }

            if (TryFormatAgentPreparation(trimmed, out var agentPreparation))
            {
                return agentPreparation;
            }

            if (ContainsAny(trimmed, "legacy agents ready"))
            {
                return trimmed.Replace("legacy agents", "agents", StringComparison.OrdinalIgnoreCase);
            }

            if (trimmed.StartsWith("Application started.", StringComparison.OrdinalIgnoreCase))
            {
                return "Application started.";
            }

            if (trimmed.StartsWith("Hosting environment:", StringComparison.OrdinalIgnoreCase))
            {
                return "Runtime environment ready.";
            }

            if (trimmed.StartsWith("Content root path:", StringComparison.OrdinalIgnoreCase))
            {
                return "Workspace loaded.";
            }

            if (ContainsAny(trimmed, "Loaded 0 tools asynchronously"))
            {
                return "No tools loaded.";
            }

            if (TryFormatStatusMessage(trimmed, out var statusMessage))
            {
                return statusMessage;
            }

            return RedactTechnicalNames(trimmed);
        }

        private static bool TryFormatStatusMessage(string value, out string message)
        {
            if (value.Equals("AGENT_RUNTIME_READY", StringComparison.OrdinalIgnoreCase))
            {
                message = "Agent runtime ready.";
                return true;
            }

            if (value.Equals("WORKSPACE_READY", StringComparison.OrdinalIgnoreCase))
            {
                message = "Workspace ready.";
                return true;
            }

            if (value.StartsWith("NAVIGATED_TO:", StringComparison.OrdinalIgnoreCase))
            {
                message = $"Opened {FormatTokenValue(value["NAVIGATED_TO:".Length..])}.";
                return true;
            }

            if (value.StartsWith("THEME_CHANGED:", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("THEME_SET:", StringComparison.OrdinalIgnoreCase))
            {
                var theme = value[(value.IndexOf(':', StringComparison.Ordinal) + 1)..];
                message = $"Switched to {FormatTokenValue(theme).ToLowerInvariant()} theme.";
                return true;
            }

            if (value.Equals("INPUT_CLEARED", StringComparison.OrdinalIgnoreCase))
            {
                message = "Input cleared.";
                return true;
            }

            if (value.Equals("COMMAND_INPUT_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
            {
                message = "Command input is not connected yet.";
                return true;
            }

            if (value.Equals("COPIED_STDOUT", StringComparison.OrdinalIgnoreCase))
            {
                message = "Copied output.";
                return true;
            }

            if (value.StartsWith("COPY_FAILED:", StringComparison.OrdinalIgnoreCase))
            {
                message = "Clipboard is not available.";
                return true;
            }

            if (value.StartsWith("RERUN_SKILL:", StringComparison.OrdinalIgnoreCase))
            {
                message = $"Rerunning {FormatTokenValue(value["RERUN_SKILL:".Length..])}.";
                return true;
            }

            if (value.StartsWith("RERUN_FAILED:", StringComparison.OrdinalIgnoreCase))
            {
                message = "Could not rerun the skill.";
                return true;
            }

            if (value.StartsWith("AI_PROVIDER_RATE_LIMIT", StringComparison.OrdinalIgnoreCase))
            {
                message = $"{ProviderDisplayName(value)} is rate limited. Wait for the reset or switch models.";
                return true;
            }

            if (value.StartsWith("AI_PROVIDER_BALANCE", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("AI_PROVIDER_QUOTA", StringComparison.OrdinalIgnoreCase))
            {
                message = $"{ProviderDisplayName(value)} quota or balance needs attention.";
                return true;
            }

            if (value.StartsWith("AI_PROVIDER_AUTH", StringComparison.OrdinalIgnoreCase))
            {
                message = $"{ProviderDisplayName(value)} credentials need attention.";
                return true;
            }

            message = string.Empty;
            return false;
        }

        private static bool TryFormatAgentRegistration(string value, out string message)
        {
            const string prefix = "Agent registered:";
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                message = string.Empty;
                return false;
            }

            var agentName = value[prefix.Length..].Trim();
            message = $"{ToSentenceStart(FormatAgentName(agentName))} registered.";
            return true;
        }

        private static bool TryFormatAgentPreparation(string value, out string message)
        {
            const string creatingPrefix = "Creating ";
            if (value.StartsWith(creatingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var tail = value[creatingPrefix.Length..];
                var agentName = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                message = $"Preparing {FormatAgentName(agentName)}.";
                return true;
            }

            const string buildingPrefix = "Building agent";
            if (value.StartsWith(buildingPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var firstQuote = value.IndexOf('\'', StringComparison.Ordinal);
                var lastQuote = value.LastIndexOf('\'');
                if (firstQuote >= 0 && lastQuote > firstQuote)
                {
                    var agentName = value[(firstQuote + 1)..lastQuote];
                    message = $"Preparing {FormatAgentName(agentName)}.";
                    return true;
                }
            }

            message = string.Empty;
            return false;
        }

        private static string FormatAgentName(string value)
        {
            var cleaned = value.Trim().Trim('\'', '"');
            var sequence = string.Empty;
            var dashIndex = cleaned.LastIndexOf('-');
            if (dashIndex > 0
                && dashIndex < cleaned.Length - 1
                && cleaned[(dashIndex + 1)..].All(char.IsDigit))
            {
                sequence = cleaned[(dashIndex + 1)..].TrimStart('0');
                cleaned = cleaned[..dashIndex];
            }

            if (cleaned.EndsWith("Agent", StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^"Agent".Length];
            }

            var words = SplitCodeName(cleaned);
            var displayName = string.IsNullOrWhiteSpace(words)
                ? "agent"
                : $"{words.ToLowerInvariant()} agent";

            return string.IsNullOrWhiteSpace(sequence)
                ? displayName
                : $"{displayName} {sequence}";
        }

        private static bool IsRuntimeAgentSource(string source)
        {
            if (ContainsAny(source, "AgentRegistry", "AgentFactory", "AgentBuilder", "AgentOrchestrator"))
            {
                return false;
            }

            if (source.EndsWith("Agent", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var dashIndex = source.LastIndexOf('-');
            return dashIndex > 0
                && source[..dashIndex].EndsWith("Agent", StringComparison.OrdinalIgnoreCase)
                && source[(dashIndex + 1)..].All(char.IsDigit);
        }

        private static string RedactTechnicalNames(string value)
        {
            return value
                .Replace("VoiceAgentHostedService", "runtime", StringComparison.Ordinal)
                .Replace("CommunicationAgentTools", "integrations", StringComparison.Ordinal)
                .Replace("TaskAgentTools", "integrations", StringComparison.Ordinal)
                .Replace("AgentRegistry", "agent manager", StringComparison.Ordinal)
                .Replace("AgentFactory", "agent manager", StringComparison.Ordinal)
                .Replace("AgentBuilder", "agent builder", StringComparison.Ordinal)
                .Replace("MultiSTTService", "speech provider", StringComparison.Ordinal)
                .Replace("WakeWordDetectionService", "wake word listener", StringComparison.Ordinal)
                .Replace("EmailService", "mail provider", StringComparison.Ordinal)
                .Replace("ISmsService", "SMS integration", StringComparison.Ordinal);
        }

        private static string ProviderDisplayName(string value)
        {
            var inferred = InferSource(value);
            return string.IsNullOrWhiteSpace(inferred) ? "AI provider" : inferred;
        }

        private static string FormatTokenValue(string value)
        {
            var token = value.Trim().Trim('\'', '"');
            if (string.IsNullOrWhiteSpace(token))
            {
                return "item";
            }

            token = token.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');
            var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant());
            return title
                .Replace("Ai", "AI", StringComparison.Ordinal)
                .Replace("Api", "API", StringComparison.Ordinal)
                .Replace("Sms", "SMS", StringComparison.Ordinal);
        }

        private static string SplitCodeName(string value)
        {
            var characters = new List<char>(value.Length + 8);
            for (var index = 0; index < value.Length; index++)
            {
                var current = value[index];
                if (index > 0
                    && char.IsUpper(current)
                    && !char.IsWhiteSpace(value[index - 1])
                    && !char.IsUpper(value[index - 1]))
                {
                    characters.Add(' ');
                }

                characters.Add(current);
            }

            return new string(characters.ToArray()).Trim();
        }

        private static string ToSentenceStart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? value
                : char.ToUpperInvariant(value[0]) + value[1..];
        }

        private static string FormatMessage(string value)
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return trimmed;
            }

            trimmed = FormatTechnicalMessage(trimmed);

            if (!trimmed.Contains("_", StringComparison.Ordinal)
                || trimmed.Any(char.IsLower))
            {
                return trimmed;
            }

            var spaced = trimmed.Replace('_', ' ');
            var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(spaced.ToLowerInvariant());

            return title
                .Replace("Ai", "AI", StringComparison.Ordinal)
                .Replace("Sms", "SMS", StringComparison.Ordinal)
                .Replace("Api", "API", StringComparison.Ordinal);
        }

        private static bool ContainsAny(string value, params string[] needles)
        {
            return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
    }

}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
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
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private TrayIconService? _trayIconService;
        private DispatcherTimer? _simulationTimer;
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
        private ISkillExecutionPipeline? _skillExecutionPipeline;
        private ISkillPlannerTraceStore? _skillPlannerTraceStore;
        private ISlashCommandService? _slashCommandService;
        private IRuntimeAgentRunStore? _runtimeAgentRunStore;
        private IApplicationUpdateService? _applicationUpdateService;
        private IApplicationRestartPlanner? _applicationRestartPlanner;
        private IApplicationVersionProvider? _applicationVersionProvider;
        private IApplicationUpdateSession? _applicationUpdateSession;
        private IGitHubAppClient? _githubAppClient;
        private IGitHubAppClientFactory? _githubAppClientFactory;
        private readonly IModelConnectionTestService _modelConnectionTestService = new ModelConnectionTestService();
        private readonly ISettingsService _pageSettingsService = new JsonSettingsService();
        private readonly Dictionary<NavView, ViewModelBase> _viewModelCache = [];

        private const int MaxSkillExecutionHistoryScanCount = 50;
        private const int MaxSkillExecutionHistoryDisplayCount = 8;
        private const int MaxSkillPlannerTraceDisplayCount = 5;
        private const int MaxRuntimeAgentActivityDisplayCount = 6;
        private const string SkillExecutionHistoryAllStatusFilter = "All";

        private static readonly JsonSerializerOptions SkillPlanJsonOptions = new(JsonSerializerDefaults.Web);

        /* ========================= */
        /* CACHED BRUSHES */
        /* ========================= */
        // Static brushes to avoid repeated allocations
        private static readonly IBrush OnlineStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));
        private static readonly IBrush OfflineStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444"));

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
            private set => this.RaiseAndSetIfChanged(ref _activeView, value);
        }

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
        /// Status text for header display - reflects VoiceAgent Host state
        /// </summary>
        public override string StatusText
        {
            get => base.StatusText;
            protected set => base.StatusText = value;
        }

        /// <summary>
        /// Status color for header display indicator
        /// </summary>
        public override IBrush StatusColor
        {
            get => base.StatusColor;
            protected set => base.StatusColor = value;
        }

        private void UpdateStatusProperties()
        {
            Console.WriteLine($"[UpdateStatusProperties] IsHostRunning={IsHostRunning}");
            
            // Use cached brushes to avoid repeated allocations
            var newText = IsHostRunning ? "SYSTEM ONLINE" : "SYSTEM OFFLINE";
            var newColor = IsHostRunning ? OnlineStatusColor : OfflineStatusColor;
            
            Console.WriteLine($"[UpdateStatusProperties] Setting StatusText to: {newText}");
            Console.WriteLine($"[UpdateStatusProperties] Current StatusText before set: {StatusText}");
            
            // Use base class property setters
            base.StatusText = newText;
            base.StatusColor = newColor;
            
            Console.WriteLine($"[UpdateStatusProperties] StatusText after set: {StatusText}");
            
            // Also explicitly raise property changed for this class
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(StatusColor));
            
            // Notify view that status changed for immediate visual refresh
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        /* ========================= */
        /* COMMANDS */
        /* ========================= */

        public ICommand NavigateToCoordinatorCommand { get; }
        public ICommand NavigateToDiagnosticsCommand { get; }
        public ICommand NavigateToPluginsCommand { get; }
        public ICommand NavigateToIntegrationsCommand { get; }
        public ICommand NavigateToSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }
        public ICommand ClearSkillExecutionHistoryCommand { get; }
        public ICommand ClearSkillExecutionHistoryFiltersCommand { get; }
        public ICommand ClearSkillPlannerTraceCommand { get; }
        public ICommand ShowRuntimeAgentRunDetailCommand { get; }

        /* ========================= */
        /* LOGGING */
        /* ========================= */

        private ObservableCollection<string> _logEntries = new();
        public ObservableCollection<string> LogEntries
        {
            get => _logEntries;
            set => this.RaiseAndSetIfChanged(ref _logEntries, value);
        }

        private ObservableCollection<ActivityLogEntryViewModel> _activityLogEntries = new();
        public ObservableCollection<ActivityLogEntryViewModel> ActivityLogEntries
        {
            get => _activityLogEntries;
            set => this.RaiseAndSetIfChanged(ref _activityLogEntries, value);
        }

        private ObservableCollection<RuntimeAgentActivityViewModel> _runtimeAgentActivities = new();
        public ObservableCollection<RuntimeAgentActivityViewModel> RuntimeAgentActivities
        {
            get => _runtimeAgentActivities;
            set => this.RaiseAndSetIfChanged(ref _runtimeAgentActivities, value);
        }

        public bool HasRuntimeAgentActivities => RuntimeAgentActivities.Count > 0;

        private RuntimeAgentActivityViewModel? _selectedRuntimeAgentActivity;
        public RuntimeAgentActivityViewModel? SelectedRuntimeAgentActivity
        {
            get => _selectedRuntimeAgentActivity;
            private set => this.RaiseAndSetIfChanged(ref _selectedRuntimeAgentActivity, value);
        }

        private RuntimeAgentRunDetailViewModel? _selectedRuntimeAgentRun;
        public RuntimeAgentRunDetailViewModel? SelectedRuntimeAgentRun
        {
            get => _selectedRuntimeAgentRun;
            private set
            {
                this.RaiseAndSetIfChanged(ref _selectedRuntimeAgentRun, value);
                this.RaisePropertyChanged(nameof(HasSelectedRuntimeAgentRun));
            }
        }

        public bool HasSelectedRuntimeAgentRun => SelectedRuntimeAgentRun is not null;

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
        /* LANGUAGE */
        /* ========================= */

        private int _selectedLanguageIndex;
        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedLanguageIndex, value);
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
                RefreshSlashCommandSuggestions();
            }
        }

        public ICommand SubmitCommand { get; }

        public ICommand SelectSlashCommandCommand { get; }

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

        private ObservableCollection<SkillExecutionHistoryItemViewModel> _skillExecutionHistory = new();
        public ObservableCollection<SkillExecutionHistoryItemViewModel> SkillExecutionHistory
        {
            get => _skillExecutionHistory;
            set => this.RaiseAndSetIfChanged(ref _skillExecutionHistory, value);
        }

        private ObservableCollection<SkillPlannerTraceItemViewModel> _skillPlannerTraces = new();
        public ObservableCollection<SkillPlannerTraceItemViewModel> SkillPlannerTraces
        {
            get => _skillPlannerTraces;
            set => this.RaiseAndSetIfChanged(ref _skillPlannerTraces, value);
        }

        public IReadOnlyList<string> SkillExecutionHistoryStatusFilters { get; } =
        [
            SkillExecutionHistoryAllStatusFilter,
            "Succeeded",
            "Failed",
            "Timed Out",
            "Permission Denied",
            "Validation Failed",
            "Review Required"
        ];

        private string _skillExecutionHistoryFilterText = string.Empty;
        public string SkillExecutionHistoryFilterText
        {
            get => _skillExecutionHistoryFilterText;
            set
            {
                var normalizedValue = value ?? string.Empty;
                if (_skillExecutionHistoryFilterText == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _skillExecutionHistoryFilterText, normalizedValue);
                RaiseSkillExecutionHistoryFilterStateChanged();
                RefreshSkillExecutionHistory();
            }
        }

        private string _skillExecutionHistoryStatusFilter = SkillExecutionHistoryAllStatusFilter;
        public string SkillExecutionHistoryStatusFilter
        {
            get => _skillExecutionHistoryStatusFilter;
            set
            {
                var normalizedValue = string.IsNullOrWhiteSpace(value)
                    ? SkillExecutionHistoryAllStatusFilter
                    : value;
                if (_skillExecutionHistoryStatusFilter == normalizedValue)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _skillExecutionHistoryStatusFilter, normalizedValue);
                RaiseSkillExecutionHistoryFilterStateChanged();
                RefreshSkillExecutionHistory();
            }
        }

        private int _skillExecutionHistoryTotalCount;
        public int SkillExecutionHistoryTotalCount
        {
            get => _skillExecutionHistoryTotalCount;
            private set => this.RaiseAndSetIfChanged(ref _skillExecutionHistoryTotalCount, value);
        }

        private int _skillExecutionHistoryMatchCount;
        public int SkillExecutionHistoryMatchCount
        {
            get => _skillExecutionHistoryMatchCount;
            private set => this.RaiseAndSetIfChanged(ref _skillExecutionHistoryMatchCount, value);
        }

        private int _skillExecutionHistoryVisibleCount;
        public int SkillExecutionHistoryVisibleCount
        {
            get => _skillExecutionHistoryVisibleCount;
            private set => this.RaiseAndSetIfChanged(ref _skillExecutionHistoryVisibleCount, value);
        }

        public bool HasSkillExecutionHistoryFilter =>
            !string.IsNullOrWhiteSpace(SkillExecutionHistoryFilterText)
            || !SkillExecutionHistoryStatusFilter.Equals(
                SkillExecutionHistoryAllStatusFilter,
                StringComparison.OrdinalIgnoreCase);

        public bool HasSkillExecutionHistoryMatches => SkillExecutionHistoryVisibleCount > 0;

        public bool HasNoSkillExecutionHistoryMatches =>
            HasSkillExecutionHistory && SkillExecutionHistoryVisibleCount == 0;

        public string SkillExecutionHistorySummaryText
        {
            get
            {
                if (SkillExecutionHistoryTotalCount == 0)
                {
                    return "No executions";
                }

                if (HasSkillExecutionHistoryFilter)
                {
                    return $"{SkillExecutionHistoryVisibleCount}/{SkillExecutionHistoryMatchCount} matches in last {SkillExecutionHistoryTotalCount}";
                }

                return $"{SkillExecutionHistoryVisibleCount}/{SkillExecutionHistoryTotalCount} recent executions";
            }
        }

        private bool _hasSkillExecutionHistory;
        public bool HasSkillExecutionHistory
        {
            get => _hasSkillExecutionHistory;
            private set => this.RaiseAndSetIfChanged(ref _hasSkillExecutionHistory, value);
        }

        private bool _hasSkillPlannerTraces;
        public bool HasSkillPlannerTraces
        {
            get => _hasSkillPlannerTraces;
            private set => this.RaiseAndSetIfChanged(ref _hasSkillPlannerTraces, value);
        }

        /* ========================= */
        /* VOICE COMMAND */
        /* ========================= */

        private VoiceCommandService? _voiceCommandService;

        // Performance: Cache brushes to avoid parsing on every status change
        private static readonly IBrush s_voiceListeningColor = Brush.Parse("#10B981"); // Green
        private static readonly IBrush s_voiceWakeWordColor = Brush.Parse("#F59E0B"); // Orange
        private static readonly IBrush s_voiceRecordingColor = Brush.Parse("#EF4444"); // Red
        private static readonly IBrush s_voiceProcessingColor = Brush.Parse("#3B82F6"); // Blue
        private static readonly IBrush s_voiceIdleColor = Brush.Parse("#6B7280"); // Gray
        
        private bool _isVoiceEnabled = false;
        public bool IsVoiceEnabled
        {
            get => _isVoiceEnabled;
            private set => this.RaiseAndSetIfChanged(ref _isVoiceEnabled, value);
        }

        private bool _isListeningForWakeWord = false;
        public bool IsListeningForWakeWord
        {
            get => _isListeningForWakeWord;
            private set => this.RaiseAndSetIfChanged(ref _isListeningForWakeWord, value);
        }

        private bool _isRecordingVoice = false;
        public bool IsRecordingVoice
        {
            get => _isRecordingVoice;
            private set => this.RaiseAndSetIfChanged(ref _isRecordingVoice, value);
        }

        private string _voiceStatusText = "Voice: Off";
        public string VoiceStatusText
        {
            get => _voiceStatusText;
            private set => this.RaiseAndSetIfChanged(ref _voiceStatusText, value);
        }

        private IBrush _voiceStatusColor = Brush.Parse("#6B7280"); // Gray
        public IBrush VoiceStatusColor
        {
            get => _voiceStatusColor;
            private set => this.RaiseAndSetIfChanged(ref _voiceStatusColor, value);
        }

        public ICommand ToggleVoiceCommand { get; }
        public ICommand StartVoiceRecordingCommand { get; }

        /* ========================= */
        /* CONSTRUCTOR */
        /* ========================= */

        public MainWindowViewModel()
        {
            // Commands
            NavigateToCoordinatorCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Coordinator));
            NavigateToDiagnosticsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Diagnostics));
            NavigateToPluginsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Plugins));
            NavigateToIntegrationsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Integrations));
            NavigateToSettingsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Settings));
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            ClearSkillExecutionHistoryCommand = ReactiveCommand.Create(ClearSkillExecutionHistory);
            ClearSkillExecutionHistoryFiltersCommand = ReactiveCommand.Create(ClearSkillExecutionHistoryFilters);
            ClearSkillPlannerTraceCommand = ReactiveCommand.Create(ClearSkillPlannerTrace);
            ShowRuntimeAgentRunDetailCommand = ReactiveCommand.Create<RuntimeAgentActivityViewModel?>(ShowRuntimeAgentRunDetail);
            SubmitCommand = ReactiveCommand.CreateFromTask(SubmitCommandInputAsync);
            SelectSlashCommandCommand = ReactiveCommand.Create<SlashCommandSuggestionViewModel>(SelectSlashCommand);
            ToggleVoiceCommand = ReactiveCommand.Create(ToggleVoiceEnabled);
            StartVoiceRecordingCommand = ReactiveCommand.CreateFromTask(StartVoiceRecordingAsync);

            Dispatcher.UIThread.Post(() =>
            {
                // Initialize theme
                if (global::Avalonia.Application.Current != null)
                {
                    IsDarkMode = global::Avalonia.Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                }

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
                Console.WriteLine("[SetVoiceAgentHostControl] Recreating CoordinatorViewModel with host control");
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
            if (_skillExecutionHistoryService is not null)
            {
                _skillExecutionHistoryService.Changed -= OnSkillExecutionHistoryChanged;
            }

            _skillExecutionHistoryService = skillExecutionHistoryService;
            _skillExecutionHistoryService.Changed += OnSkillExecutionHistoryChanged;
            RefreshSkillExecutionHistory();
        }

        public void SetSkillPlannerTraceStore(ISkillPlannerTraceStore skillPlannerTraceStore)
        {
            if (_skillPlannerTraceStore is not null)
            {
                _skillPlannerTraceStore.Changed -= OnSkillPlannerTraceChanged;
            }

            _skillPlannerTraceStore = skillPlannerTraceStore;
            _skillPlannerTraceStore.Changed += OnSkillPlannerTraceChanged;
            RefreshSkillPlannerTrace();
        }

        public void SetSkillExecutionPipeline(ISkillExecutionPipeline skillExecutionPipeline)
        {
            _skillExecutionPipeline = skillExecutionPipeline;
        }

        public void SetSlashCommandService(ISlashCommandService slashCommandService)
        {
            _slashCommandService = slashCommandService;
            RefreshSlashCommandSuggestions();
        }

        public void SetRuntimeAgentRunStore(IRuntimeAgentRunStore runtimeAgentRunStore)
        {
            _runtimeAgentRunStore = runtimeAgentRunStore;
            RefreshSelectedRuntimeAgentRun();
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
        
        private void OnHostStateChanged(object? sender, bool isRunning)
        {
            Console.WriteLine($"[MainWindowViewModel] OnHostStateChanged called: isRunning={isRunning}");
            
            // Update state immediately on UI thread with maximum priority
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[MainWindowViewModel] Updating UI on dispatcher thread: isRunning={isRunning}");
                
                // Use the property setter to ensure proper notification
                IsHostRunning = isRunning;
                
                Console.WriteLine($"[MainWindowViewModel] Adding log message");
                AddLog(isRunning ? "🟢 VoiceAgent Host started" : "🔴 VoiceAgent Host stopped");
                
                // Also update the CoordinatorViewModel if it's active
                if (CurrentViewModel is CoordinatorViewModel coordinator)
                {
                    Console.WriteLine($"[MainWindowViewModel] Syncing CoordinatorViewModel");
                    coordinator.SyncWithHostState(isRunning);
                }
            }, DispatcherPriority.Send);
        }

        /// <summary>
        /// Toggles the VoiceAgent Host on/off
        /// </summary>
        public async Task ToggleHostAsync()
        {
            Console.WriteLine($"[MainWindowViewModel] ToggleHostAsync called, _hostControl is null: {_hostControl == null}");
            
            if (_hostControl == null)
            {
                Console.WriteLine("[MainWindowViewModel] Host control is null, cannot toggle");
                return;
            }

            Console.WriteLine($"[MainWindowViewModel] Host is running: {_hostControl.IsRunning}");
            
            if (_hostControl.IsRunning)
            {
                Console.WriteLine("[MainWindowViewModel] Calling StopAsync...");
                await _hostControl.StopAsync();
                Console.WriteLine("[MainWindowViewModel] StopAsync completed");
            }
            else
            {
                Console.WriteLine("[MainWindowViewModel] Calling StartAsync...");
                await _hostControl.StartAsync();
                Console.WriteLine("[MainWindowViewModel] StartAsync completed");
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
                NavView.Integrations => new IntegrationsViewModel(
                    _pageSettingsService,
                    _githubAppClientFactory),
                NavView.Settings => new SettingsViewModel(_pageSettingsService, this),
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

        public void SetTrayIconService(TrayIconService service)
        {
            _trayIconService = service;

            // Wire up tray icon menu events
            service.ShowWindowRequested += (s, e) =>
            {
                ShowMainWindow();
            };

            service.OpenSettingsRequested += (s, e) =>
            {
                NavigateTo(NavView.Settings);
            };

            service.ToggleVoiceRequested += (s, e) =>
            {
                ToggleVoiceEnabled();
            };

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
            service.SetVoiceEnabled(IsVoiceEnabled);
        }

        public void SetCommandInputService(ICommandInputService commandInput)
        {
            _commandInput = commandInput;
            _commandInput.OnResult += OnCommandResult;
            
            // Start listening for results
            _resultListenerCts = new CancellationTokenSource();
        }

        public async Task SubmitCommandInputAsync()
        {
            if (string.IsNullOrWhiteSpace(CommandInputText))
                return;

            var input = CommandInputText.Trim();
            AddLog($"> {input}");

            if (TryExecuteLocalSlashCommand(input))
            {
                CommandInputText = string.Empty;
                SlashCommandSuggestions.Clear();
                IsSlashCommandPaletteVisible = false;
                return;
            }

            if (_slashCommandService?.IsSlashCommand(input) == true)
            {
                var slashResult = await _slashCommandService.ExecuteAsync(input);
                AddLog(slashResult.Success
                    ? $"✅ {slashResult.Message}"
                    : $"❌ Error: {slashResult.Message}");
                CommandInputText = string.Empty;
                SlashCommandSuggestions.Clear();
                IsSlashCommandPaletteVisible = false;
                return;
            }

            if (_commandInput is null)
            {
                AddLog("COMMAND_INPUT_UNAVAILABLE");
                return;
            }

            _commandInput.SubmitCommand(input);
            CommandInputText = string.Empty;
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
                    ToggleVoiceEnabled();
                    AddLog($"VOICE_SET: {(IsVoiceEnabled ? "ON" : "OFF")}");
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
                }
                else
                {
                    AddLog($"❌ Error: {e.Result}");
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

        private void OnSkillExecutionHistoryChanged(object? sender, EventArgs e)
        {
            RefreshSkillExecutionHistory();
        }

        private void RefreshSkillExecutionHistory()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                if (global::Avalonia.Application.Current is not null)
                {
                    Dispatcher.UIThread.Post(RefreshSkillExecutionHistory);
                    return;
                }
            }

            SkillExecutionHistory.Clear();

            if (_skillExecutionHistoryService is not null)
            {
                var entries = _skillExecutionHistoryService
                    .GetRecent(MaxSkillExecutionHistoryScanCount)
                    .ToList();
                var matches = entries
                    .Where(MatchesSkillExecutionHistoryFilters)
                    .ToList();

                SkillExecutionHistoryTotalCount = entries.Count;
                SkillExecutionHistoryMatchCount = matches.Count;

                foreach (var entry in matches.Take(MaxSkillExecutionHistoryDisplayCount))
                {
                    SkillExecutionHistory.Add(new SkillExecutionHistoryItemViewModel(
                        entry,
                        CopySkillExecutionText,
                        RerunSkillExecution));
                }
            }
            else
            {
                SkillExecutionHistoryTotalCount = 0;
                SkillExecutionHistoryMatchCount = 0;
            }

            SkillExecutionHistoryVisibleCount = SkillExecutionHistory.Count;
            HasSkillExecutionHistory = SkillExecutionHistoryTotalCount > 0;
            RaiseSkillExecutionHistoryFilterStateChanged();
        }

        private void ClearSkillExecutionHistory()
        {
            if (_skillExecutionHistoryService is null)
            {
                return;
            }

            _skillExecutionHistoryService.Clear();
            AddLog("SKILL_HISTORY_CLEARED");
        }

        private void ClearSkillExecutionHistoryFilters()
        {
            SkillExecutionHistoryFilterText = string.Empty;
            SkillExecutionHistoryStatusFilter = SkillExecutionHistoryAllStatusFilter;
            RefreshSkillExecutionHistory();
        }

        private void OnSkillPlannerTraceChanged(object? sender, EventArgs e)
        {
            RefreshSkillPlannerTrace();
        }

        private void RefreshSkillPlannerTrace()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                if (global::Avalonia.Application.Current is not null)
                {
                    Dispatcher.UIThread.Post(RefreshSkillPlannerTrace);
                    return;
                }
            }

            SkillPlannerTraces.Clear();

            if (_skillPlannerTraceStore is not null)
            {
                foreach (var entry in _skillPlannerTraceStore.GetRecent(MaxSkillPlannerTraceDisplayCount))
                {
                    SkillPlannerTraces.Add(new SkillPlannerTraceItemViewModel(entry));
                }
            }

            HasSkillPlannerTraces = SkillPlannerTraces.Count > 0;
        }

        private void ClearSkillPlannerTrace()
        {
            if (_skillPlannerTraceStore is null)
            {
                return;
            }

            _skillPlannerTraceStore.Clear();
            AddLog("PLAN_TRACE_CLEARED");
        }

        private bool MatchesSkillExecutionHistoryFilters(SkillExecutionHistoryEntry entry)
        {
            return MatchesSkillExecutionHistoryStatusFilter(entry)
                && MatchesSkillExecutionHistoryTextFilter(entry);
        }

        private bool MatchesSkillExecutionHistoryStatusFilter(SkillExecutionHistoryEntry entry)
        {
            if (SkillExecutionHistoryStatusFilter.Equals(
                    SkillExecutionHistoryAllStatusFilter,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return SkillExecutionHistoryItemViewModel.FormatStatusText(entry.Status)
                .Equals(SkillExecutionHistoryStatusFilter, StringComparison.OrdinalIgnoreCase);
        }

        private bool MatchesSkillExecutionHistoryTextFilter(SkillExecutionHistoryEntry entry)
        {
            var query = SkillExecutionHistoryFilterText.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return ContainsIgnoreCase(entry.SkillId, query)
                || ContainsIgnoreCase(SkillExecutionHistoryItemViewModel.FormatStatusText(entry.Status), query)
                || ContainsIgnoreCase(entry.ResultSummary, query)
                || ContainsIgnoreCase(entry.ArgumentsSummary, query)
                || ContainsIgnoreCase(entry.ErrorCode, query)
                || ContainsIgnoreCase(entry.Command, query)
                || ContainsIgnoreCase(entry.WorkingDirectory, query)
                || ContainsIgnoreCase(entry.StdOut, query)
                || ContainsIgnoreCase(entry.StdErr, query);
        }

        private void RaiseSkillExecutionHistoryFilterStateChanged()
        {
            this.RaisePropertyChanged(nameof(HasSkillExecutionHistoryFilter));
            this.RaisePropertyChanged(nameof(HasSkillExecutionHistoryMatches));
            this.RaisePropertyChanged(nameof(HasNoSkillExecutionHistoryMatches));
            this.RaisePropertyChanged(nameof(SkillExecutionHistorySummaryText));
        }

        private static bool ContainsIgnoreCase(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Contains(query, StringComparison.OrdinalIgnoreCase);
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

        private void RerunSkillExecution(SkillExecutionHistoryItemViewModel item)
        {
            _ = RerunSkillExecutionAsync(item);
        }

        private async Task RerunSkillExecutionAsync(SkillExecutionHistoryItemViewModel item)
        {
            if (_skillExecutionPipeline is null)
            {
                AddLog("RERUN_FAILED: skill pipeline unavailable");
                return;
            }

            if (!item.CanRerun)
            {
                AddLog($"RERUN_BLOCKED: {item.RerunBlockedReason}");
                return;
            }

            SkillPlan? plan;
            try
            {
                plan = JsonSerializer.Deserialize<SkillPlan>(
                    item.ReplayPlanJson,
                    SkillPlanJsonOptions);
            }
            catch (JsonException ex)
            {
                AddLog($"RERUN_FAILED: invalid plan json ({ex.Message})");
                return;
            }

            if (plan is null || string.IsNullOrWhiteSpace(plan.SkillId))
            {
                AddLog("RERUN_FAILED: invalid plan");
                return;
            }

            AddLog($"RERUN_SKILL: {plan.SkillId}");
            var result = await _skillExecutionPipeline.ExecuteAsync(plan);
            if (result.Success)
            {
                AddLog($"RERUN_OK: {result.Message}");
                return;
            }

            var error = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? result.Message
                : result.ErrorMessage;
            AddLog($"RERUN_FAILED: {error}");
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
                ActivityLogEntries.Add(activityEntry);

                // Keep only last 100 messages
                if (LogEntries.Count > 100)
                {
                    LogEntries.RemoveAt(0);
                }

                if (ActivityLogEntries.Count > 100)
                {
                    ActivityLogEntries.RemoveAt(0);
                }

                _trayIconService?.UpdateToolTip($"Kam - {activityEntry.MessageText}");
                
                // Notify that log was updated (for auto-scroll)
                LogUpdated?.Invoke(this, EventArgs.Empty);
            });
        }

        public void TrackRuntimeAgentUpdate(
            string? agentName,
            string? message,
            bool isComplete,
            string? runId = null)
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return;
            }

            var update = RuntimeAgentActivityViewModel.Create(agentName, message, isComplete, runId);
            Dispatcher.UIThread.Post(() =>
            {
                var existing = RuntimeAgentActivities
                    .Select((activity, index) => new { Activity = activity, Index = index })
                    .FirstOrDefault(item => MatchesRuntimeAgentActivity(item.Activity, update));

                if (existing is null)
                {
                    RuntimeAgentActivities.Insert(0, update);
                }
                else
                {
                    RuntimeAgentActivities.RemoveAt(existing.Index);
                    RuntimeAgentActivities.Insert(0, update);
                }

                while (RuntimeAgentActivities.Count > MaxRuntimeAgentActivityDisplayCount)
                {
                    RuntimeAgentActivities.RemoveAt(RuntimeAgentActivities.Count - 1);
                }

                this.RaisePropertyChanged(nameof(HasRuntimeAgentActivities));

                if (SelectedRuntimeAgentActivity is null || MatchesRuntimeAgentActivity(SelectedRuntimeAgentActivity, update))
                {
                    SelectRuntimeAgentRun(update);
                }
            });
        }

        private static bool MatchesRuntimeAgentActivity(
            RuntimeAgentActivityViewModel existing,
            RuntimeAgentActivityViewModel update)
        {
            if (!string.IsNullOrWhiteSpace(existing.RunId)
                && !string.IsNullOrWhiteSpace(update.RunId))
            {
                return existing.RunId.Equals(update.RunId, StringComparison.OrdinalIgnoreCase);
            }

            return existing.AgentName.Equals(update.AgentName, StringComparison.OrdinalIgnoreCase);
        }

        private void ShowRuntimeAgentRunDetail(RuntimeAgentActivityViewModel? activity)
        {
            if (activity is null)
            {
                return;
            }

            SelectRuntimeAgentRun(activity);
        }

        private void RefreshSelectedRuntimeAgentRun()
        {
            if (SelectedRuntimeAgentActivity is not null)
            {
                SelectRuntimeAgentRun(SelectedRuntimeAgentActivity);
            }
        }

        private void SelectRuntimeAgentRun(RuntimeAgentActivityViewModel activity)
        {
            SelectedRuntimeAgentActivity = activity;
            var run = FindRuntimeAgentRun(activity);
            SelectedRuntimeAgentRun = run is null
                ? null
                : RuntimeAgentRunDetailViewModel.Create(run);
        }

        private RuntimeAgentRun? FindRuntimeAgentRun(RuntimeAgentActivityViewModel activity)
        {
            if (_runtimeAgentRunStore is null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(activity.RunId))
            {
                var run = _runtimeAgentRunStore.Get(activity.RunId);
                if (run is not null)
                {
                    return run;
                }
            }

            return _runtimeAgentRunStore
                .List()
                .FirstOrDefault(run => run.AgentName.Equals(activity.AgentName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Event raised when a new log entry is added (for auto-scroll)
        /// </summary>
        public event EventHandler? LogUpdated;
        
        /// <summary>
        /// Event raised when status properties change and UI needs immediate refresh
        /// </summary>
        public event EventHandler? StatusChanged;

        /* ========================= */
        /* VOICE COMMAND METHODS */
        /* ========================= */

        /// <summary>
        /// Sets the voice command service
        /// </summary>
        public void SetVoiceCommandService(VoiceCommandService voiceCommandService)
        {
            _voiceCommandService = voiceCommandService;
            
            // Subscribe to voice events
            _voiceCommandService.StatusChanged += OnVoiceStatusChanged;
            _voiceCommandService.OnTranscriptionResult += OnVoiceTranscriptionResult;
            _voiceCommandService.OnError += OnVoiceError;

            IsVoiceEnabled = false;
            IsListeningForWakeWord = false;
            VoiceStatusText = "Voice: Off";
            VoiceStatusColor = s_voiceIdleColor;
            _trayIconService?.SetVoiceEnabled(false);
            _trayIconService?.UpdateStatus("Ready", false);
        }

        private void OnVoiceStatusChanged(object? sender, VoiceStatusEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsListeningForWakeWord = e.Status == VoiceStatus.ListeningForWakeWord;
                IsRecordingVoice = e.Status == VoiceStatus.Recording;
                VoiceStatusText = e.Message;
                
                // Update status color based on state (using cached brushes)
                VoiceStatusColor = e.Status switch
                {
                    VoiceStatus.ListeningForWakeWord => s_voiceListeningColor,
                    VoiceStatus.WakeWordDetected => s_voiceWakeWordColor,
                    VoiceStatus.Recording => s_voiceRecordingColor,
                    VoiceStatus.Processing or VoiceStatus.Transcribing => s_voiceProcessingColor,
                    VoiceStatus.Error => s_voiceRecordingColor, // Red (reuse)
                    _ => s_voiceIdleColor
                };
                
                // Add to log for important states
                if (e.Status is VoiceStatus.WakeWordDetected or VoiceStatus.Error)
                {
                    AddLog(e.Message);
                }
            });
        }

        private void OnVoiceTranscriptionResult(object? sender, string text)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AddLog($"🎤 Voice: '{text}'");
            });
        }

        private void OnVoiceError(object? sender, string error)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AddLog($"❌ Voice Error: {error}");
            });
        }

        private void ToggleVoiceEnabled()
        {
            if (_voiceCommandService == null)
            {
                AddLog("⚠️ Voice service not available");
                return;
            }

            if (IsVoiceEnabled)
            {
                _voiceCommandService.StopWakeWordDetection();
                IsVoiceEnabled = false;
                VoiceStatusText = "Voice: Off";
                VoiceStatusColor = Brush.Parse("#6B7280");
                AddLog("🛑 Voice control disabled");
            }
            else
            {
                _voiceCommandService.StartWakeWordDetection();
                IsVoiceEnabled = true;
                AddLog("🎤 Voice control enabled - Say 'Hey Kam'");
            }

            // Sync tray icon menu state
            _trayIconService?.SetVoiceEnabled(IsVoiceEnabled);
            _trayIconService?.UpdateStatus(IsVoiceEnabled ? "Listening" : "Ready", IsVoiceEnabled);
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

        private async Task StartVoiceRecordingAsync()
        {
            if (_voiceCommandService == null)
            {
                AddLog("⚠️ Voice service not available");
                return;
            }

            await _voiceCommandService.StartVoiceRecordingAsync();
        }

        /* ========================= */
        /* SIMULATION - DISABLED */
        /* ========================= */

        public void StartSimulation()
        {
            // Simulation disabled - only real agent logs are shown
            // This method kept for compatibility but does nothing
        }

        /* ========================= */
        /* CLEANUP */
        /* ========================= */

        public void Cleanup()
        {
            _simulationTimer?.Stop();
            _simulationTimer = null;
            
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

            if (_skillExecutionHistoryService is not null)
            {
                _skillExecutionHistoryService.Changed -= OnSkillExecutionHistoryChanged;
            }

            if (_skillPlannerTraceStore is not null)
            {
                _skillPlannerTraceStore.Changed -= OnSkillPlannerTraceChanged;
            }

            _voiceCommandService?.Dispose();
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
            CreatedAtText = request.CreatedAt.ToLocalTime().ToString("HH:mm:ss");
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

    public sealed class SlashCommandSuggestionViewModel
        : ReactiveObject
    {
        public SlashCommandSuggestionViewModel(SlashCommandDefinition definition)
        {
            Name = definition.Name;
            Summary = definition.Summary;
            Usage = definition.Usage;
            Category = definition.Category;
        }

        public string Name { get; }

        public string Summary { get; }

        public string Usage { get; }

        public string Category { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
    }

    public sealed class RuntimeAgentActivityViewModel
    {
        private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#38BDF8"));
        private static readonly IBrush CompletedBrush = new SolidColorBrush(Color.Parse("#10B981"));
        private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#EF4444"));

        private RuntimeAgentActivityViewModel(
            string? runId,
            string agentName,
            string displayName,
            string statusText,
            string lastMessage,
            string updatedText,
            IBrush statusBrush)
        {
            RunId = runId;
            AgentName = agentName;
            DisplayName = displayName;
            StatusText = statusText;
            LastMessage = lastMessage;
            UpdatedText = updatedText;
            StatusBrush = statusBrush;
        }

        public string? RunId { get; }

        public string AgentName { get; }

        public string DisplayName { get; }

        public string StatusText { get; }

        public string LastMessage { get; }

        public string UpdatedText { get; }

        public IBrush StatusBrush { get; }

        public static RuntimeAgentActivityViewModel Create(
            string agentName,
            string? message,
            bool isComplete,
            string? runId = null)
        {
            var normalizedMessage = string.IsNullOrWhiteSpace(message)
                ? "Working on the request."
                : message.Trim();
            var failed = normalizedMessage.Contains("failed", StringComparison.OrdinalIgnoreCase);
            var statusText = failed
                ? "Failed"
                : isComplete
                    ? "Done"
                    : "Running";
            var statusBrush = failed
                ? FailedBrush
                : isComplete
                    ? CompletedBrush
                    : RunningBrush;

            return new RuntimeAgentActivityViewModel(
                string.IsNullOrWhiteSpace(runId) ? null : runId.Trim(),
                agentName.Trim(),
                FormatAgentDisplayName(agentName),
                statusText,
                normalizedMessage,
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                statusBrush);
        }

        private static string FormatAgentDisplayName(string value)
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

            var words = SplitCodeName(cleaned).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(words))
            {
                words = "task";
            }

            return string.IsNullOrWhiteSpace(sequence)
                ? $"{ToSentenceStart(words)} agent"
                : $"{ToSentenceStart(words)} agent {sequence}";
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
    }

    public sealed class RuntimeAgentRunDetailViewModel
    {
        private RuntimeAgentRunDetailViewModel(
            string runId,
            string displayName,
            string statusText,
            string modelIdText,
            string roleText,
            string taskText,
            string durationText,
            string lastMessageText,
            string responseText,
            string errorText,
            IReadOnlyList<RuntimeAgentObservationDetailViewModel> observations)
        {
            RunId = runId;
            DisplayName = displayName;
            StatusText = statusText;
            ModelIdText = modelIdText;
            RoleText = roleText;
            TaskText = taskText;
            DurationText = durationText;
            LastMessageText = lastMessageText;
            ResponseText = responseText;
            ErrorText = errorText;
            Observations = observations;
        }

        public string RunId { get; }

        public string DisplayName { get; }

        public string StatusText { get; }

        public string ModelIdText { get; }

        public string RoleText { get; }

        public string TaskText { get; }

        public string DurationText { get; }

        public string LastMessageText { get; }

        public string ResponseText { get; }

        public string ErrorText { get; }

        public bool HasResponse => !string.IsNullOrWhiteSpace(ResponseText);

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

        public IReadOnlyList<RuntimeAgentObservationDetailViewModel> Observations { get; }

        public bool HasObservations => Observations.Count > 0;

        public static RuntimeAgentRunDetailViewModel Create(RuntimeAgentRun run)
        {
            var observations = (run.ToolObservations ?? [])
                .Take(6)
                .Select(RuntimeAgentObservationDetailViewModel.Create)
                .ToArray();

            return new RuntimeAgentRunDetailViewModel(
                run.RunId,
                RuntimeAgentActivityViewModel.Create(run.AgentName, run.LastMessage, run.Status is not RuntimeAgentRunStatus.Running, run.RunId).DisplayName,
                FormatStatus(run.Status),
                string.IsNullOrWhiteSpace(run.ModelId) ? "Default model" : run.ModelId.Trim(),
                string.IsNullOrWhiteSpace(run.Role) ? "General task" : run.Role.Trim(),
                TrimForDisplay(run.Task, 260),
                FormatDuration(run.StartedAt, run.CompletedAt),
                string.IsNullOrWhiteSpace(run.LastMessage) ? "Working on the request." : run.LastMessage.Trim(),
                TrimForDisplay(run.Response, 360),
                TrimForDisplay(run.ErrorMessage, 260),
                observations);
        }

        private static string FormatStatus(RuntimeAgentRunStatus status)
        {
            return status switch
            {
                RuntimeAgentRunStatus.Running => "Running",
                RuntimeAgentRunStatus.Succeeded => "Completed",
                RuntimeAgentRunStatus.Failed => "Failed",
                RuntimeAgentRunStatus.Canceled => "Canceled",
                _ => "Unknown"
            };
        }

        private static string FormatDuration(DateTimeOffset startedAt, DateTimeOffset? completedAt)
        {
            var end = completedAt ?? DateTimeOffset.UtcNow;
            var duration = end - startedAt;
            if (duration < TimeSpan.Zero)
            {
                duration = TimeSpan.Zero;
            }

            if (duration.TotalMinutes >= 1)
            {
                return $"{Math.Floor(duration.TotalMinutes):0}m {duration.Seconds:00}s";
            }

            var seconds = Math.Max(1, (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
            return $"{seconds}s";
        }

        private static string TrimForDisplay(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed[..Math.Max(0, maxLength - 1)] + "...";
        }
    }

    public sealed class RuntimeAgentObservationDetailViewModel
    {
        private RuntimeAgentObservationDetailViewModel(
            string displayName,
            string statusText,
            string summaryText)
        {
            DisplayName = displayName;
            StatusText = statusText;
            SummaryText = summaryText;
        }

        public string DisplayName { get; }

        public string StatusText { get; }

        public string SummaryText { get; }

        public static RuntimeAgentObservationDetailViewModel Create(RuntimeAgentToolObservation observation)
        {
            return new RuntimeAgentObservationDetailViewModel(
                FormatToolName(observation.SkillId),
                observation.Success ? "Ready" : "Unavailable",
                TrimForDisplay(observation.Summary, 220));
        }

        private static string FormatToolName(string? tool)
        {
            return (tool ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "file.read_lines" => "Read file",
                "workspace.search_text" => "Search text",
                "git.diff_summary" => "Diff summary",
                "workspace.map" => "Workspace map",
                _ => "Context"
            };
        }

        private static string TrimForDisplay(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= maxLength
                ? trimmed
                : trimmed[..Math.Max(0, maxLength - 1)] + "...";
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

    public sealed class SkillPlannerTraceItemViewModel
    {
        public SkillPlannerTraceItemViewModel(SkillPlannerTraceEntry entry)
        {
            TimestampText = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            StatusText = entry.IsValid ? "Valid" : "Invalid";
            SkillIdText = string.IsNullOrWhiteSpace(entry.SkillId)
                ? "no skill"
                : entry.SkillId;
            ConfidenceText = entry.Confidence > 0
                ? $"confidence {entry.Confidence:0.00}"
                : "confidence n/a";
            DurationText = entry.DurationMilliseconds <= 0
                ? "<1 ms"
                : $"{entry.DurationMilliseconds} ms";
            UserRequestText = entry.UserRequest;
            RawResponseText = entry.RawResponse;
            ErrorText = entry.ErrorMessage;
            ReasoningText = entry.Reasoning;
            AvailableSkillCountText = $"{entry.AvailableSkillCount} skills";
        }

        public string TimestampText { get; }

        public string StatusText { get; }

        public string SkillIdText { get; }

        public string ConfidenceText { get; }

        public string DurationText { get; }

        public string UserRequestText { get; }

        public string RawResponseText { get; }

        public string ErrorText { get; }

        public string ReasoningText { get; }

        public string AvailableSkillCountText { get; }

        public bool HasRawResponse => !string.IsNullOrWhiteSpace(RawResponseText);

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

        public bool HasReasoning => !string.IsNullOrWhiteSpace(ReasoningText);
    }

    public sealed class SkillExecutionHistoryItemViewModel
    {
        public SkillExecutionHistoryItemViewModel(
            SkillExecutionHistoryEntry entry,
            Action<string, string>? copy = null,
            Action<SkillExecutionHistoryItemViewModel>? rerun = null)
        {
            SkillId = entry.SkillId;
            StatusText = FormatStatusText(entry.Status);
            TimestampText = entry.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            DurationText = entry.DurationMilliseconds <= 0
                ? "<1 ms"
                : $"{entry.DurationMilliseconds} ms";
            ResultSummary = entry.ResultSummary;
            ArgumentsSummary = entry.ArgumentsSummary;
            ErrorCode = entry.ErrorCode;
            StdOut = entry.StdOut;
            StdErr = entry.StdErr;
            ExitCodeText = entry.ExitCode.HasValue ? $"exit {entry.ExitCode.Value}" : string.Empty;
            RuntimeFlagsText = FormatRuntimeFlags(entry);
            DetailText = FormatDetailText(entry);
            ReplayPlanJson = entry.ReplayPlanJson;
            CanRerun = entry.CanReplay && !string.IsNullOrWhiteSpace(entry.ReplayPlanJson);
            RerunBlockedReason = entry.ReplayBlockedReason;
            CopyResultText = FormatCopyResultText(entry, DetailText, StatusText, DurationText);
            CopyResultCommand = ReactiveCommand.Create(() => copy?.Invoke("result", CopyResultText));
            CopyStdOutCommand = ReactiveCommand.Create(() => copy?.Invoke("stdout", StdOut));
            CopyStdErrCommand = ReactiveCommand.Create(() => copy?.Invoke("stderr", StdErr));
            RerunCommand = ReactiveCommand.Create(() =>
            {
                if (CanRerun)
                {
                    rerun?.Invoke(this);
                }
            });
        }

        public string SkillId { get; }

        public string StatusText { get; }

        public string TimestampText { get; }

        public string DurationText { get; }

        public string ResultSummary { get; }

        public string ArgumentsSummary { get; }

        public string ErrorCode { get; }

        public string DetailText { get; }

        public string StdOut { get; }

        public string StdErr { get; }

        public string ExitCodeText { get; }

        public string RuntimeFlagsText { get; }

        public string ReplayPlanJson { get; }

        public string RerunBlockedReason { get; }

        public string CopyResultText { get; }

        public bool CanRerun { get; }

        public bool HasArguments => !string.IsNullOrWhiteSpace(ArgumentsSummary);

        public bool HasDetailText => !string.IsNullOrWhiteSpace(DetailText);

        public bool HasStdOut => !string.IsNullOrWhiteSpace(StdOut);

        public bool HasStdErr => !string.IsNullOrWhiteSpace(StdErr);

        public bool HasExitCode => !string.IsNullOrWhiteSpace(ExitCodeText);

        public bool HasRuntimeFlags => !string.IsNullOrWhiteSpace(RuntimeFlagsText);

        public bool HasRerunBlockedReason => !string.IsNullOrWhiteSpace(RerunBlockedReason);

        public ICommand CopyResultCommand { get; }

        public ICommand CopyStdOutCommand { get; }

        public ICommand CopyStdErrCommand { get; }

        public ICommand RerunCommand { get; }

        public static string FormatStatusText(SkillExecutionStatus status)
        {
            return status switch
            {
                SkillExecutionStatus.Cancelled => "Cancelled",
                SkillExecutionStatus.Disabled => "Disabled",
                SkillExecutionStatus.ExecutorNotFound => "Executor Missing",
                SkillExecutionStatus.Failed => "Failed",
                SkillExecutionStatus.PermissionDenied => "Permission Denied",
                SkillExecutionStatus.ReviewRequired => "Review Required",
                SkillExecutionStatus.SkillNotFound => "Skill Missing",
                SkillExecutionStatus.Succeeded => "Succeeded",
                SkillExecutionStatus.TimedOut => "Timed Out",
                SkillExecutionStatus.ValidationFailed => "Validation Failed",
                _ => status.ToString()
            };
        }

        private static string FormatRuntimeFlags(SkillExecutionHistoryEntry entry)
        {
            var flags = new List<string>();
            if (entry.TimedOut)
            {
                flags.Add("timed out");
            }

            if (entry.Cancelled)
            {
                flags.Add("cancelled");
            }

            if (entry.Truncated)
            {
                flags.Add("truncated");
            }

            return string.Join(", ", flags);
        }

        private static string FormatDetailText(SkillExecutionHistoryEntry entry)
        {
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.ResultSummary))
            {
                lines.Add(entry.ResultSummary);
            }

            if (!string.IsNullOrWhiteSpace(entry.ErrorCode))
            {
                lines.Add($"error: {entry.ErrorCode}");
            }

            if (!string.IsNullOrWhiteSpace(entry.ArgumentsSummary))
            {
                lines.Add($"args: {entry.ArgumentsSummary}");
            }

            if (!string.IsNullOrWhiteSpace(entry.Command))
            {
                lines.Add($"command: {entry.Command}");
            }

            if (!string.IsNullOrWhiteSpace(entry.WorkingDirectory))
            {
                lines.Add($"cwd: {entry.WorkingDirectory}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string FormatCopyResultText(
            SkillExecutionHistoryEntry entry,
            string detailText,
            string statusText,
            string durationText)
        {
            var lines = new List<string>
            {
                $"Skill: {entry.SkillId}",
                $"Status: {statusText}",
                $"Duration: {durationText}"
            };

            if (!string.IsNullOrWhiteSpace(detailText))
            {
                lines.Add(string.Empty);
                lines.Add(detailText);
            }

            if (!string.IsNullOrWhiteSpace(entry.StdOut))
            {
                lines.Add(string.Empty);
                lines.Add("StdOut:");
                lines.Add(entry.StdOut);
            }

            if (!string.IsNullOrWhiteSpace(entry.StdErr))
            {
                lines.Add(string.Empty);
                lines.Add("StdErr:");
                lines.Add(entry.StdErr);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }
}

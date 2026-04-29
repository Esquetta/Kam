using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private readonly IModelConnectionTestService _modelConnectionTestService = new ModelConnectionTestService();

        private const int MaxSkillExecutionHistoryScanCount = 50;
        private const int MaxSkillExecutionHistoryDisplayCount = 8;
        private const int MaxSkillPlannerTraceDisplayCount = 5;
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
            set => this.RaiseAndSetIfChanged(ref _commandInputText, value);
        }

        public ICommand SubmitCommand { get; }

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
            SubmitCommand = ReactiveCommand.Create(SubmitCommandInput);
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

            CurrentViewModel = view switch
            {
                NavView.Coordinator => new CoordinatorViewModel(_hostControl, this),
                NavView.Diagnostics => new RuntimeDiagnosticsViewModel(
                    new JsonSettingsService(),
                    _hostControl,
                    _skillHealthService,
                    _modelConnectionTestService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog,
                    _skillExecutionHistoryService,
                    _skillPlannerTraceStore,
                    CopyRuntimeDiagnosticsText),
                NavView.Plugins => CreatePluginsViewModel(),
                NavView.Integrations => new IntegrationsViewModel(),
                NavView.Settings => new SettingsViewModel(this),
                _ => null
            };

            CurrentViewModel?.OnNavigatedTo();
            AddLog($"NAVIGATED_TO: {view.ToString().ToUpper()}");
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

        private void SubmitCommandInput()
        {
            if (string.IsNullOrWhiteSpace(CommandInputText) || _commandInput == null)
                return;

            AddLog($"> {CommandInputText}");
            _commandInput.SubmitCommand(CommandInputText);
            CommandInputText = string.Empty;
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
            Console.WriteLine($"[AddLog] Called with message: {message}");
            
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}";

            // Always use dispatcher to ensure UI updates properly
            Dispatcher.UIThread.Post(() =>
            {
                Console.WriteLine($"[AddLog] Adding to LogEntries on UI thread: {logEntry}");
                Console.WriteLine($"[AddLog] Current LogEntries count: {LogEntries.Count}");
                
                // Add to end (chat style - new messages at bottom)
                LogEntries.Add(logEntry);

                // Keep only last 100 messages
                if (LogEntries.Count > 100)
                    LogEntries.RemoveAt(0);

                _trayIconService?.UpdateToolTip($"Kam - {message}");
                
                // Notify that log was updated (for auto-scroll)
                LogUpdated?.Invoke(this, EventArgs.Empty);
                
                Console.WriteLine($"[AddLog] LogEntries count after add: {LogEntries.Count}");
            });
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
            
            // Auto-start wake word detection
            ToggleVoiceEnabled();
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

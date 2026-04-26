using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Collections.ObjectModel;
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
        public ICommand NavigateToPluginsCommand { get; }
        public ICommand NavigateToIntegrationsCommand { get; }
        public ICommand NavigateToSettingsCommand { get; }
        public ICommand ToggleThemeCommand { get; }

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
        /* NEURAL ORB */
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
            NavigateToPluginsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Plugins));
            NavigateToIntegrationsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Integrations));
            NavigateToSettingsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Settings));
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
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
                AddLog("KERNEL_INITIALIZED... v3.5");
                AddLog("NEURAL_LINK_STABLE");
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
                && _skillEvalCaseCatalog is not null)
            {
                return new PluginsViewModel(
                    _skillHealthService,
                    _skillEvalHarness,
                    _skillEvalCaseCatalog);
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
                AddLog("Kam - AI Voice Assistant v1.0");
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

                _trayIconService?.UpdateToolTip($"KAM NEURAL - {message}");
                
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

            _voiceCommandService?.Dispose();
        }
    }
}

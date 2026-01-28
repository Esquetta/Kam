using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
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
        /* COMMANDS */
        /* ========================= */

        public ICommand NavigateToCoordinatorCommand { get; }
        public ICommand NavigateToPluginsCommand { get; }
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
        /* CONSTRUCTOR */
        /* ========================= */

        public MainWindowViewModel()
        {
            

            // Commands
            NavigateToCoordinatorCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Coordinator));
            NavigateToPluginsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Plugins));
            NavigateToSettingsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Settings));
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
            SubmitCommand = ReactiveCommand.Create(SubmitCommandInput);

           
            Dispatcher.UIThread.Post(() =>
            {
                // Initialize theme
                if (global::Avalonia.Application.Current != null)
                {
                    IsDarkMode = global::Avalonia.Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                }

                // Initialize View
                CurrentViewModel = new CoordinatorViewModel();
                ActiveView = NavView.Coordinator;

                // Initial system log only - no simulation
                AddLog("KERNEL_INITIALIZED... v3.5");
                AddLog("NEURAL_LINK_STABLE");
            }, DispatcherPriority.Background);
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
                NavView.Coordinator => new CoordinatorViewModel(),
                NavView.Plugins => new PluginsViewModel(),
                NavView.Settings => new SettingsViewModel(this),
                _ => null
            };

            CurrentViewModel?.OnNavigatedTo();
            AddLog($"NAVIGATED_TO: {view.ToString().ToUpper()}");
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
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            if (Dispatcher.UIThread.CheckAccess())
            {
                // Add to end (chat style - new messages at bottom)
                LogEntries.Add($"[{timestamp}] {message}");

                // Keep only last 100 messages
                if (LogEntries.Count > 100)
                    LogEntries.RemoveAt(0);

                _trayIconService?.UpdateToolTip($"KAM NEURAL - {message}");
                
                // Notify that log was updated (for auto-scroll)
                LogUpdated?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogEntries.Add($"[{timestamp}] {message}");

                    if (LogEntries.Count > 100)
                        LogEntries.RemoveAt(0);

                    _trayIconService?.UpdateToolTip($"KAM NEURAL - {message}");
                    
                    LogUpdated?.Invoke(this, EventArgs.Empty);
                });
            }
        }

        /// <summary>
        /// Event raised when a new log entry is added (for auto-scroll)
        /// </summary>
        public event EventHandler? LogUpdated;

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
        }
    }
}
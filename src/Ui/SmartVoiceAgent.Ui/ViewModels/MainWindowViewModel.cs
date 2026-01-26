using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private TrayIconService? _trayIconService;
        private DispatcherTimer? _simulationTimer; 

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
        /* CONSTRUCTOR */
        /* ========================= */

        public MainWindowViewModel()
        {
            

            // Commands
            NavigateToCoordinatorCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Coordinator));
            NavigateToPluginsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Plugins));
            NavigateToSettingsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Settings));
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);

           
            Dispatcher.UIThread.Post(() =>
            {
                // Initialize theme
                if (Application.Current != null)
                {
                    IsDarkMode = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
                }

                // Initialize View
                CurrentViewModel = new CoordinatorViewModel();
                ActiveView = NavView.Coordinator;

                
                StartSimulation();
                AddLog("SYSTEM_INITIALIZED...");
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
                if (Application.Current is not { } app)
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

        /* ========================= */
        /* LOGGING */
        /* ========================= */

        public void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            // Her zaman UI thread'de çalıştır
            if (Dispatcher.UIThread.CheckAccess())
            {
                // Zaten UI thread'deyiz
                LogEntries.Insert(0, $"[{timestamp}] {message}");

                if (LogEntries.Count > 100)
                    LogEntries.RemoveAt(LogEntries.Count - 1);

                _trayIconService?.UpdateToolTip($"KAM NEURAL - {message}");
            }
            else
            {
                
                Dispatcher.UIThread.Post(() =>
                {
                    LogEntries.Insert(0, $"[{timestamp}] {message}");

                    if (LogEntries.Count > 100)
                        LogEntries.RemoveAt(LogEntries.Count - 1);

                    _trayIconService?.UpdateToolTip($"KAM NEURAL - {message}");
                });
            }
        }

        /* ========================= */
        /* SIMULATION */
        /* ========================= */

        public void StartSimulation()
        {
            
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(StartSimulation);
                return;
            }

            var random = new Random();
            _simulationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };

            _simulationTimer.Tick += (sender, args) =>
            {
                
                TaskProgress = (TaskProgress + 2) % 100;

                string[] tasks =
                {
                    "ANALYZING_NODE",
                    "SYNCING_CORES",
                    "CRITICAL_ERROR",
                    "VOICE_RECOGNITION"
                };

                string task = tasks[random.Next(tasks.Length)];
                AddLog($"{task}... OK");

                if (task.Contains("ERROR"))
                {
                    CurrentOrbColor = Brush.Parse("#FF3B30");
                    _trayIconService?.UpdateStatus("Error Detected");
                }
                else
                {
                    CurrentOrbColor = Brush.Parse("#00D4FF");
                    _trayIconService?.UpdateStatus("Running");
                }
            };

            _simulationTimer?.Start();
        }

        /* ========================= */
        /* CLEANUP */
        /* ========================= */

        public void Cleanup()
        {
            _simulationTimer?.Stop();
            _simulationTimer = null;
        }
    }
}
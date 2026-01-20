using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.Services.Abstract;
using SmartVoiceAgent.Ui.Services.Concrete;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private TrayIconService? _trayIconService;

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
        /* CONSTRUCTOR */
        /* ========================= */

        public MainWindowViewModel(INavigationService? navigationService = null)
        {
            _navigationService = navigationService ?? new NavigationService();
            _navigationService.NavigationChanged += OnNavigationChanged;

            // Commands
            NavigateToCoordinatorCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Coordinator));
            NavigateToPluginsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Plugins));
            NavigateToSettingsCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Settings));
            ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);

            // Initialize theme
            if (Application.Current != null)
            {
                IsDarkMode = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
            }

            // Start simulation
            StartSimulation();
            AddLog("SYSTEM_INITIALIZED...");
        }

        /* ========================= */
        /* NAVIGATION */
        /* ========================= */

        private void NavigateTo(NavView view)
        {
            _navigationService.NavigateTo(view);
        }

        private void OnNavigationChanged(object? sender, NavView newView)
        {
            CurrentViewModel?.OnNavigatedFrom();
            ActiveView = newView;

            CurrentViewModel = newView switch
            {
                NavView.Plugins => new PluginsViewModel(),
                _ => null
            };

            CurrentViewModel?.OnNavigatedTo();
            AddLog($"NAVIGATED_TO: {newView.ToString().ToUpper()}");
        }

        /* ========================= */
        /* THEME */
        /* ========================= */

        private void ApplyTheme(bool isDark)
        {
            if (Application.Current is not { } app)
                return;

            app.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
            AddLog($"THEME_CHANGED: {(isDark ? "DARK" : "LIGHT")}");
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

            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Insert(0, $"[{timestamp}] {message}");

                if (LogEntries.Count > 100)
                    LogEntries.RemoveAt(LogEntries.Count - 1);

                _trayIconService?.UpdateToolTip($"KAM NEURAL - {message}");
            });
        }

        /* ========================= */
        /* SIMULATION */
        /* ========================= */

        public void StartSimulation()
        {
            var random = new Random();
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };

            timer.Tick += (_, _) =>
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

            timer.Start();
        }
    }
}
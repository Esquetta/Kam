using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.ObjectModel;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public partial class MainWindowViewModel : ReactiveObject
    {
        // Tray icon servisi referansı
        private TrayIconService? _trayIconService;

        /* ========================= */
        /* NAVIGATION */
        /* ========================= */

        private NavView _activeView = NavView.Coordinator;
        public NavView ActiveView
        {
            get => _activeView;
            set => this.RaiseAndSetIfChanged(ref _activeView, value);
        }

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
        /* TASK / SIMULATION */
        /* ========================= */

        private double _taskProgress;
        public double TaskProgress
        {
            get => _taskProgress;
            set => this.RaiseAndSetIfChanged(ref _taskProgress, value);
        }

        /* ========================= */
        /* THEME (DARK / LIGHT) */
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

        private void ApplyTheme(bool isDark)
        {
            if (Application.Current is not { } app)
                return;

            app.RequestedThemeVariant =
                isDark ? ThemeVariant.Dark : ThemeVariant.Light;

            AddLog($"SYSTEM_THEME_TOGGLED: {(isDark ? "DARK" : "LIGHT")}");
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

        /* ========================= */
        /* TRAY ICON SERVICE */
        /* ========================= */

        /// <summary>
        /// Tray icon servisini ViewModel'e bağlar
        /// </summary>
        public void SetTrayIconService(TrayIconService service)
        {
            _trayIconService = service;
        }

        /* ========================= */
        /* COMMANDS / ACTIONS */
        /* ========================= */

        public void SetView(string viewName)
        {
            if (Enum.TryParse(viewName, out NavView target))
            {
                ActiveView = target;
                AddLog($"MOUNTED_VIEW: {target.ToString().ToUpper()}");
            }
        }

        public void ToggleTheme()
        {
            IsDarkMode = !IsDarkMode;
        }

        public void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");

            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Insert(0, $"[{timestamp}] {message}");

                // Tray tooltip'i güncelle (son log mesajını göster)
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

                // Hata durumunda tray status'u güncelle
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

            // İlk başlatma logu
            AddLog("SYSTEM_INITIALIZED...");
            _trayIconService?.UpdateStatus("Running");
        }

        /* ========================= */
        /* CTOR */
        /* ========================= */

        public MainWindowViewModel()
        {
            if (Application.Current != null)
            {
                IsDarkMode =
                    Application.Current.ActualThemeVariant == ThemeVariant.Dark;
            }

            StartSimulation();
        }
    }
}
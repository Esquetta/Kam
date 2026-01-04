using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;

namespace SmartVoiceAgent.Ui.ViewModels
{

    public partial class MainWindowViewModel : ReactiveObject
    {
        private NavView _activeView = NavView.Coordinator;
        public NavView ActiveView
        {
            get => _activeView;
            set => this.RaiseAndSetIfChanged(ref _activeView, value);
        }

        private ObservableCollection<string> _logEntries = new ObservableCollection<string>();
        public ObservableCollection<string> LogEntries
        {
            get => _logEntries;
            set => this.RaiseAndSetIfChanged(ref _logEntries, value);
        }

        private double _taskProgress;
        public double TaskProgress
        {
            get => _taskProgress;
            set => this.RaiseAndSetIfChanged(ref _taskProgress, value);
        }

        // UI tarafındaki Güneş/Ay ikon geçişini tetiklemek için IsDarkMode ekledik
        private bool _isDarkMode = true;
        public bool IsDarkMode
        {
            get => _isDarkMode;
            set => this.RaiseAndSetIfChanged(ref _isDarkMode, value);
        }

        private IBrush _currentOrbColor = Brush.Parse("#00D4FF");
        public IBrush CurrentOrbColor
        {
            get => _currentOrbColor;
            set => this.RaiseAndSetIfChanged(ref _currentOrbColor, value);
        }

        public void SetView(string viewName)
        {
            if (Enum.TryParse(viewName, out NavView target))
            {
                ActiveView = target;
                AddLog($"MOUNTED_VIEW: {target.ToString().ToUpper()}");
            }
        }

        public void AddLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            // Logların UI'da donmaması için Dispatcher.UIThread kullanımı (Opsiyonel ama tavsiye edilir)
            Dispatcher.UIThread.Post(() =>
            {
                LogEntries.Insert(0, $"[{timestamp}] {message}");
            });
        }

        public void ToggleTheme()
        {
            var app = Application.Current;
            if (app != null)
            {
                // Mevcut durumu tersine çevir
                IsDarkMode = !IsDarkMode;

                app.RequestedThemeVariant = IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;

                // Pencere sınıflarını (Classes) güncellemek bazı özel CSS-like stiller için faydalıdır
                var mainWindow = (app.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (mainWindow != null)
                {
                    if (IsDarkMode)
                    {
                        if (!mainWindow.Classes.Contains("Dark")) mainWindow.Classes.Add("Dark");
                        mainWindow.Classes.Remove("Light");
                    }
                    else
                    {
                        if (!mainWindow.Classes.Contains("Light")) mainWindow.Classes.Add("Light");
                        mainWindow.Classes.Remove("Dark");
                    }
                }

                AddLog($"SYSTEM_THEME_TOGGLED: {(IsDarkMode ? "DARK" : "LIGHT")}");
            }
        }

        public void StartSimulation()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (s, e) =>
            {
                TaskProgress = (TaskProgress + 2) % 100;
                string[] tasks = { "ANALYZING_NODE", "SYNCING_CORES", "CRITICAL_ERROR", "VOICE_RECOGNITION" };

                string newTask = tasks[new Random().Next(tasks.Length)];

                AddLog($"{newTask}... OK");

                // Hata durumunda Orb rengini değiştirme
                if (newTask.Contains("ERROR"))
                    CurrentOrbColor = Brush.Parse("#FF3B30");
                else
                    CurrentOrbColor = Brush.Parse("#00D4FF");
            };
            timer.Start();
        }

        public MainWindowViewModel()
        {
            // İlk açılışta sistemin gerçek temasını yakala
            if (Application.Current != null)
            {
                IsDarkMode = Application.Current.ActualThemeVariant == ThemeVariant.Dark;
            }

            StartSimulation();
            AddLog("SYSTEM_INITIALIZED...");
        }
    }
}
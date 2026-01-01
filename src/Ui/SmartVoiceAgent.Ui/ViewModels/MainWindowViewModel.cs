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
            LogEntries.Insert(0, $"[{timestamp}] {message}");
        }

        public void ToggleTheme()
        {
            var app = Application.Current;
            if (app != null)
            {
                app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
                    ? ThemeVariant.Light : ThemeVariant.Dark;
                AddLog("SYSTEM_THEME_TOGGLED");
            }
        }

        public void StartSimulation()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (s, e) =>
            {
                TaskProgress = (TaskProgress + 2) % 100;
                string[] tasks = { "ANALYZING_NODE", "SYNCING_CORES", "CRITICAL_ERROR", "VOICE_RECOGNITION" };

                // FIXED: Declare variable inside this scope
                string newTask = tasks[new Random().Next(tasks.Length)];

                AddLog($"{newTask}... OK");

                if (newTask.Contains("ERROR"))
                    CurrentOrbColor = Brush.Parse("#FF3B30");
                else
                    CurrentOrbColor = Brush.Parse("#00D4FF");
            };
            timer.Start();
        }

        public MainWindowViewModel()
        {
            StartSimulation();
            AddLog("SYSTEM_INITIALIZED...");
        }
    }
}
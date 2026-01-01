using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartVoiceAgent.Ui.ViewModels
{
    
    public partial class MainWindowViewModel : ReactiveObject
    {
        private double _taskProgress = 45.0;
        public double TaskProgress
        {
            get => _taskProgress;
            set => this.RaiseAndSetIfChanged(ref _taskProgress, value);
        }

        private string _researchLogs = "SYSTEM_INITIALIZED...";
        public string ResearchLogs
        {
            get => _researchLogs;
            set => this.RaiseAndSetIfChanged(ref _researchLogs, value);
        }
        private Points _analyzerPoints = new Points();
        public Points AnalyzerPoints
        {
            get => _analyzerPoints;
            set => this.RaiseAndSetIfChanged(ref _analyzerPoints, value);
        }
        private List<double> _rawPoints = new List<double>(Enumerable.Repeat(50.0, 20));
        private IBrush _currentOrbColor = Brush.Parse("#00F2FF");
        public IBrush CurrentOrbColor
        {
            get => _currentOrbColor;
            set => this.RaiseAndSetIfChanged(ref _currentOrbColor, value);
        }
        public void StartSimulation()
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };

            timer.Tick += (s, e) =>
            {
                TaskProgress = (TaskProgress + 1.5) % 100;

                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string[] fakeTasks = { "ANALYZING_NODE", "ENCRYPTING_DATA", "VOICE_RECOGNITION", "SYNCING_CORES", "CRITICAL_ERROR_71", "SYSTEM_OVERLOAD" };

                // Declare the variable here first!
                string newTask = fakeTasks[new Random().Next(fakeTasks.Length)];

                ResearchLogs = $"[{timestamp}] {newTask}... OK\n" + ResearchLogs;

                // Now you can use newTask
                if (newTask.Contains("ERROR") || newTask.Contains("OVERLOAD"))
                {
                    CurrentOrbColor = Brush.Parse("#FF3B30"); // Red
                }
                else
                {
                    CurrentOrbColor = Brush.Parse("#00F2FF"); // Cyan
                }
            };

            timer.Start();
        }
    }
}
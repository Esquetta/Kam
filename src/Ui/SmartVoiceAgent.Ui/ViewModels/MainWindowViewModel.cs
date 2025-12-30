using Avalonia.Threading;
using ReactiveUI;
using System;

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
        public void StartSimulation()
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800) // Updates every 0.8 seconds
            };

            timer.Tick += (s, e) =>
            {
                // 1. Update Progress Bar (loops back to 0 at 100)
                TaskProgress = (TaskProgress + 1.5) % 100;

                // 2. Generate Fake Logs
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                string[] fakeTasks = { "ANALYZING_NODE", "ENCRYPTING_DATA", "VOICE_RECOGNITION_ACTIVE", "SYNCING_CORES" };
                string randomTask = fakeTasks[new Random().Next(fakeTasks.Length)];

                // Add new log to the top
                ResearchLogs = $"[{timestamp}] {randomTask}... OK\n" + ResearchLogs;
            };

            timer.Start();
        }
    }
}
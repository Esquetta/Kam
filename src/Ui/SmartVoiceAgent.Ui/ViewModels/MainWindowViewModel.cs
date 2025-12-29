using ReactiveUI;
namespace SmartVoiceAgent.Ui.ViewModels
{
    public partial class MainWindowViewModel : ReactiveObject
    {
        private double _taskProgress = 45.0;
        private string _researchLogs = "SYSTEM_INITIALIZED...";

        // This property allows the ProgressBar to work
        public double TaskProgress
        {
            get => _taskProgress;
            set => this.RaiseAndSetIfChanged(ref _taskProgress, value);
        }

        // This property allows the Research TextBlock to work
        public string ResearchLogs
        {
            get => _researchLogs;
            set => this.RaiseAndSetIfChanged(ref _researchLogs, value);
        }
    }


}


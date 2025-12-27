using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SmartVoiceAgent.Ui.Views
{
    public partial class MainWindow : Window
    {
        private double _taskProgress = 45.0; // Default starting value

        public double TaskProgress
        {
            get => _taskProgress;
            set => this.RaiseAndSetIfChanged(ref _taskProgress, value);
        }

        private string _researchLogs = "INITIALIZING_SYSTEM_KERNELS...\nCONNECTED_TO_NODE_A...\nLISTENING_FOR_VOICE_COMMANDS...";

        public string ResearchLogs
        {
            get => _researchLogs;
            set => this.RaiseAndSetIfChanged(ref _researchLogs, value);
        }
        public MainWindow()
        {
            InitializeComponent();
        }
        
    }
}
using Avalonia.Media;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Ui.ViewModels;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class CoordinatorViewModel : ViewModelBase
    {
        private readonly IVoiceAgentHostControl? _hostControl;
        private readonly MainWindowViewModel? _mainViewModel;

        /* ========================= */
        /* ONLINE/OFFLINE STATE */
        /* ========================= */

        private bool _isOnline = true;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (this.RaiseAndSetIfChanged(ref _isOnline, value))
                {
                    // Update dependent properties when state changes
                    this.RaisePropertyChanged(nameof(StatusText));
                    this.RaisePropertyChanged(nameof(StatusColor));
                    this.RaisePropertyChanged(nameof(OrbColor));
                    this.RaisePropertyChanged(nameof(OrbGlowColor));
                    this.RaisePropertyChanged(nameof(LabelColor));
                    this.RaisePropertyChanged(nameof(ResearchStatusColor));
                    this.RaisePropertyChanged(nameof(AnalyzerStatusColor));
                    this.RaisePropertyChanged(nameof(TasksStatusColor));
                }
            }
        }

        /* ========================= */
        /* STATUS DISPLAY (OVERRIDE) */
        /* ========================= */

        public override string StatusText => IsOnline ? "SYSTEM ONLINE" : "SYSTEM OFFLINE";

        public override IBrush StatusColor => IsOnline 
            ? Brush.Parse("#10B981") // AccentGreenBrush
            : Brush.Parse("#EF4444"); // Red for offline

        /* ========================= */
        /* NEURAL ORB COLORS */
        /* ========================= */

        public IBrush OrbColor => IsOnline
            ? Brush.Parse("#06B6D4") // Cyan when online
            : Brush.Parse("#EF4444"); // Red when offline

        public IBrush OrbGlowColor => IsOnline
            ? Brush.Parse("#4006B6D4") // Cyan glow
            : Brush.Parse("#40EF4444"); // Red glow

        /* ========================= */
        /* LABEL COLORS */
        /* ========================= */

        public IBrush LabelColor => IsOnline
            ? Brush.Parse("#F8FAFC") // TextPrimaryBrush
            : Brush.Parse("#EF4444"); // Red when offline

        /* ========================= */
        /* CARD STATUS COLORS */
        /* ========================= */

        public IBrush ResearchStatusColor => IsOnline
            ? Brush.Parse("#A855F7") // AccentPurpleBrush
            : Brush.Parse("#EF4444"); // Red when offline

        public IBrush AnalyzerStatusColor => IsOnline
            ? Brush.Parse("#10B981") // AccentGreenBrush
            : Brush.Parse("#EF4444"); // Red when offline

        public IBrush TasksStatusColor => IsOnline
            ? Brush.Parse("#F97316") // AccentOrangeBrush
            : Brush.Parse("#EF4444"); // Red when offline

        /* ========================= */
        /* COMMANDS */
        /* ========================= */

        public ICommand ToggleOnlineStateCommand { get; }

        /* ========================= */
        /* CONSTRUCTOR */
        /* ========================= */

        public CoordinatorViewModel()
        {
            Title = "COORDINATOR";
            ToggleOnlineStateCommand = ReactiveCommand.Create(ToggleOnlineState);
        }

        public CoordinatorViewModel(IVoiceAgentHostControl? hostControl, MainWindowViewModel? mainViewModel) : this()
        {
            _hostControl = hostControl;
            _mainViewModel = mainViewModel;
            
            // Sync with host state
            if (_hostControl != null)
            {
                IsOnline = _hostControl.IsRunning;
            }
        }

        /* ========================= */
        /* METHODS */
        /* ========================= */

        private async void ToggleOnlineState()
        {
            if (_mainViewModel != null)
            {
                // Use the main view model to toggle the host
                await _mainViewModel.ToggleHostAsync();
            }
            else
            {
                // Fallback: just toggle local state if no host control
                IsOnline = !IsOnline;
            }
        }

        /// <summary>
        /// Syncs the view model state with the host state (called from MainWindowViewModel)
        /// </summary>
        public void SyncWithHostState(bool isHostRunning)
        {
            IsOnline = isHostRunning;
        }

        public override void OnNavigatedTo()
        {
        }

        public override void OnNavigatedFrom()
        {
        }
    }
}

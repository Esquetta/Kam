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
            ? GetThemeTextBrush() // Theme-aware text color
            : Brush.Parse("#EF4444"); // Red when offline
        
        /// <summary>
        /// Gets the appropriate text color based on current theme (dark/light)
        /// </summary>
        private IBrush GetThemeTextBrush()
        {
            var app = global::Avalonia.Application.Current;
            if (app?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark)
            {
                return Brush.Parse("#FAFAFA"); // White for dark mode
            }
            return Brush.Parse("#18181B"); // Dark for light mode
        }

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
            // Use CreateFromTask for async commands
            ToggleOnlineStateCommand = ReactiveCommand.CreateFromTask(ToggleOnlineStateAsync);
            
            // Subscribe to theme changes to update LabelColor (always subscribe, regardless of constructor)
            var app = global::Avalonia.Application.Current;
            if (app != null)
            {
                app.ActualThemeVariantChanged += OnThemeChanged;
            }
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
        
        private void OnThemeChanged(object? sender, System.EventArgs e)
        {
            // Theme changed - raise property changed for theme-dependent properties
            this.RaisePropertyChanged(nameof(LabelColor));
        }

        /* ========================= */
        /* METHODS */
        /* ========================= */

        private async Task ToggleOnlineStateAsync()
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
            // Unsubscribe from theme changes to prevent memory leaks
            var app = global::Avalonia.Application.Current;
            if (app != null)
            {
                app.ActualThemeVariantChanged -= OnThemeChanged;
            }
        }
    }
}

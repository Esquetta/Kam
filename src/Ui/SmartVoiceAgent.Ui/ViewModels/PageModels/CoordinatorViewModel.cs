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
                    // Update all dependent properties
                    UpdateStatusProperties();
                }
            }
        }

        /* ========================= */
        /* STATUS DISPLAY (OVERRIDE) */
        /* ========================= */

        private string _statusText = "SYSTEM ONLINE";
        private IBrush _statusColor = Brush.Parse("#10B981");
        private IBrush _orbColor = Brush.Parse("#06B6D4");
        private IBrush _orbGlowColor = Brush.Parse("#4006B6D4");
        private IBrush _labelColor;
        private IBrush _researchStatusColor = Brush.Parse("#A855F7");
        private IBrush _analyzerStatusColor = Brush.Parse("#10B981");
        private IBrush _tasksStatusColor = Brush.Parse("#F97316");

        public override string StatusText
        {
            get => _statusText;
            protected set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public override IBrush StatusColor
        {
            get => _statusColor;
            protected set => this.RaiseAndSetIfChanged(ref _statusColor, value);
        }

        /* ========================= */
        /* NEURAL ORB COLORS */
        /* ========================= */

        public IBrush OrbColor
        {
            get => _orbColor;
            private set => this.RaiseAndSetIfChanged(ref _orbColor, value);
        }

        public IBrush OrbGlowColor
        {
            get => _orbGlowColor;
            private set => this.RaiseAndSetIfChanged(ref _orbGlowColor, value);
        }

        /* ========================= */
        /* LABEL COLORS */
        /* ========================= */

        public IBrush LabelColor
        {
            get => _labelColor ?? GetThemeTextBrush();
            private set => this.RaiseAndSetIfChanged(ref _labelColor, value);
        }
        
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

        public IBrush ResearchStatusColor
        {
            get => _researchStatusColor;
            private set => this.RaiseAndSetIfChanged(ref _researchStatusColor, value);
        }

        public IBrush AnalyzerStatusColor
        {
            get => _analyzerStatusColor;
            private set => this.RaiseAndSetIfChanged(ref _analyzerStatusColor, value);
        }

        public IBrush TasksStatusColor
        {
            get => _tasksStatusColor;
            private set => this.RaiseAndSetIfChanged(ref _tasksStatusColor, value);
        }

        /// <summary>
        /// Updates all status-dependent properties based on IsOnline value
        /// </summary>
        private void UpdateStatusProperties()
        {
            StatusText = IsOnline ? "SYSTEM ONLINE" : "SYSTEM OFFLINE";
            StatusColor = IsOnline ? Brush.Parse("#10B981") : Brush.Parse("#EF4444");
            OrbColor = IsOnline ? Brush.Parse("#06B6D4") : Brush.Parse("#EF4444");
            OrbGlowColor = IsOnline ? Brush.Parse("#4006B6D4") : Brush.Parse("#40EF4444");
            LabelColor = IsOnline ? GetThemeTextBrush() : Brush.Parse("#EF4444");
            ResearchStatusColor = IsOnline ? Brush.Parse("#A855F7") : Brush.Parse("#EF4444");
            AnalyzerStatusColor = IsOnline ? Brush.Parse("#10B981") : Brush.Parse("#EF4444");
            TasksStatusColor = IsOnline ? Brush.Parse("#F97316") : Brush.Parse("#EF4444");
        }

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
            
            // Initialize status properties
            UpdateStatusProperties();
            
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
            // Always update and notify, even if value is the same
            // This ensures UI refreshes immediately
            _isOnline = isHostRunning;
            this.RaisePropertyChanged(nameof(IsOnline));
            UpdateStatusProperties();
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

using Avalonia.Media;
using ReactiveUI;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class CoordinatorViewModel : ViewModelBase
    {
        private readonly IVoiceAgentHostControl? _hostControl;
        private readonly MainWindowViewModel? _mainViewModel;

        /* ========================= */
        /* CACHED BRUSHES */
        /* ========================= */
        // Static brushes to avoid repeated allocations and color parsing
        private static readonly IBrush OnlineStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));
        private static readonly IBrush OfflineStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444"));
        private static readonly IBrush OnlineOrbColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#06B6D4"));
        private static readonly IBrush OfflineOrbColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444"));
        private static readonly IBrush OnlineOrbGlowColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#4006B6D4"));
        private static readonly IBrush OfflineOrbGlowColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#40EF4444"));
        private static readonly IBrush ResearchOnlineColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#A855F7"));
        private static readonly IBrush AnalyzerOnlineColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));
        private static readonly IBrush TasksOnlineColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#F97316"));
        private static readonly IBrush DarkThemeTextColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#FAFAFA"));
        private static readonly IBrush LightThemeTextColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#18181B"));

        /* ========================= */
        /* ONLINE/OFFLINE STATE */
        /* ========================= */

        private bool _isOnline = true;
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                // Always update and notify, even if value hasn't changed
                _isOnline = value;
                this.RaisePropertyChanged(nameof(IsOnline));
                
                // Update all dependent properties
                UpdateStatusProperties();
            }
        }

        /* ========================= */
        /* STATUS DISPLAY (OVERRIDE) */
        /* ========================= */

        private string _statusText = "SYSTEM ONLINE";
        private IBrush _statusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));
        private IBrush _orbColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#06B6D4"));
        private IBrush _orbGlowColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#4006B6D4"));
        private IBrush? _labelColor;
        private IBrush _researchStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#A855F7"));
        private IBrush _analyzerStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));
        private IBrush _tasksStatusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#F97316"));

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
        /// Uses cached brushes to minimize allocations
        /// </summary>
        private IBrush GetThemeTextBrush()
        {
            var app = global::Avalonia.Application.Current;
            // Return cached brush based on theme
            return app?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark 
                ? DarkThemeTextColor 
                : LightThemeTextColor;
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
        /// Uses cached brushes to minimize allocations
        /// </summary>
        private void UpdateStatusProperties()
        {
            // Use cached brushes to avoid repeated allocations
            StatusText = IsOnline ? "SYSTEM ONLINE" : "SYSTEM OFFLINE";
            StatusColor = IsOnline ? OnlineStatusColor : OfflineStatusColor;
            OrbColor = IsOnline ? OnlineOrbColor : OfflineOrbColor;
            OrbGlowColor = IsOnline ? OnlineOrbGlowColor : OfflineOrbGlowColor;
            LabelColor = IsOnline ? GetThemeTextBrush() : OfflineStatusColor;
            ResearchStatusColor = IsOnline ? ResearchOnlineColor : OfflineStatusColor;
            AnalyzerStatusColor = IsOnline ? AnalyzerOnlineColor : OfflineStatusColor;
            TasksStatusColor = IsOnline ? TasksOnlineColor : OfflineStatusColor;
            
            // Raise property change for IsOnline to ensure converters re-evaluate
            this.RaisePropertyChanged(nameof(IsOnline));
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
            Console.WriteLine($"[CoordinatorViewModel] ToggleOnlineStateAsync called, _mainViewModel is null: {_mainViewModel == null}");
            
            if (_mainViewModel != null)
            {
                // Use the main view model to toggle the host
                Console.WriteLine("[CoordinatorViewModel] Calling ToggleHostAsync...");
                await _mainViewModel.ToggleHostAsync();
                Console.WriteLine("[CoordinatorViewModel] ToggleHostAsync completed");
            }
            else
            {
                // Fallback: just toggle local state if no host control
                Console.WriteLine("[CoordinatorViewModel] No main view model, toggling local state only");
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
            
            // Force property change notifications for all status properties
            // First set to null to force binding refresh, then set actual values
            this.RaisePropertyChanged(nameof(IsOnline));
            
            // Update status properties with forced refresh
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

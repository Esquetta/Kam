using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReactiveUI;
using System;
using System.Reactive.Linq;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Manages window state and provides responsive breakpoint information
/// </summary>
public class WindowStateManager : ReactiveObject
{
    private double _windowWidth = 1280; // Default to expanded
    private double _windowHeight = 800;
    private bool _isCompact;
    private bool _isMedium;
    private bool _isExpanded = true; // Default to expanded
    private int _gridColumns = 3;
    private bool _isLogPanelVisible = true;
    private bool _isNarrow;
    private bool _isRailCompact;
    private bool _reducedMotion;

    /// <summary>Kam Studio breakpoint: below this width the Tasks context pane hides.</summary>
    public const double ContextPaneBreakpoint = 1280;

    /// <summary>Kam Studio breakpoint: below this width grids stack and the top bar trims.</summary>
    public const double NarrowBreakpoint = 980;

    /// <summary>Kam Studio breakpoint: below this width the rail shows icons only.</summary>
    public const double RailBreakpoint = 760;

    private static readonly WindowStateManager _instance = new();

    /// <summary>
    /// Gets the shared instance. XAML must bind to it with <c>{x:Static services:WindowStateManager.Instance}</c>;
    /// a new instance would never receive window size updates.
    /// </summary>
    public static WindowStateManager Instance => _instance;

    /// <summary>
    /// Creates a standalone manager. Production code uses <see cref="Instance"/>; this exists for tests.
    /// </summary>
    internal WindowStateManager() { }

    /// <summary>
    /// Current window width
    /// </summary>
    public double WindowWidth
    {
        get => _windowWidth;
        private set
        {
            this.RaiseAndSetIfChanged(ref _windowWidth, value);
            UpdateBreakpoints();
        }
    }

    /// <summary>
    /// Current window height
    /// </summary>
    public double WindowHeight
    {
        get => _windowHeight;
        private set => this.RaiseAndSetIfChanged(ref _windowHeight, value);
    }

    /// <summary>
    /// Compact breakpoint (mobile/tablet) - < 1024px
    /// </summary>
    public bool IsCompact
    {
        get => _isCompact;
        private set => this.RaiseAndSetIfChanged(ref _isCompact, value);
    }

    /// <summary>
    /// Medium breakpoint (small desktop) - 1024px - 1440px
    /// </summary>
    public bool IsMedium
    {
        get => _isMedium;
        private set => this.RaiseAndSetIfChanged(ref _isMedium, value);
    }

    /// <summary>
    /// Expanded breakpoint (large desktop) - > 1440px
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        private set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }

    /// <summary>
    /// Number of columns for grid layouts based on current breakpoint
    /// </summary>
    public int GridColumns
    {
        get => _gridColumns;
        private set => this.RaiseAndSetIfChanged(ref _gridColumns, value);
    }

    /// <summary>
    /// Below 980 px (Kam Studio): grids stack, the top bar drops the wake pill and the search text.
    /// </summary>
    public bool IsNarrow
    {
        get => _isNarrow;
        private set => this.RaiseAndSetIfChanged(ref _isNarrow, value);
    }

    /// <summary>
    /// Below 760 px (Kam Studio): the rail shows icons only and the task list sits above the thread.
    /// </summary>
    public bool IsRailCompact
    {
        get => _isRailCompact;
        private set => this.RaiseAndSetIfChanged(ref _isRailCompact, value);
    }

    /// <summary>
    /// Whether the Tasks context pane (the Activity panel) fits: 1280 px and wider, as in Kam Studio.
    /// </summary>
    public bool IsLogPanelVisible
    {
        get => _isLogPanelVisible;
        private set => this.RaiseAndSetIfChanged(ref _isLogPanelVisible, value);
    }

    /// <summary>
    /// User preference for reduced motion (accessibility)
    /// </summary>
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set => this.RaiseAndSetIfChanged(ref _reducedMotion, value);
    }

    /// <summary>
    /// Attaches to a window to track its size changes
    /// </summary>
    public void AttachToWindow(Window window)
    {
        if (window == null) return;

        // Set initial values
        WindowWidth = window.Bounds.Width;
        WindowHeight = window.Bounds.Height;

        // Bounds changes are raised on the UI thread; keep them there so bound
        // properties are never updated from a background scheduler.
        window.GetObservable(Visual.BoundsProperty)
            .DistinctUntilChanged()
            .Subscribe(bounds =>
            {
                WindowWidth = bounds.Width;
                WindowHeight = bounds.Height;
            });
    }

    private void UpdateBreakpoints()
    {
        // Grid columns: Compact < 1024px (1), Medium 1024px - 1440px (2-3), Expanded > 1440px (3).
        // Kam Studio shell: context pane from 1280px, narrow top bar below 980px, icon rail below 760px.

        IsCompact = WindowWidth < 1024;
        IsMedium = WindowWidth >= 1024 && WindowWidth < 1440;
        IsExpanded = WindowWidth >= 1440;
        IsNarrow = WindowWidth < NarrowBreakpoint;
        IsRailCompact = WindowWidth < RailBreakpoint;

        // Update grid columns based on breakpoint
        if (IsCompact)
        {
            GridColumns = 1;
        }
        else if (IsMedium)
        {
            GridColumns = WindowWidth >= 1200 ? 3 : 2;
        }
        else
        {
            GridColumns = 3;
        }

        IsLogPanelVisible = WindowWidth >= ContextPaneBreakpoint;
    }
}

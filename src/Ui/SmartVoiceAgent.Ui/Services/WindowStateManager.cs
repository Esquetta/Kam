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
    private double _windowWidth;
    private double _windowHeight;
    private bool _isCompact;
    private bool _isMedium;
    private bool _isExpanded;
    private int _gridColumns = 3;
    private bool _isLogPanelVisible = true;
    private bool _reducedMotion;

    private static readonly WindowStateManager _instance = new();
    public static WindowStateManager Instance => _instance;

    // Public constructor for XAML resource instantiation (returns same instance)
    public WindowStateManager() { }

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
    /// Whether the log panel should be visible
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

        // Subscribe to bounds changes
        window.GetObservable(Visual.BoundsProperty)
            .Throttle(TimeSpan.FromMilliseconds(50))
            .Subscribe(bounds =>
            {
                WindowWidth = bounds.Width;
                WindowHeight = bounds.Height;
            });
    }

    private void UpdateBreakpoints()
    {
        // Breakpoints:
        // Compact: < 1024px (hide log panel, single column)
        // Medium: 1024px - 1440px (show log panel, 2 columns)
        // Expanded: > 1440px (show log panel, 3 columns)

        IsCompact = WindowWidth < 1024;
        IsMedium = WindowWidth >= 1024 && WindowWidth < 1440;
        IsExpanded = WindowWidth >= 1440;

        // Update grid columns based on breakpoint
        if (IsCompact)
        {
            GridColumns = 1;
            IsLogPanelVisible = false;
        }
        else if (IsMedium)
        {
            GridColumns = 2;
            IsLogPanelVisible = true;
        }
        else
        {
            GridColumns = 3;
            IsLogPanelVisible = true;
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.Views;

public partial class NeuralOrb : UserControl
{
    public NeuralOrb()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Orb color - changes based on online/offline state
    /// </summary>
    public static readonly StyledProperty<IBrush> OrbColorProperty =
        AvaloniaProperty.Register<NeuralOrb, IBrush>(nameof(OrbColor), Brush.Parse("#06B6D4"));

    public IBrush OrbColor
    {
        get => GetValue(OrbColorProperty);
        set => SetValue(OrbColorProperty, value);
    }

    /// <summary>
    /// IsActive - binds to the online state
    /// </summary>
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<NeuralOrb, bool>(nameof(IsActive), true);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// ToggleCommand - executes when the orb is clicked
    /// </summary>
    public static readonly StyledProperty<ICommand?> ToggleCommandProperty =
        AvaloniaProperty.Register<NeuralOrb, ICommand?>(nameof(ToggleCommand));

    public ICommand? ToggleCommand
    {
        get => GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    /// <summary>
    /// Ring opacity - lower when offline
    /// </summary>
    public static readonly StyledProperty<double> RingOpacityProperty =
        AvaloniaProperty.Register<NeuralOrb, double>(nameof(RingOpacity), 0.3);

    public double RingOpacity
    {
        get => GetValue(RingOpacityProperty);
        set => SetValue(RingOpacityProperty, value);
    }

    /// <summary>
    /// Core opacity - lower when offline
    /// </summary>
    public static readonly StyledProperty<double> CoreOpacityProperty =
        AvaloniaProperty.Register<NeuralOrb, double>(nameof(CoreOpacity), 0.65);

    public double CoreOpacity
    {
        get => GetValue(CoreOpacityProperty);
        set => SetValue(CoreOpacityProperty, value);
    }

    /// <summary>
    /// Box shadow value - changes based on state
    /// </summary>
    public static readonly StyledProperty<string> BoxShadowValueProperty =
        AvaloniaProperty.Register<NeuralOrb, string>(nameof(BoxShadowValue), "0 0 40 0 #4006B6D4");

    public string BoxShadowValue
    {
        get => GetValue(BoxShadowValueProperty);
        set => SetValue(BoxShadowValueProperty, value);
    }
}

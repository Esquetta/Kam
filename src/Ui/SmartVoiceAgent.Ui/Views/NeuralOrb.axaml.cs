using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace SmartVoiceAgent.Ui.Views;

public partial class NeuralOrb : UserControl
{
    public NeuralOrb()
    {
        InitializeComponent();
        var btn = this.FindControl<ToggleButton>("OrbButton");
        if (btn != null)
        {
            btn.Click += (s, e) =>
            {
                bool? isActive = btn.IsChecked;
            };
        }
    }

    public static readonly StyledProperty<IBrush> OrbColorProperty =
        AvaloniaProperty.Register<NeuralOrb, IBrush>(nameof(OrbColor), Brush.Parse("#06B6D4"));

    public IBrush OrbColor
    {
        get => GetValue(OrbColorProperty);
        set => SetValue(OrbColorProperty, value);
    }

    public static readonly StyledProperty<Color> OrbColorValueProperty =
        AvaloniaProperty.Register<NeuralOrb, Color>(nameof(OrbColorValue), Color.Parse("#06B6D4"));

    public Color OrbColorValue
    {
        get => GetValue(OrbColorValueProperty);
        set => SetValue(OrbColorValueProperty, value);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SmartVoiceAgent.Ui.Views;

public partial class NeuralOrb : UserControl
{
    public NeuralOrb()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<IBrush> OrbColorProperty =
        AvaloniaProperty.Register<NeuralOrb, IBrush>(nameof(OrbColor), Brush.Parse("#00D4FF"));

    public IBrush OrbColor
    {
        get => GetValue(OrbColorProperty);
        set => SetValue(OrbColorProperty, value);
    }
}
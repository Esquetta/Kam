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

    public static readonly StyledProperty<IBrush> ActiveColorProperty =
        AvaloniaProperty.Register<NeuralOrb, IBrush>(nameof(ActiveColor));

    public IBrush ActiveColor
    {
        get => GetValue(ActiveColorProperty);
        set => SetValue(ActiveColorProperty, value);
    }

}
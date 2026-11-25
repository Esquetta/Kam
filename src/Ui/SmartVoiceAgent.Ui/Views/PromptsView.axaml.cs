using Avalonia.Controls;

namespace SmartVoiceAgent.Ui.Views;

/// <summary>
/// The Prompts page: the Markdown slash command library with search, preview and actions.
/// Its DataContext is a <see cref="ViewModels.PageModels.PromptsViewModel"/>.
/// </summary>
public partial class PromptsView : UserControl
{
    // Below this page width the list and the detail stack.
    private const double NarrowWidth = 980;

    /// <summary>Creates the view.</summary>
    public PromptsView()
    {
        InitializeComponent();
        Page.SizeChanged += (_, e) => Page.Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
    }
}

using Avalonia.Controls;

namespace SmartVoiceAgent.Ui.Views;

/// <summary>
/// The Agents page: coding-agent CLIs and recent role-agent runs, with a detail pane.
/// Its DataContext is <see cref="ViewModels.PageModels.AgentsViewModel"/>.
/// </summary>
public partial class AgentsView : UserControl
{
    // Below this page width (about a 980 px window beside the rail) the list and the detail stack.
    private const double NarrowWidth = 760;

    /// <summary>
    /// Creates the page.
    /// </summary>
    public AgentsView()
    {
        InitializeComponent();
        Page.SizeChanged += (_, e) => Page.Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
    }
}

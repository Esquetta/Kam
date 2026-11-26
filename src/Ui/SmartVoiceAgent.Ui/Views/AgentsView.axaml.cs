using Avalonia.Controls;

namespace SmartVoiceAgent.Ui.Views;

/// <summary>
/// The Agents page: coding-agent CLIs and a table of recent role-agent runs, each opening its detail in place.
/// Its DataContext is <see cref="ViewModels.PageModels.AgentsViewModel"/>.
/// </summary>
public partial class AgentsView : UserControl
{
    /// <summary>
    /// Creates the page.
    /// </summary>
    public AgentsView()
    {
        InitializeComponent();
    }
}

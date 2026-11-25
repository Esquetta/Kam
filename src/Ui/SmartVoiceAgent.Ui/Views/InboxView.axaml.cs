using Avalonia.Controls;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// The Needs you page: tool calls waiting for approval across all tasks.
    /// Its DataContext is the window's <see cref="ViewModels.MainWindowViewModel"/>.
    /// </summary>
    public partial class InboxView : UserControl
    {
        public InboxView()
        {
            InitializeComponent();
        }
    }
}

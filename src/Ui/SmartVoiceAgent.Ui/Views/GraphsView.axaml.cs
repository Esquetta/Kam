using Avalonia.Controls;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// The Graphs page: a live graph of a task's main agent and subagents. Its DataContext is a <see cref="GraphsViewModel"/>.
    /// </summary>
    public partial class GraphsView : UserControl
    {
        // Below this page width the task list moves above the graph and the detail below it.
        private const double NarrowWidth = 1100;

        /// <summary>
        /// Creates the page.
        /// </summary>
        public GraphsView()
        {
            InitializeComponent();
            Page.SizeChanged += (_, e) => Page.Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
        }
    }
}

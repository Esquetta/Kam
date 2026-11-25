using Avalonia.Controls;
using Avalonia.Input;
using SmartVoiceAgent.Ui.ViewModels.PageModels;
using System;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// The Runs page: a history of tasks and role-agent runs. Its DataContext is a <see cref="RunsViewModel"/>.
    /// </summary>
    public partial class RunsView : UserControl
    {
        // Below this page width the search moves under the filter.
        private const double NarrowWidth = 620;

        /// <summary>
        /// Creates the page.
        /// </summary>
        public RunsView()
        {
            InitializeComponent();
            Page.SizeChanged += (_, e) => Page.Classes.Set("narrow", e.NewSize.Width < NarrowWidth);
        }

        private void OnRowTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Control { DataContext: RunRowViewModel row } && DataContext is RunsViewModel viewModel)
            {
                viewModel.ActivateRow(row);
            }
        }
    }
}

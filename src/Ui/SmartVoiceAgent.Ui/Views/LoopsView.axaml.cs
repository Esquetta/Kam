using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// The Loops page: step, tool result and context limits for every agent turn.
    /// Its DataContext is a <see cref="ViewModels.PageModels.LoopsViewModel"/>.
    /// </summary>
    public partial class LoopsView : UserControl
    {
        public LoopsView()
        {
            InitializeComponent();
        }

        // The boxes save when they lose focus, so a half-typed number is never clamped and saved; Enter saves too.
        private void OnLimitKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox box)
                {
                    BindingOperations.GetBindingExpressionBase(box, TextBox.TextProperty)?.UpdateSource();
                }
                e.Handled = true;
            }
        }
    }
}

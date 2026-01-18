using Avalonia.Controls;
using Avalonia.Interactivity;
using SmartVoiceAgent.Ui.ViewModels;
using System.ComponentModel;

namespace SmartVoiceAgent.Ui.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            this.Closing += MainWindow_Closed;
            this.Opened += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.StartSimulation();
                }
            };


        }
        private void MainWindow_Closed(object? sender, CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }
        private void ToggleTheme(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ToggleTheme();
        }
    }
}
using Avalonia.Controls;
using Avalonia.Interactivity;
using SmartVoiceAgent.Ui.ViewModels;

namespace SmartVoiceAgent.Ui.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Opened += (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.StartSimulation();
                }
            };
        }
        private void ToggleTheme(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
                vm.ToggleTheme();
        }
    }
}
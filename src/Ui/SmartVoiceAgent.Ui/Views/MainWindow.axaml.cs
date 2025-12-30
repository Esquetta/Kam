using Avalonia.Controls;
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

    }
}
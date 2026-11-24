using Avalonia.Controls;
using Avalonia.Interactivity;
using SmartVoiceAgent.Ui.ViewModels;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.Views;

public partial class RuntimeDiagnosticsView : UserControl
{
    public RuntimeDiagnosticsView()
    {
        InitializeComponent();
    }

    private void OnCardActionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string target }
            || TopLevel.GetTopLevel(this)?.DataContext is not MainWindowViewModel main)
        {
            return;
        }

        ICommand? command = target switch
        {
            "Settings" => main.NavigateToSettingsCommand,
            "Skills" => main.NavigateToPluginsCommand,
            "Extensions" => main.NavigateToExtensionsCommand,
            _ => null
        };
        command?.Execute(null);
    }
}

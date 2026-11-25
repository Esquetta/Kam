using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.ComponentModel;

namespace SmartVoiceAgent.Ui.Controls;

/// <summary>Ctrl+K command palette: a scrim, a search box and a grouped result list bound to <see cref="CommandPaletteViewModel"/>.</summary>
public partial class CommandPalette : UserControl
{
    private CommandPaletteViewModel? _viewModel;

    /// <summary>Creates the palette.</summary>
    public CommandPalette()
    {
        InitializeComponent();
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        Scrim.PointerPressed += (_, e) =>
        {
            _viewModel?.Close();
            e.Handled = true;
        };
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as CommandPaletteViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && _viewModel.IsOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                Input.Focus();
                Input.SelectAll();
            }, DispatcherPriority.Input);
        }
        else if (e.PropertyName == nameof(CommandPaletteViewModel.SelectedIndex))
        {
            var index = _viewModel.SelectedIndex;
            Dispatcher.UIThread.Post(() => List.ContainerFromIndex(index)?.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _viewModel.MoveSelection(1);
                break;
            case Key.Up:
                _viewModel.MoveSelection(-1);
                break;
            case Key.Tab:
                _viewModel.MoveSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                break;
            case Key.Enter:
                _viewModel.ExecuteSelected();
                break;
            case Key.Escape:
                _viewModel.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnRowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: PaletteRow { Item: { } item } }
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _viewModel?.ExecuteCommand(item);
            e.Handled = true;
        }
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.ComponentModel;

namespace SmartVoiceAgent.Ui.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _viewModel;
        private ScrollViewer? _logScrollViewer;

        public MainWindow()
        {
            InitializeComponent();

            // Get reference to ScrollViewer after initialization
            _logScrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");

            this.Closing += MainWindow_Closed;
            this.Opened += MainWindow_Opened;
            this.DataContextChanged += OnDataContextChanged;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            
            // Attach WindowStateManager for responsive design
            WindowStateManager.Instance.AttachToWindow(this);
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Unsubscribe from old view model
            if (_viewModel != null)
            {
                _viewModel.LogUpdated -= OnLogUpdated;
            }

            // Subscribe to new view model
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
            {
                _viewModel.LogUpdated += OnLogUpdated;
            }
        }

        private void OnLogUpdated(object? sender, EventArgs e)
        {
            // Auto-scroll to bottom when new log is added
            if (_logScrollViewer != null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _logScrollViewer.ScrollToEnd();
                });
            }
        }

        private void MainWindow_Opened(object? sender, EventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.StartSimulation();
            }
        }

        private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
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

        private void OnPromptKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm)
            {
                vm.SubmitCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}

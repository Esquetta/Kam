using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace SmartVoiceAgent.Ui.Views
{
    public partial class MainWindow : Window
    {
        private MainWindowViewModel? _viewModel;
        private ScrollViewer? _logScrollViewer;
        private ScrollViewer? _chatScrollViewer;
        private bool _chatFollowsLatest = true;

        public MainWindow()
        {
            InitializeComponent();

            // Get reference to ScrollViewer after initialization
            _logScrollViewer = this.FindControl<ScrollViewer>("LogScrollViewer");
            _chatScrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");
            if (_chatScrollViewer is not null)
            {
                _chatScrollViewer.ScrollChanged += OnChatScrollChanged;
            }

            this.Closing += MainWindow_Closed;
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

        /// <summary>
        /// Keeps the chat pinned to the newest message while the agent streams, unless the user scrolled up to read.
        /// </summary>
        private void OnChatScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_chatScrollViewer is null)
            {
                return;
            }

            // New content, or a taller composer shrinking the view, keeps the newest message in sight.
            if ((e.ExtentDelta.Y > 0 || e.ViewportDelta.Y < 0) && _chatFollowsLatest)
            {
                _chatScrollViewer.ScrollToEnd();
                return;
            }

            var distanceFromBottom = _chatScrollViewer.Extent.Height
                - _chatScrollViewer.Viewport.Height
                - _chatScrollViewer.Offset.Y;
            _chatFollowsLatest = distanceFromBottom <= 48;
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

        private void OnPromptKeyDown(object? sender, KeyEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (e.Key == Key.Down && vm.MoveSlashCommandSelection(1))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Up && vm.MoveSlashCommandSelection(-1))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab && vm.AcceptSelectedSlashCommandSuggestion())
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                vm.HideSlashCommandSuggestions();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (vm.IsSlashCommandPaletteVisible && vm.AcceptSelectedSlashCommandSuggestion())
                {
                    e.Handled = true;
                    return;
                }

                vm.SubmitCommand.Execute(null);
                e.Handled = true;
            }
        }

        private async void OnAttachFilesClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Attach files",
                AllowMultiple = true
            });

            vm.AddComposerAttachmentPaths(files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path)));
        }
    }
}

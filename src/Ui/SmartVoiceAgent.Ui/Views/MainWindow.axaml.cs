using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
        private TextBox? _workbenchPromptInput;
        private TextBox? _chatSearchInput;
        private GlobalTalkShortcut? _talkShortcut;
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

            _workbenchPromptInput = this.FindControl<TextBox>("WorkbenchPromptInput");
            _chatSearchInput = this.FindControl<TextBox>("ChatSearchInput");

            // Tunnel, so Enter sends before a multi-line TextBox turns it into a new line.
            foreach (var prompt in new[] { _workbenchPromptInput, this.FindControl<TextBox>("PromptInput") })
            {
                prompt?.AddHandler(KeyDownEvent, OnPromptKeyDown, RoutingStrategies.Tunnel);
            }

            this.Closing += MainWindow_Closed;
            this.DataContextChanged += OnDataContextChanged;
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);

            // Attach WindowStateManager for responsive design
            WindowStateManager.Instance.AttachToWindow(this);

            // The talk shortcut works while Kam is in the background, through the window's message loop.
            _talkShortcut = new GlobalTalkShortcut(this, () => _viewModel?.ToggleTalk());
            RegisterTalkShortcut();
        }

        protected override void OnClosed(EventArgs e)
        {
            _talkShortcut?.Dispose();
            _talkShortcut = null;
            base.OnClosed(e);
        }

        private void RegisterTalkShortcut()
        {
            if (_talkShortcut is null)
            {
                return;
            }

            var shortcut = _viewModel?.TalkShortcut;
            if (_talkShortcut.Register(shortcut) || _viewModel is null)
            {
                return;
            }

            // On Windows a valid shortcut fails only when another app holds it.
            if (OperatingSystem.IsWindows() && GlobalTalkShortcut.TryParse(shortcut, out _, out _))
            {
                _viewModel.ReportTalkShortcutUnavailable(shortcut!);
            }
        }

        private void OnTalkShortcutChanged(object? sender, EventArgs e)
        {
            RegisterTalkShortcut();
        }

        /// <summary>
        /// Window shortcuts: Ctrl+N new chat, Ctrl+K search chats, Ctrl+L composer, F2 rename,
        /// Ctrl+, Settings, the talk shortcut when Windows doesn't deliver it globally, and Esc to cancel
        /// voice or stop the running turn.
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled || DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            if (_talkShortcut?.IsRegistered != true
                && GlobalTalkShortcut.TryParseGesture(vm.TalkShortcut, out var talk)
                && talk.Matches(e))
            {
                vm.ToggleTalk();
                e.Handled = true;
                return;
            }

            var command = global::Avalonia.Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers
                ?? KeyModifiers.Control;
            if (e.KeyModifiers == command)
            {
                switch (e.Key)
                {
                    case Key.N:
                        ShowChat(vm);
                        vm.NewAgentChatCommand.Execute(null);
                        FocusAfterLayout(_workbenchPromptInput, selectAll: false);
                        e.Handled = true;
                        break;
                    case Key.K:
                        ShowChat(vm);
                        FocusAfterLayout(_chatSearchInput, selectAll: true);
                        e.Handled = true;
                        break;
                    case Key.L:
                        ShowChat(vm);
                        FocusAfterLayout(_workbenchPromptInput, selectAll: false);
                        e.Handled = true;
                        break;
                    case Key.OemComma:
                        vm.NavigateToSettingsCommand.Execute(null);
                        e.Handled = true;
                        break;
                }

                return;
            }

            if (e.KeyModifiers != KeyModifiers.None)
            {
                return;
            }

            if (e.Key == Key.F2 && vm.IsChatWorkbenchVisible)
            {
                vm.RenameAgentChatCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && vm.CancelVoice())
            {
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && vm.IsAgentTurnRunning)
            {
                vm.StopAgentTurnCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            // Unsubscribe from old view model
            if (_viewModel != null)
            {
                _viewModel.LogUpdated -= OnLogUpdated;
                _viewModel.TalkShortcutChanged -= OnTalkShortcutChanged;
            }

            // Subscribe to new view model
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
            {
                _viewModel.LogUpdated += OnLogUpdated;
                _viewModel.TalkShortcutChanged += OnTalkShortcutChanged;
            }

            RegisterTalkShortcut();
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
                if (vm.IsSlashCommandPaletteVisible)
                {
                    vm.HideSlashCommandSuggestions();
                    e.Handled = true;
                }
                else if (vm.CancelVoice())
                {
                    e.Handled = true;
                }
                else if (vm.IsAgentTurnRunning)
                {
                    vm.StopAgentTurnCommand.Execute(null);
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Enter)
            {
                // Shift+Enter is left to the TextBox, which adds a line where it accepts returns.
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    return;
                }

                if (vm.IsSlashCommandPaletteVisible && vm.AcceptSelectedSlashCommandSuggestion())
                {
                    e.Handled = true;
                    return;
                }

                vm.SubmitCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnChatSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape || DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            // The first Esc clears the search; the next one goes back to the composer.
            if (vm.IsAgentChatSearchActive)
            {
                vm.AgentChatSearchText = string.Empty;
            }
            else
            {
                FocusAfterLayout(_workbenchPromptInput, selectAll: false);
            }

            e.Handled = true;
        }

        private void OnRenameChatClick(object? sender, RoutedEventArgs e)
        {
            // Row menus pass their thread; the header menu passes none, which means the selected thread.
            _viewModel?.RenameAgentChatCommand.Execute(ChatFrom(sender));
        }

        private void OnDeleteChatClick(object? sender, RoutedEventArgs e)
        {
            _viewModel?.DeleteAgentChatCommand.Execute(ChatFrom(sender));
        }

        private void OnRenameEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == IsVisibleProperty
                && e.NewValue is true
                && sender is Border { Child: TextBox input })
            {
                FocusAfterLayout(input, selectAll: true);
            }
        }

        private void OnRenameKeyDown(object? sender, KeyEventArgs e)
        {
            var chat = ChatFrom(sender);
            if (e.Key == Key.Enter)
            {
                _viewModel?.CommitAgentChatRename(chat);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                _viewModel?.CancelAgentChatRename(chat);
                e.Handled = true;
            }
        }

        private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
        {
            _viewModel?.CommitAgentChatRename(ChatFrom(sender));
        }

        private static AgentChatSessionViewModel? ChatFrom(object? sender)
        {
            return (sender as StyledElement)?.DataContext as AgentChatSessionViewModel;
        }

        private static void ShowChat(MainWindowViewModel vm)
        {
            if (!vm.IsChatWorkbenchVisible)
            {
                vm.NavigateToCoordinatorCommand.Execute(null);
            }
        }

        /// <summary>
        /// Focuses an input once the layout shows it, for example right after a page switch.
        /// </summary>
        private static void FocusAfterLayout(TextBox? input, bool selectAll)
        {
            if (input is null)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                input.Focus(NavigationMethod.Tab);
                if (selectAll)
                {
                    input.SelectAll();
                }
                else
                {
                    input.CaretIndex = input.Text?.Length ?? 0;
                }
            }, DispatcherPriority.Background);
        }

        private async void OnAttachFilesClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainWindowViewModel vm)
            {
                return;
            }

            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("Chat.Composer.AttachFiles"),
                AllowMultiple = true
            });

            vm.AddComposerAttachmentPaths(files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path)));
        }
    }
}

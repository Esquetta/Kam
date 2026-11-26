using Avalonia;
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
        private TasksView? _tasksView;
        private GlobalTalkShortcut? _talkShortcut;

        public MainWindow()
        {
            InitializeComponent();

            _tasksView = this.FindControl<TasksView>("TasksPanel");

            this.Closing += MainWindow_Closed;
            this.DataContextChanged += OnDataContextChanged;

            // A short window starts the rail's Build and Manage groups collapsed until the user opens them.
            WindowStateManager.Instance.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WindowStateManager.IsShort) && _viewModel is not null)
                {
                    _viewModel.IsWindowShort = WindowStateManager.Instance.IsShort;
                }
            };
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
        /// Window shortcuts: Ctrl+N new chat, Ctrl+K command palette, Ctrl+L composer, F2 rename,
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
                        StartNewTask(vm);
                        e.Handled = true;
                        break;
                    case Key.K:
                        vm.OpenCommandPalette();
                        e.Handled = true;
                        break;
                    case Key.L:
                        ShowChat(vm);
                        _tasksView?.FocusPrompt();
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
                _viewModel.TalkShortcutChanged -= OnTalkShortcutChanged;
                _viewModel.ComposerFocusRequested -= OnComposerFocusRequested;
            }

            // Subscribe to new view model
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel != null)
            {
                _viewModel.TalkShortcutChanged += OnTalkShortcutChanged;
                _viewModel.ComposerFocusRequested += OnComposerFocusRequested;
                _viewModel.IsWindowShort = WindowStateManager.Instance.IsShort;
            }

            RegisterTalkShortcut();
        }

        private void OnHeaderPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void OnTitleSearchClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OpenCommandPalette();
            }
        }

        private void OnNewTaskClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                StartNewTask(vm);
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
                ShowChat(vm);
                _tasksView?.FocusPrompt();
            }

            e.Handled = true;
        }

        private void OnRenameChatClick(object? sender, RoutedEventArgs e)
        {
            // Row menus pass their thread; with none the command acts on the selected thread.
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
                TasksView.FocusAfterLayout(input, selectAll: true);
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

        private void OnComposerFocusRequested(object? sender, EventArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _tasksView?.FocusPrompt());
        }

        private void MainWindow_Closed(object? sender, CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        /// <summary>Opens a new task on the Tasks page and focuses its composer, for Ctrl+N and the rail's New task.</summary>
        private void StartNewTask(MainWindowViewModel vm)
        {
            ShowChat(vm);
            vm.NewAgentChatCommand.Execute(null);
            _tasksView?.FocusPrompt();
        }

        private static void ShowChat(MainWindowViewModel vm)
        {
            if (!vm.IsChatWorkbenchVisible)
            {
                vm.NavigateToCoordinatorCommand.Execute(null);
            }
        }
    }
}

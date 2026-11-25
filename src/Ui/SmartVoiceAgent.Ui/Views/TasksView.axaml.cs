using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;
using System.Linq;

namespace SmartVoiceAgent.Ui.Views
{
    /// <summary>
    /// The Tasks screen: task list, the selected task's conversation and the composer.
    /// Its DataContext is the window's <see cref="MainWindowViewModel"/>.
    /// </summary>
    public partial class TasksView : UserControl
    {
        private readonly ScrollViewer? _chatScrollViewer;
        private readonly TextBox? _promptInput;
        private readonly TextBox? _searchInput;
        private bool _chatFollowsLatest = true;

        public TasksView()
        {
            InitializeComponent();

            _chatScrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");
            if (_chatScrollViewer is not null)
            {
                _chatScrollViewer.ScrollChanged += OnChatScrollChanged;
            }

            _promptInput = this.FindControl<TextBox>("WorkbenchPromptInput");
            _searchInput = this.FindControl<TextBox>("ChatSearchInput");

            // Tunnel, so Enter sends before a multi-line TextBox turns it into a new line.
            _promptInput?.AddHandler(KeyDownEvent, OnPromptKeyDown, RoutingStrategies.Tunnel);
        }

        private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

        /// <summary>Puts the caret at the end of the composer once the layout shows it.</summary>
        public void FocusPrompt() => FocusAfterLayout(_promptInput, selectAll: false);

        /// <summary>Selects the task search text once the layout shows it.</summary>
        public void FocusSearch() => FocusAfterLayout(_searchInput, selectAll: true);

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

        private void OnPromptKeyDown(object? sender, KeyEventArgs e)
        {
            if (ViewModel is not { } vm)
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
            if (e.Key != Key.Escape || ViewModel is not { } vm)
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
                FocusPrompt();
            }

            e.Handled = true;
        }

        private void OnRenameChatClick(object? sender, RoutedEventArgs e)
        {
            // Row menus pass their thread; the header menu passes none, which means the selected thread.
            ViewModel?.RenameAgentChatCommand.Execute(ChatFrom(sender));
        }

        private void OnDeleteChatClick(object? sender, RoutedEventArgs e)
        {
            ViewModel?.DeleteAgentChatCommand.Execute(ChatFrom(sender));
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
                ViewModel?.CommitAgentChatRename(chat);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                ViewModel?.CancelAgentChatRename(chat);
                e.Handled = true;
            }
        }

        private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
        {
            ViewModel?.CommitAgentChatRename(ChatFrom(sender));
        }

        private async void OnAttachFilesClick(object? sender, RoutedEventArgs e)
        {
            if (ViewModel is not { } vm || TopLevel.GetTopLevel(this) is not { } topLevel)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("Chat.Composer.AttachFiles"),
                AllowMultiple = true
            });

            vm.AddComposerAttachmentPaths(files
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrWhiteSpace(path)));
        }

        private static AgentChatSessionViewModel? ChatFrom(object? sender)
        {
            return (sender as StyledElement)?.DataContext as AgentChatSessionViewModel;
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
    }
}

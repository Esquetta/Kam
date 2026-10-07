using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// One chat thread in the sidebar and its timeline. Text it makes itself, such as the
    /// "New chat" title or "5m", follows the interface language when its owner calls
    /// <see cref="RefreshLocalizedText"/>.
    /// </summary>
    public sealed class AgentChatSessionViewModel : ReactiveObject
    {
        /// <summary>
        /// The title the session store gives a saved thread that has no user message yet.
        /// </summary>
        private const string StoreDefaultTitle = "New chat";

        private string _title;
        private string _summary;
        private string _relativeTimeText;
        private bool _isSelected;
        private bool _isRunning;
        private bool _isRenaming;
        private bool _isConfirmingDelete;
        private bool _isVisibleInList = true;
        private string _editTitle = string.Empty;
        private string? _modelId;
        private int _persistedMessageCount;
        private PlaceholderTitle _placeholderTitle;
        private PlaceholderSummary _placeholderSummary;
        private DateTimeOffset? _lastActivityAt;

        private enum PlaceholderTitle
        {
            None,
            WorkspaceChat,
            NewChat
        }

        private enum PlaceholderSummary
        {
            None,
            WorkspaceReady,
            NoMessagesYet,
            Saved
        }

        private AgentChatSessionViewModel(
            string sessionId,
            string title,
            string summary,
            string relativeTimeText)
        {
            SessionId = sessionId;
            _title = title;
            _summary = summary;
            _relativeTimeText = relativeTimeText;
        }

        /// <summary>
        /// Gets the id the agent runtime stores this thread under.
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// Gets whether the saved history still has to be read from disk before the thread is shown.
        /// </summary>
        public bool NeedsHistoryLoad { get; private set; }

        /// <summary>
        /// Gets or sets whether an agent turn is running in this thread.
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRunning, value);
                this.RaisePropertyChanged(nameof(StatusText));
                this.RaisePropertyChanged(nameof(CanDelete));
            }
        }

        /// <summary>
        /// Gets or sets the thread title. Setting it replaces a placeholder such as "New chat" for good.
        /// </summary>
        public string Title
        {
            get => _title;
            set
            {
                _placeholderTitle = PlaceholderTitle.None;
                this.RaiseAndSetIfChanged(ref _title, value);
            }
        }

        /// <summary>
        /// Gets whether the title is still a placeholder such as "New chat", so the first message names the thread.
        /// </summary>
        public bool HasPlaceholderTitle => _placeholderTitle != PlaceholderTitle.None;

        /// <summary>
        /// Gets or sets whether the user named this thread, so its first message no longer sets the title.
        /// </summary>
        public bool HasCustomTitle { get; set; }

        /// <summary>
        /// Gets or sets the line under the title, usually the last message.
        /// </summary>
        public string Summary
        {
            get => _summary;
            set
            {
                _placeholderSummary = PlaceholderSummary.None;
                this.RaiseAndSetIfChanged(ref _summary, value);
            }
        }

        /// <summary>
        /// Gets or sets how long ago the thread was last active, such as "5m". Prefer <see cref="MarkActivity"/>,
        /// which keeps the time so the text can follow the language.
        /// </summary>
        public string RelativeTimeText
        {
            get => _relativeTimeText;
            set
            {
                _lastActivityAt = null;
                this.RaiseAndSetIfChanged(ref _relativeTimeText, value);
            }
        }

        /// <summary>
        /// Gets when the thread was last active, when known.
        /// </summary>
        public DateTimeOffset? LastActivityAt => _lastActivityAt;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                this.RaiseAndSetIfChanged(ref _isSelected, value);
                this.RaisePropertyChanged(nameof(StatusText));
            }
        }

        /// <summary>
        /// Gets or sets the model this thread runs on, or null for the model chosen in Settings.
        /// </summary>
        public string? ModelId
        {
            get => _modelId;
            set => this.RaiseAndSetIfChanged(ref _modelId, value);
        }

        /// <summary>
        /// Gets or sets whether the sidebar shows a title editor for this thread.
        /// </summary>
        public bool IsRenaming
        {
            get => _isRenaming;
            set
            {
                this.RaiseAndSetIfChanged(ref _isRenaming, value);
                this.RaisePropertyChanged(nameof(IsShowingSummary));
            }
        }

        /// <summary>
        /// Gets or sets the title being typed while renaming.
        /// </summary>
        public string EditTitle
        {
            get => _editTitle;
            set => this.RaiseAndSetIfChanged(ref _editTitle, value);
        }

        /// <summary>
        /// Gets or sets whether the sidebar asks to confirm deleting this thread.
        /// </summary>
        public bool IsConfirmingDelete
        {
            get => _isConfirmingDelete;
            set
            {
                this.RaiseAndSetIfChanged(ref _isConfirmingDelete, value);
                this.RaisePropertyChanged(nameof(IsShowingSummary));
            }
        }

        /// <summary>
        /// Gets whether the sidebar row shows its title and summary rather than an editor or a confirmation.
        /// </summary>
        public bool IsShowingSummary => !IsRenaming && !IsConfirmingDelete;

        /// <summary>
        /// Gets whether the thread can be deleted; a running thread has to be stopped first.
        /// </summary>
        public bool CanDelete => !IsRunning;

        /// <summary>
        /// Gets or sets whether the thread matches the sidebar search.
        /// </summary>
        public bool IsVisibleInList
        {
            get => _isVisibleInList;
            set => this.RaiseAndSetIfChanged(ref _isVisibleInList, value);
        }

        public ObservableCollection<AgentChatMessageViewModel> Messages { get; } = new();

        /// <summary>
        /// Gets the name of the agent answering in this thread.
        /// </summary>
        public string AgentName => Loc.Get("Workbench.Thread.AgentName");

        /// <summary>
        /// Gets the thread state shown in the sidebar: working, active or ready.
        /// </summary>
        public string StatusText => IsRunning
            ? Loc.Get("Workbench.State.Working")
            : IsSelected
                ? Loc.Get("Workbench.State.Active")
                : Loc.Get("Workbench.State.Ready");

        /// <summary>
        /// Gets the number of items in the thread, such as "4 messages".
        /// </summary>
        public string MessageCountText => FormatMessageCount(NeedsHistoryLoad ? _persistedMessageCount : Messages.Count);

        /// <summary>
        /// Formats a message count, such as "No messages", "1 message" or "4 messages", in the interface language.
        /// </summary>
        /// <param name="count">The number of messages.</param>
        public static string FormatMessageCount(int count)
        {
            return count switch
            {
                0 => Loc.Get("Workbench.Thread.NoMessages"),
                1 => Loc.Get("Workbench.Thread.OneMessage"),
                _ => Loc.Format("Workbench.Thread.Messages", count)
            };
        }

        /// <summary>
        /// Formats a thread's last activity like "5m", "3h", "Yesterday" or "Oct 2" in the interface language.
        /// </summary>
        /// <param name="value">When the thread was last active.</param>
        /// <param name="now">The current time.</param>
        public static string FormatRelativeTime(DateTimeOffset value, DateTimeOffset now)
        {
            return FormatRelativeTime(value, now, LocalizationService.Instance);
        }

        /// <summary>
        /// Formats a thread's last activity in the language of <paramref name="localization"/>,
        /// for example "5m" or "Oct 2" in English and "5 dk önce" or "2 Eki" in Turkish.
        /// </summary>
        /// <param name="value">When the thread was last active.</param>
        /// <param name="now">The current time.</param>
        /// <param name="localization">The text and culture to format with.</param>
        public static string FormatRelativeTime(DateTimeOffset value, DateTimeOffset now, LocalizationService localization)
        {
            ArgumentNullException.ThrowIfNull(localization);

            var elapsed = now - value;
            if (elapsed < TimeSpan.FromMinutes(1))
            {
                return localization.Get("Workbench.Time.Now");
            }

            if (elapsed < TimeSpan.FromHours(1))
            {
                return localization.Format("Workbench.Time.Minutes", (int)elapsed.TotalMinutes);
            }

            if (elapsed < TimeSpan.FromHours(24) && value.ToLocalTime().Date == now.ToLocalTime().Date)
            {
                return localization.Format("Workbench.Time.Hours", (int)elapsed.TotalHours);
            }

            if (value.ToLocalTime().Date == now.ToLocalTime().Date.AddDays(-1))
            {
                return localization.Get("Workbench.Time.Yesterday");
            }

            return value.ToLocalTime().ToString(localization.Get("Workbench.Time.DateFormat"), localization.Culture);
        }

        /// <summary>
        /// Returns whether the title or summary contains the search text, ignoring case.
        /// </summary>
        public bool Matches(string query)
        {
            return string.IsNullOrWhiteSpace(query)
                || Title.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase)
                || Summary.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        public void AddMessage(AgentChatMessageViewModel message)
        {
            Messages.Add(message);
            this.RaisePropertyChanged(nameof(MessageCountText));
        }

        public void RemoveMessage(AgentChatMessageViewModel message)
        {
            Messages.Remove(message);
            this.RaisePropertyChanged(nameof(MessageCountText));
        }

        /// <summary>
        /// Records activity in the thread now; the sidebar shows it as "now".
        /// </summary>
        /// <param name="at">When the activity happened.</param>
        public void MarkActivity(DateTimeOffset at)
        {
            RelativeTimeText = FormatRelativeTime(at, at);
            _lastActivityAt = at;
        }

        /// <summary>
        /// Rebuilds the text this thread makes itself in the current interface language: placeholder
        /// title and summary, the last-activity time, state and counts, and its items' labels.
        /// Titles the user or the first message set stay as they are.
        /// </summary>
        /// <param name="now">The current time, for the last-activity text.</param>
        public void RefreshLocalizedText(DateTimeOffset now)
        {
            if (_placeholderTitle != PlaceholderTitle.None)
            {
                _title = PlaceholderTitleText(_placeholderTitle);
                this.RaisePropertyChanged(nameof(Title));
            }

            if (_placeholderSummary != PlaceholderSummary.None)
            {
                _summary = PlaceholderSummaryText(_placeholderSummary);
                this.RaisePropertyChanged(nameof(Summary));
            }

            if (_lastActivityAt is { } lastActivityAt)
            {
                _relativeTimeText = FormatRelativeTime(lastActivityAt, now);
                this.RaisePropertyChanged(nameof(RelativeTimeText));
            }

            this.RaisePropertyChanged(nameof(AgentName));
            this.RaisePropertyChanged(nameof(StatusText));
            this.RaisePropertyChanged(nameof(MessageCountText));

            foreach (var message in Messages)
            {
                message.RefreshLocalizedText();
            }
        }

        /// <summary>
        /// Replaces the timeline with history read from disk.
        /// </summary>
        public void LoadHistory(IEnumerable<AgentChatMessageViewModel> messages)
        {
            Messages.Clear();
            foreach (var message in messages)
            {
                Messages.Add(message);
            }

            NeedsHistoryLoad = false;
            this.RaisePropertyChanged(nameof(MessageCountText));
        }

        public static AgentChatSessionViewModel Create(
            string title,
            string summary,
            string relativeTimeText)
        {
            return new AgentChatSessionViewModel(Guid.NewGuid().ToString("N"), title, summary, relativeTimeText);
        }

        /// <summary>
        /// Creates the thread the workbench opens with, titled "Workspace chat" in the interface language.
        /// </summary>
        /// <param name="now">When the thread was created.</param>
        public static AgentChatSessionViewModel CreateWorkspaceChat(DateTimeOffset now)
        {
            return CreatePlaceholder(PlaceholderTitle.WorkspaceChat, PlaceholderSummary.WorkspaceReady, now);
        }

        /// <summary>
        /// Creates an empty thread titled "New chat" in the interface language; its first message names it.
        /// </summary>
        /// <param name="now">When the thread was created.</param>
        public static AgentChatSessionViewModel CreateNewChat(DateTimeOffset now)
        {
            return CreatePlaceholder(PlaceholderTitle.NewChat, PlaceholderSummary.NoMessagesYet, now);
        }

        /// <summary>
        /// Creates a thread saved by an earlier run; its history loads when it is opened.
        /// </summary>
        /// <param name="sessionId">The stored thread id.</param>
        /// <param name="title">The stored title.</param>
        /// <param name="relativeTimeText">The last-activity text to show.</param>
        /// <param name="savedItemCount">The number of stored items, shown until the history loads.</param>
        /// <param name="modelId">The model saved with the thread, or null for the Settings model.</param>
        /// <param name="hasCustomTitle">Whether the user named the thread.</param>
        /// <param name="updatedAt">When the thread was last active, so the time text can follow the language.</param>
        public static AgentChatSessionViewModel CreateSaved(
            string sessionId,
            string title,
            string relativeTimeText,
            int savedItemCount,
            string? modelId = null,
            bool hasCustomTitle = false,
            DateTimeOffset? updatedAt = null)
        {
            // The store names a thread without a user message "New chat"; it stays a placeholder here.
            var placeholderTitle = !hasCustomTitle && string.Equals(title, StoreDefaultTitle, StringComparison.Ordinal)
                ? PlaceholderTitle.NewChat
                : PlaceholderTitle.None;

            return new AgentChatSessionViewModel(
                sessionId,
                placeholderTitle == PlaceholderTitle.None ? title : PlaceholderTitleText(placeholderTitle),
                PlaceholderSummaryText(PlaceholderSummary.Saved),
                relativeTimeText)
            {
                NeedsHistoryLoad = true,
                _persistedMessageCount = savedItemCount,
                _modelId = modelId,
                HasCustomTitle = hasCustomTitle,
                _placeholderTitle = placeholderTitle,
                _placeholderSummary = PlaceholderSummary.Saved,
                _lastActivityAt = updatedAt
            };
        }

        private static AgentChatSessionViewModel CreatePlaceholder(
            PlaceholderTitle title,
            PlaceholderSummary summary,
            DateTimeOffset now)
        {
            return new AgentChatSessionViewModel(
                Guid.NewGuid().ToString("N"),
                PlaceholderTitleText(title),
                PlaceholderSummaryText(summary),
                FormatRelativeTime(now, now))
            {
                _placeholderTitle = title,
                _placeholderSummary = summary,
                _lastActivityAt = now
            };
        }

        private static string PlaceholderTitleText(PlaceholderTitle title)
        {
            return title switch
            {
                PlaceholderTitle.WorkspaceChat => Loc.Get("Workbench.Thread.WorkspaceChat"),
                PlaceholderTitle.NewChat => Loc.Get("Workbench.Thread.NewChat"),
                _ => string.Empty
            };
        }

        private static string PlaceholderSummaryText(PlaceholderSummary summary)
        {
            return summary switch
            {
                PlaceholderSummary.WorkspaceReady => Loc.Get("Workbench.Thread.WorkspaceChatSummary"),
                PlaceholderSummary.NoMessagesYet => Loc.Get("Workbench.Thread.NewChatSummary"),
                PlaceholderSummary.Saved => Loc.Get("Workbench.Thread.SavedSummary"),
                _ => string.Empty
            };
        }
    }
}

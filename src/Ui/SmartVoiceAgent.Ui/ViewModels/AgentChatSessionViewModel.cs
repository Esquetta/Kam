using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// One chat thread in the sidebar and its timeline.
    /// </summary>
    public sealed class AgentChatSessionViewModel : ReactiveObject
    {
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

        public string Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        /// <summary>
        /// Gets or sets whether the user named this thread, so its first message no longer sets the title.
        /// </summary>
        public bool HasCustomTitle { get; set; }

        public string Summary
        {
            get => _summary;
            set => this.RaiseAndSetIfChanged(ref _summary, value);
        }

        public string RelativeTimeText
        {
            get => _relativeTimeText;
            set => this.RaiseAndSetIfChanged(ref _relativeTimeText, value);
        }

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

        public string AgentName => "Kam Agent";

        public string StatusText => IsRunning ? "Working" : IsSelected ? "Active" : "Ready";

        public string MessageCountText
        {
            get
            {
                var count = NeedsHistoryLoad ? _persistedMessageCount : Messages.Count;
                return count == 0
                    ? "No messages"
                    : count == 1
                        ? "1 message"
                        : $"{count} messages";
            }
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
        /// Creates a thread saved by an earlier run; its history loads when it is opened.
        /// </summary>
        public static AgentChatSessionViewModel CreateSaved(
            string sessionId,
            string title,
            string relativeTimeText,
            int savedItemCount,
            string? modelId = null,
            bool hasCustomTitle = false)
        {
            return new AgentChatSessionViewModel(sessionId, title, "Saved thread", relativeTimeText)
            {
                NeedsHistoryLoad = true,
                _persistedMessageCount = savedItemCount,
                _modelId = modelId,
                HasCustomTitle = hasCustomTitle
            };
        }
    }
}

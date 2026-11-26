using ReactiveUI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// One tool call waiting for the user, as the Needs you page lists it.
    /// </summary>
    /// <param name="Session">The task the call belongs to.</param>
    /// <param name="Step">The waiting tool step; the approval commands take it as their parameter.</param>
    public sealed record PendingApprovalItem(AgentChatSessionViewModel Session, AgentChatMessageViewModel Step);

    public partial class MainWindowViewModel
    {
        private ObservableCollection<AgentChatSessionViewModel>? _watchedSessions;

        /// <summary>
        /// Gets the tool calls waiting for approval across all tasks, oldest task first.
        /// </summary>
        public ObservableCollection<PendingApprovalItem> PendingApprovals { get; } = new();

        /// <summary>Gets how many tool calls wait for approval across all tasks.</summary>
        public int PendingApprovalCount => PendingApprovals.Count;

        /// <summary>Gets whether any tool call waits for approval.</summary>
        public bool HasPendingApprovals => PendingApprovals.Count > 0;

        /// <summary>Gets how many tasks have a turn running.</summary>
        public int RunningTaskCount => AgentChatSessions.Count(session => session.IsRunning);

        /// <summary>Gets whether any task has a turn running.</summary>
        public bool HasRunningTasks => RunningTaskCount > 0;

        /// <summary>Gets the tasks with a tool call waiting for approval: the task list's first group.</summary>
        public ObservableCollection<AgentChatSessionViewModel> WaitingTasks { get; } = new();

        /// <summary>Gets the tasks with a turn running and nothing waiting.</summary>
        public ObservableCollection<AgentChatSessionViewModel> RunningTasks { get; } = new();

        /// <summary>Gets the other tasks, in the task list's order.</summary>
        public ObservableCollection<AgentChatSessionViewModel> RecentTasks { get; } = new();

        /// <summary>Gets whether the task list shows its "Needs you" group.</summary>
        public bool HasWaitingTasks => WaitingTasks.Count > 0;

        /// <summary>Gets whether the task list shows its "Running" group.</summary>
        public bool HasRunningTaskGroup => RunningTasks.Count > 0;

        /// <summary>Gets whether the task list shows its "Recent" group.</summary>
        public bool HasRecentTasks => RecentTasks.Count > 0;

        /// <summary>Gets whether the Needs you page is shown.</summary>
        public bool IsInboxVisible => ActiveView == NavView.Inbox;

        /// <summary>Opens the Needs you page.</summary>
        public ICommand NavigateToInboxCommand { get; private set; } = null!;

        /// <summary>Opens a task on the Tasks page.</summary>
        public ICommand OpenTaskCommand { get; private set; } = null!;

        /// <summary>
        /// Creates the commands and starts following the task list. Called once from the constructor.
        /// </summary>
        private void InitializeInbox()
        {
            NavigateToInboxCommand = ReactiveCommand.Create(() => NavigateTo(NavView.Inbox));
            OpenTaskCommand = ReactiveCommand.Create<AgentChatSessionViewModel?>(session =>
            {
                if (session is null)
                {
                    return;
                }

                NavigateTo(NavView.Coordinator);
                SelectAgentChat(session);
            });

            WatchSessions(AgentChatSessions);

            // Plain PropertyChanged, not WhenAnyValue: view model tests build this without ReactiveUI's app builder.
            PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AgentChatSessions))
                {
                    WatchSessions(AgentChatSessions);
                }
            };
        }

        private void WatchSessions(ObservableCollection<AgentChatSessionViewModel> sessions)
        {
            if (ReferenceEquals(_watchedSessions, sessions))
            {
                return;
            }

            if (_watchedSessions is not null)
            {
                _watchedSessions.CollectionChanged -= OnWatchedSessionsChanged;
                foreach (var session in _watchedSessions)
                {
                    session.PropertyChanged -= OnWatchedSessionPropertyChanged;
                }
            }

            _watchedSessions = sessions;
            sessions.CollectionChanged += OnWatchedSessionsChanged;
            foreach (var session in sessions)
            {
                session.PropertyChanged += OnWatchedSessionPropertyChanged;
            }

            RefreshTaskCounts();
        }

        private void OnWatchedSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (AgentChatSessionViewModel session in e.OldItems)
                {
                    session.PropertyChanged -= OnWatchedSessionPropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (AgentChatSessionViewModel session in e.NewItems)
                {
                    session.PropertyChanged += OnWatchedSessionPropertyChanged;
                }
            }

            RefreshTaskCounts();
        }

        private void OnWatchedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // A turn runs its tool calls one after another, so a task has at most one waiting call
            // and HasPendingApproval changes whenever the waiting call does.
            if (e.PropertyName is nameof(AgentChatSessionViewModel.HasPendingApproval)
                or nameof(AgentChatSessionViewModel.IsRunning))
            {
                RefreshTaskCounts();
            }
        }

        private void RefreshTaskCounts()
        {
            var waiting = AgentChatSessions
                .SelectMany(session => session.ToolSteps
                    .Where(step => step.IsAwaitingApproval)
                    .Select(step => new PendingApprovalItem(session, step)))
                .ToList();

            if (!waiting.SequenceEqual(PendingApprovals))
            {
                PendingApprovals.Clear();
                foreach (var item in waiting)
                {
                    PendingApprovals.Add(item);
                }

                this.RaisePropertyChanged(nameof(PendingApprovalCount));
                this.RaisePropertyChanged(nameof(HasPendingApprovals));
            }

            this.RaisePropertyChanged(nameof(RunningTaskCount));
            this.RaisePropertyChanged(nameof(HasRunningTasks));
            RefreshTaskGroups();
        }

        /// <summary>
        /// Sorts the tasks into the list's groups: waiting for the user, running, and the rest.
        /// Each group keeps the task list's order.
        /// </summary>
        private void RefreshTaskGroups()
        {
            SyncGroup(WaitingTasks, AgentChatSessions.Where(session => session.HasPendingApproval));
            SyncGroup(RunningTasks, AgentChatSessions.Where(session => session.IsRunning && !session.HasPendingApproval));
            SyncGroup(RecentTasks, AgentChatSessions.Where(session => !session.IsRunning && !session.HasPendingApproval));
            this.RaisePropertyChanged(nameof(HasWaitingTasks));
            this.RaisePropertyChanged(nameof(HasRunningTaskGroup));
            this.RaisePropertyChanged(nameof(HasRecentTasks));
        }

        private static void SyncGroup(
            ObservableCollection<AgentChatSessionViewModel> group,
            IEnumerable<AgentChatSessionViewModel> members)
        {
            var wanted = members.ToList();
            if (wanted.SequenceEqual(group))
            {
                return;
            }

            group.Clear();
            foreach (var session in wanted)
            {
                group.Add(session);
            }
        }
    }
}

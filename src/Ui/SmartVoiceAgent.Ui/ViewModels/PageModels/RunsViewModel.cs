using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// Which kind of run the Runs page lists.
    /// </summary>
    public enum RunsFilter
    {
        /// <summary>Tasks and role-agent runs.</summary>
        All,

        /// <summary>Tasks (saved threads).</summary>
        Tasks,

        /// <summary>Role-agent runs.</summary>
        AgentRuns
    }

    /// <summary>
    /// One tool call a role agent made during a run.
    /// </summary>
    /// <param name="Skill">The skill the agent called.</param>
    /// <param name="Summary">What the call returned, in one line.</param>
    /// <param name="Success">Whether the call succeeded.</param>
    public sealed record RunObservationItem(string Skill, string Summary, bool Success);

    /// <summary>
    /// One row of the Runs table: a task or a role-agent run.
    /// </summary>
    public sealed class RunRowViewModel : ReactiveObject
    {
        private bool _isExpanded;

        /// <summary>Gets the session id for a task, or the run id for an agent run.</summary>
        public required string Id { get; init; }

        /// <summary>Gets whether the row is a task rather than an agent run.</summary>
        public required bool IsTask { get; init; }

        /// <summary>Gets the kind chip text.</summary>
        public required string KindText { get; init; }

        /// <summary>Gets the task title or agent name.</summary>
        public required string Name { get; init; }

        /// <summary>Gets the model name, or the Settings default text.</summary>
        public required string ModelText { get; init; }

        /// <summary>Gets the trimmed workspace folder (tasks) or role (agent runs).</summary>
        public required string ContextText { get; init; }

        /// <summary>Gets the full workspace path, shown as a tooltip.</summary>
        public string? ContextTooltip { get; init; }

        /// <summary>Gets the message count (tasks) or tool-call count (agent runs).</summary>
        public required int Count { get; init; }

        /// <summary>Gets the tooltip that says what <see cref="Count"/> counts.</summary>
        public required string CountTooltip { get; init; }

        /// <summary>Gets the status key: running, needs-you, done, failed or cancelled.</summary>
        public required string StatusKey { get; init; }

        /// <summary>Gets the status chip text.</summary>
        public required string StatusText { get; init; }

        /// <summary>Gets when the run last changed (tasks) or started (agent runs).</summary>
        public required DateTimeOffset When { get; init; }

        /// <summary>Gets the relative time shown in the table.</summary>
        public required string WhenText { get; init; }

        /// <summary>Gets the absolute time shown as a tooltip.</summary>
        public required string WhenTooltip { get; init; }

        /// <summary>Gets the task text of an agent run.</summary>
        public string TaskText { get; init; } = string.Empty;

        /// <summary>Gets the tool calls of an agent run.</summary>
        public IReadOnlyList<RunObservationItem> Observations { get; init; } = [];

        /// <summary>Gets the agent's response, or an empty string.</summary>
        public string ResponseText { get; init; } = string.Empty;

        /// <summary>Gets the failure or cancellation reason, or an empty string.</summary>
        public string ErrorText { get; init; } = string.Empty;

        /// <summary>Gets whether the status is running.</summary>
        public bool IsRunning => StatusKey == "running";

        /// <summary>Gets whether the task waits for an approval.</summary>
        public bool IsNeedsYou => StatusKey == "needs-you";

        /// <summary>Gets whether the run finished.</summary>
        public bool IsDone => StatusKey == "done";

        /// <summary>Gets whether the run failed.</summary>
        public bool IsFailed => StatusKey == "failed";

        /// <summary>Gets whether the run was cancelled.</summary>
        public bool IsCancelled => StatusKey == "cancelled";

        /// <summary>Gets whether the row has a detail to expand.</summary>
        public bool CanExpand => !IsTask;

        /// <summary>Gets whether the agent run has tool calls.</summary>
        public bool HasObservations => Observations.Count > 0;

        /// <summary>Gets whether the agent run has a response.</summary>
        public bool HasResponse => ResponseText.Length > 0;

        /// <summary>Gets whether the agent run has an error or cancellation reason.</summary>
        public bool HasError => ErrorText.Length > 0;

        /// <summary>Gets or sets whether the inline detail is open.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
        }
    }

    /// <summary>
    /// The Runs page: a history of tasks and role-agent runs from the stores, with counts for today and the
    /// failures of the last week. Kam records no cost or tokens per run, so none are shown.
    /// </summary>
    public sealed class RunsViewModel : ViewModelBase, IDisposable
    {
        private readonly IAgentSessionStore? _sessions;
        private readonly IRuntimeAgentRunStore? _agentRuns;
        private readonly Action<string> _openTask;
        private readonly Func<string, string?>? _liveStatus;
        private IReadOnlyList<AgentSessionSummary> _sessionData = [];
        private IReadOnlyList<RuntimeAgentRun> _runData = [];
        private int _loadVersion;
        private RunsFilter _filter;
        private string _searchText = string.Empty;
        private int _tasksToday;
        private int _agentRunsToday;
        private int _failedAgentRuns;
        private bool _hasLoaded;

        /// <summary>
        /// Creates the page and starts loading the history in the background.
        /// </summary>
        /// <param name="sessions">The saved tasks.</param>
        /// <param name="agentRuns">The role-agent runs.</param>
        /// <param name="openTask">Opens a task by session id.</param>
        /// <param name="liveStatus">Returns "running", "needs-you" or null for a session id that is open in a chat.</param>
        public RunsViewModel(
            IAgentSessionStore? sessions,
            IRuntimeAgentRunStore? agentRuns,
            Action<string> openTask,
            Func<string, string?>? liveStatus = null)
        {
            ArgumentNullException.ThrowIfNull(openTask);

            Title = Loc.Get("Runs.Title");
            _sessions = sessions;
            _agentRuns = agentRuns;
            _openTask = openTask;
            _liveStatus = liveStatus;

            ActivateRowCommand = ReactiveCommand.Create<RunRowViewModel>(ActivateRow);
            SetFilterCommand = ReactiveCommand.Create<string>(value =>
            {
                if (Enum.TryParse<RunsFilter>(value, out var filter))
                {
                    Filter = filter;
                }
            });
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            _ = LoadAsync();
        }

        /// <summary>Gets the rows the filter and search show, newest first.</summary>
        public ObservableCollection<RunRowViewModel> Rows { get; } = new();

        /// <summary>Gets the command a row click runs: opens a task or toggles an agent run's detail.</summary>
        public ReactiveCommand<RunRowViewModel, Unit> ActivateRowCommand { get; }

        /// <summary>Gets the command that picks the filter by its <see cref="RunsFilter"/> name.</summary>
        public ReactiveCommand<string, Unit> SetFilterCommand { get; }

        /// <summary>Gets the command that reloads the history.</summary>
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        /// <summary>Gets the number of tasks last active today.</summary>
        public int TasksToday
        {
            get => _tasksToday;
            private set => this.RaiseAndSetIfChanged(ref _tasksToday, value);
        }

        /// <summary>Gets the number of role-agent runs started today.</summary>
        public int AgentRunsToday
        {
            get => _agentRunsToday;
            private set => this.RaiseAndSetIfChanged(ref _agentRunsToday, value);
        }

        /// <summary>Gets the number of role-agent runs that failed in the last 7 days.</summary>
        public int FailedAgentRuns
        {
            get => _failedAgentRuns;
            private set => this.RaiseAndSetIfChanged(ref _failedAgentRuns, value);
        }

        /// <summary>Gets or sets which kind of run is listed.</summary>
        public RunsFilter Filter
        {
            get => _filter;
            set
            {
                if (_filter == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _filter, value);
                this.RaisePropertyChanged(nameof(IsFilterAll));
                this.RaisePropertyChanged(nameof(IsFilterTasks));
                this.RaisePropertyChanged(nameof(IsFilterAgentRuns));
                ApplyFilter();
            }
        }

        /// <summary>Gets whether every kind is listed.</summary>
        public bool IsFilterAll => _filter == RunsFilter.All;

        /// <summary>Gets whether only tasks are listed.</summary>
        public bool IsFilterTasks => _filter == RunsFilter.Tasks;

        /// <summary>Gets whether only agent runs are listed.</summary>
        public bool IsFilterAgentRuns => _filter == RunsFilter.AgentRuns;

        /// <summary>Gets or sets the search text; rows match on name, model, workspace or role and status.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                var text = value ?? string.Empty;
                if (_searchText == text)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _searchText, text);
                ApplyFilter();
            }
        }

        /// <summary>Gets whether the history is loaded and nothing matches (or nothing exists).</summary>
        public bool IsEmpty => _hasLoaded && Rows.Count == 0;

        /// <summary>Gets whether the stores hold nothing at all, as opposed to the filter hiding everything.</summary>
        public bool HasNoHistory => _sessionData.Count == 0 && _runData.Count == 0;

        /// <summary>Gets the empty-state title.</summary>
        public string EmptyTitle => Loc.Get(HasNoHistory ? "Runs.Empty.Title" : "Runs.NoMatch.Title");

        /// <summary>Gets the empty-state text.</summary>
        public string EmptyBody => Loc.Get(HasNoHistory ? "Runs.Empty.Body" : "Runs.NoMatch.Body");

        /// <inheritdoc />
        public override void OnNavigatedTo() => _ = LoadAsync();

        /// <summary>
        /// Reads both stores off the UI thread and rebuilds the rows. The newest call wins when calls overlap.
        /// </summary>
        public async Task LoadAsync()
        {
            var version = ++_loadVersion;
            IReadOnlyList<AgentSessionSummary> sessions = [];
            IReadOnlyList<RuntimeAgentRun> runs = [];

            try
            {
                if (_sessions is not null)
                {
                    sessions = await _sessions.ListAsync().ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                // A store that cannot be read leaves its rows out; the other store still shows.
            }

            try
            {
                if (_agentRuns is not null)
                {
                    var store = _agentRuns;
                    runs = await Task.Run(() => store.List(200)).ConfigureAwait(true);
                }
            }
            catch (Exception)
            {
                // See above.
            }

            if (version != _loadVersion)
            {
                return;
            }

            _sessionData = sessions;
            _runData = runs;
            _hasLoaded = true;
            Rebuild();
        }

        /// <summary>
        /// Opens a task, or opens and closes an agent run's detail.
        /// </summary>
        /// <param name="row">The clicked row.</param>
        public void ActivateRow(RunRowViewModel row)
        {
            ArgumentNullException.ThrowIfNull(row);

            if (row.IsTask)
            {
                _openTask(row.Id);
            }
            else
            {
                row.IsExpanded = !row.IsExpanded;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = Loc.Get("Runs.Title");
            if (_hasLoaded)
            {
                Rebuild();
            }
        }

        private readonly List<RunRowViewModel> _allRows = [];

        private void Rebuild()
        {
            var now = DateTimeOffset.Now;
            var today = now.ToLocalTime().Date;
            var weekAgo = now.AddDays(-7);

            var expanded = _allRows.Where(row => row.IsExpanded).Select(row => row.Id).ToHashSet();
            _allRows.Clear();
            _allRows.AddRange(_sessionData.Select(session => CreateTaskRow(session, now)));
            _allRows.AddRange(_runData.Select(run => CreateAgentRow(run, now)));
            _allRows.Sort((left, right) => right.When.CompareTo(left.When));
            foreach (var row in _allRows.Where(row => expanded.Contains(row.Id)))
            {
                row.IsExpanded = true;
            }

            TasksToday = _sessionData.Count(session => session.UpdatedAt.ToLocalTime().Date == today);
            AgentRunsToday = _runData.Count(run => run.StartedAt.ToLocalTime().Date == today);
            FailedAgentRuns = _runData.Count(run => run.Status == RuntimeAgentRunStatus.Failed && run.StartedAt >= weekAgo);

            this.RaisePropertyChanged(nameof(HasNoHistory));
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var query = _searchText.Trim();
            var visible = _allRows.Where(row =>
                (_filter == RunsFilter.All
                    || (_filter == RunsFilter.Tasks && row.IsTask)
                    || (_filter == RunsFilter.AgentRuns && !row.IsTask))
                && (query.Length == 0 || Matches(row, query))).ToList();

            Rows.Clear();
            foreach (var row in visible)
            {
                Rows.Add(row);
            }

            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(EmptyTitle));
            this.RaisePropertyChanged(nameof(EmptyBody));
        }

        private static bool Matches(RunRowViewModel row, string query) =>
            new[] { row.Name, row.ModelText, row.ContextText, row.StatusText, row.KindText }
                .Any(text => text.Contains(query, StringComparison.CurrentCultureIgnoreCase));

        private RunRowViewModel CreateTaskRow(AgentSessionSummary session, DateTimeOffset now)
        {
            var status = _liveStatus?.Invoke(session.Id) switch
            {
                "running" => "running",
                "needs-you" => "needs-you",
                _ => "done"
            };

            return new RunRowViewModel
            {
                Id = session.Id,
                IsTask = true,
                KindText = Loc.Get("Runs.Kind.Task"),
                Name = string.IsNullOrWhiteSpace(session.Title) ? Loc.Get("Runs.Untitled") : session.Title,
                ModelText = ModelText(session.ModelId),
                ContextText = FolderName(session.WorkspaceRoot),
                ContextTooltip = session.WorkspaceRoot,
                Count = session.MessageCount,
                CountTooltip = Loc.Format("Runs.Count.Messages", session.MessageCount),
                StatusKey = status,
                StatusText = StatusLabel(status),
                When = session.UpdatedAt,
                WhenText = AgentChatSessionViewModel.FormatRelativeTime(session.UpdatedAt, now),
                WhenTooltip = Absolute(session.UpdatedAt)
            };
        }

        private RunRowViewModel CreateAgentRow(RuntimeAgentRun run, DateTimeOffset now)
        {
            var status = run.Status switch
            {
                RuntimeAgentRunStatus.Running => "running",
                RuntimeAgentRunStatus.Failed => "failed",
                RuntimeAgentRunStatus.Canceled => "cancelled",
                _ => "done"
            };
            var observations = run.ToolObservations ?? [];

            return new RunRowViewModel
            {
                Id = run.RunId,
                IsTask = false,
                KindText = Loc.Get("Runs.Kind.Agent"),
                Name = string.IsNullOrWhiteSpace(run.AgentName) ? run.Role : run.AgentName,
                ModelText = ModelText(run.ModelId),
                ContextText = run.Role,
                Count = observations.Count,
                CountTooltip = Loc.Format("Runs.Count.ToolCalls", observations.Count),
                StatusKey = status,
                StatusText = StatusLabel(status),
                When = run.StartedAt,
                WhenText = AgentChatSessionViewModel.FormatRelativeTime(run.StartedAt, now),
                WhenTooltip = Absolute(run.StartedAt),
                TaskText = run.Task,
                Observations = observations.Select(item => new RunObservationItem(item.SkillId, item.Summary, item.Success)).ToList(),
                ResponseText = run.Response ?? string.Empty,
                ErrorText = run.ErrorMessage ?? string.Empty
            };
        }

        private static string ModelText(string? modelId) =>
            string.IsNullOrWhiteSpace(modelId) ? Loc.Get("Runs.Model.Default") : modelId;

        private static string StatusLabel(string status) => status switch
        {
            "running" => Loc.Get("Runs.Status.Running"),
            "needs-you" => Loc.Get("Runs.Status.NeedsYou"),
            "failed" => Loc.Get("Runs.Status.Failed"),
            "cancelled" => Loc.Get("Runs.Status.Cancelled"),
            _ => Loc.Get("Runs.Status.Done")
        };

        private static string FolderName(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "—";
            }

            var trimmed = path.TrimEnd('\\', '/');
            var index = trimmed.LastIndexOfAny(['\\', '/']);
            var name = index >= 0 ? trimmed[(index + 1)..] : trimmed;
            return name.Length > 0 ? name : path;
        }

        private static string Absolute(DateTimeOffset value) =>
            value.ToLocalTime().ToString("g", LocalizationService.Instance.Culture);
    }
}

using Avalonia;
using Avalonia.Threading;
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
    /// A task with subagent runs, as the Graphs page lists it.
    /// </summary>
    public sealed class GraphTaskItem : ReactiveObject
    {
        private bool _isSelected;

        /// <summary>Gets the task's session id.</summary>
        public required string SessionId { get; init; }

        /// <summary>Gets the task title.</summary>
        public required string Title { get; init; }

        /// <summary>Gets the line under the title, such as "3 subagents · 5m".</summary>
        public required string MetaText { get; init; }

        /// <summary>Gets the status key: running, needs-you, done or failed.</summary>
        public required string StatusKey { get; init; }

        /// <summary>Gets whether a subagent of the task is running.</summary>
        public bool IsRunning => StatusKey == "running";

        /// <summary>Gets whether a subagent of the task waits for approval.</summary>
        public bool IsNeedsYou => StatusKey == "needs-you";

        /// <summary>Gets whether a subagent of the task failed.</summary>
        public bool IsFailed => StatusKey == "failed";

        /// <summary>Gets whether every subagent of the task finished.</summary>
        public bool IsDone => StatusKey == "done";

        /// <summary>Gets or sets whether the task's graph is shown.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
    }

    /// <summary>
    /// A node drawn on the graph canvas. Nodes are updated in place while their run changes, so the canvas does not flicker.
    /// </summary>
    public sealed class GraphNodeViewModel : ReactiveObject
    {
        private double _x;
        private double _y;
        private string _title = string.Empty;
        private string _subtitle = string.Empty;
        private string _statusKey = "done";
        private string _statusText = string.Empty;
        private string _metaText = string.Empty;
        private string _currentText = string.Empty;
        private bool _isSelected;

        /// <summary>Creates a node.</summary>
        /// <param name="id">"root", the run id or "join:{n}".</param>
        /// <param name="kind">What it stands for.</param>
        public GraphNodeViewModel(string id, GraphNodeKind kind)
        {
            Id = id;
            Kind = kind;
        }

        /// <summary>Gets the node id.</summary>
        public string Id { get; }

        /// <summary>Gets what the node stands for.</summary>
        public GraphNodeKind Kind { get; }

        /// <summary>Gets whether it is the main agent node.</summary>
        public bool IsRoot => Kind == GraphNodeKind.Root;

        /// <summary>Gets whether it is a subagent node.</summary>
        public bool IsAgent => Kind == GraphNodeKind.Agent;

        /// <summary>Gets whether it is a join node.</summary>
        public bool IsJoin => Kind == GraphNodeKind.Join;

        /// <summary>Gets the left edge on the canvas.</summary>
        public double X { get => _x; private set => this.RaiseAndSetIfChanged(ref _x, value); }

        /// <summary>Gets the top edge on the canvas.</summary>
        public double Y { get => _y; private set => this.RaiseAndSetIfChanged(ref _y, value); }

        /// <summary>Gets the width.</summary>
        public double Width { get; private set; }

        /// <summary>Gets the height.</summary>
        public double Height { get; private set; }

        /// <summary>Gets the left edge the layout gives the node, before it was dragged.</summary>
        public double LayoutX { get; private set; }

        /// <summary>Gets the top edge the layout gives the node, before it was dragged.</summary>
        public double LayoutY { get; private set; }

        /// <summary>Gets where the node is drawn.</summary>
        public Rect Bounds => new(X, Y, Width, Height);

        /// <summary>Gets the first line: "Kam" or the subagent type.</summary>
        public string Title { get => _title; private set => this.RaiseAndSetIfChanged(ref _title, value); }

        /// <summary>Gets the second line: the task title or the subagent's job.</summary>
        public string Subtitle { get => _subtitle; private set => this.RaiseAndSetIfChanged(ref _subtitle, value); }

        /// <summary>Gets the status key: running, needs-you, done, failed or cancelled.</summary>
        public string StatusKey
        {
            get => _statusKey;
            private set
            {
                if (_statusKey == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _statusKey, value);
                this.RaisePropertyChanged(nameof(IsRunning));
                this.RaisePropertyChanged(nameof(IsNeedsYou));
                this.RaisePropertyChanged(nameof(IsDone));
                this.RaisePropertyChanged(nameof(IsFailed));
                this.RaisePropertyChanged(nameof(IsCancelled));
            }
        }

        /// <summary>Gets the status pill text.</summary>
        public string StatusText { get => _statusText; private set => this.RaiseAndSetIfChanged(ref _statusText, value); }

        /// <summary>Gets the steps, time and tokens line.</summary>
        public string MetaText { get => _metaText; private set => this.RaiseAndSetIfChanged(ref _metaText, value); }

        /// <summary>Gets the tool the subagent runs now, or an empty string.</summary>
        public string CurrentText
        {
            get => _currentText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _currentText, value);
                this.RaisePropertyChanged(nameof(HasCurrent));
            }
        }

        /// <summary>Gets whether <see cref="CurrentText"/> has anything to show.</summary>
        public bool HasCurrent => CurrentText.Length > 0;

        /// <summary>Gets whether the node is running.</summary>
        public bool IsRunning => StatusKey == "running";

        /// <summary>Gets whether the node waits for approval.</summary>
        public bool IsNeedsYou => StatusKey == "needs-you";

        /// <summary>Gets whether the node finished.</summary>
        public bool IsDone => StatusKey == "done";

        /// <summary>Gets whether the node failed.</summary>
        public bool IsFailed => StatusKey == "failed";

        /// <summary>Gets whether the node was stopped.</summary>
        public bool IsCancelled => StatusKey == "cancelled";

        /// <summary>Gets or sets whether the node's detail is shown.</summary>
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        /// <summary>Moves the node to its layout place plus the user's offset and sets its text.</summary>
        internal void Apply(GraphNodePlacement placement, Vector offset, string title, string subtitle, string statusKey, string metaText, string currentText)
        {
            LayoutX = placement.Bounds.X;
            LayoutY = placement.Bounds.Y;
            Width = placement.Bounds.Width;
            Height = placement.Bounds.Height;
            MoveTo(new Point(LayoutX + offset.X, LayoutY + offset.Y));
            Title = title;
            Subtitle = subtitle;
            StatusKey = statusKey;
            StatusText = GraphsViewModel.StatusLabel(statusKey);
            MetaText = metaText;
            CurrentText = currentText;
        }

        /// <summary>Moves the node, keeping it inside the canvas.</summary>
        internal void MoveTo(Point position)
        {
            X = Math.Max(0, position.X);
            Y = Math.Max(0, position.Y);
        }
    }

    /// <summary>A tool call in the detail of a selected subagent.</summary>
    /// <param name="DisplayName">The tool's name.</param>
    /// <param name="Target">What the call acted on.</param>
    /// <param name="StatusKey">running, needs-you, done or failed.</param>
    /// <param name="DurationText">How long it took, or an empty string while it runs.</param>
    /// <param name="Summary">The first line of its result.</param>
    public sealed record GraphStepItem(string DisplayName, string Target, string StatusKey, string DurationText, string Summary)
    {
        /// <summary>Gets whether the call is running.</summary>
        public bool IsRunning => StatusKey == "running";

        /// <summary>Gets whether the call waits for approval.</summary>
        public bool IsNeedsYou => StatusKey == "needs-you";

        /// <summary>Gets whether the call finished.</summary>
        public bool IsDone => StatusKey == "done";

        /// <summary>Gets whether the call failed.</summary>
        public bool IsFailed => StatusKey == "failed";

        /// <summary>Gets whether there is a result line.</summary>
        public bool HasSummary => Summary.Length > 0;
    }

    /// <summary>
    /// The Graphs page: for each task that started subagents, a live fork-join graph of its main agent and
    /// subagents, and the selected subagent's brief, tool calls and report. Updates as the tracker reports changes.
    /// </summary>
    public sealed class GraphsViewModel : ViewModelBase, IDisposable
    {
        /// <summary>The smallest zoom.</summary>
        public const double MinZoom = 0.5;

        /// <summary>The largest zoom.</summary>
        public const double MaxZoom = 2;

        /// <summary>How much the zoom buttons change the zoom.</summary>
        public const double ZoomStep = 0.1;

        // Keeps the canvas just inside the viewport, so filling it never adds a scroll bar.
        private const double ViewportSlack = 2;

        private readonly ISubagentRunTracker? _tracker;
        private readonly IAgentSessionStore? _sessions;
        private readonly Action<string> _openTask;
        private readonly Func<string, string?>? _liveStatus;
        private readonly Action<Action> _post;
        private readonly Func<DateTimeOffset> _clock;
        private readonly Dictionary<string, string> _titles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, Vector>> _offsets = new(StringComparer.Ordinal);
        private IReadOnlyList<GraphEdge> _links = [];
        private IReadOnlyList<GraphEdge> _edges = [];
        private Size _contentSize;
        private Size _viewport;
        private double _zoom = 1;

        // Until the user zooms, a new graph shrinks to fit the viewport (never past 100%), so a narrow window shows
        // every node instead of cutting the graph off.
        private bool _autoFit = true;
        private double _canvasWidth;
        private double _canvasHeight;
        private string? _selectedSessionId;
        private string? _selectedRunId;
        private SubagentRun? _selectedRun;
        private IReadOnlyList<GraphStepItem> _selectedSteps = [];
        private bool _refreshQueued;
        private bool _ticking;
        private bool _isVisible;

        /// <summary>
        /// Creates the page.
        /// </summary>
        /// <param name="tracker">The subagent runs.</param>
        /// <param name="sessions">Task titles.</param>
        /// <param name="openTask">Opens a task by session id.</param>
        /// <param name="liveStatus">Returns "running", "needs-you" or null for an open task.</param>
        /// <param name="post">Runs an action on the UI thread; tests pass one that runs it at once.</param>
        /// <param name="clock">The current time; tests pass a fixed one.</param>
        public GraphsViewModel(
            ISubagentRunTracker? tracker,
            IAgentSessionStore? sessions,
            Action<string> openTask,
            Func<string, string?>? liveStatus = null,
            Action<Action>? post = null,
            Func<DateTimeOffset>? clock = null)
        {
            ArgumentNullException.ThrowIfNull(openTask);

            Title = Loc.Get("Graphs.Title");
            _tracker = tracker;
            _sessions = sessions;
            _openTask = openTask;
            _liveStatus = liveStatus;
            _post = post ?? (action => Dispatcher.UIThread.Post(action));
            _clock = clock ?? (() => DateTimeOffset.Now);

            SelectTaskCommand = ReactiveCommand.Create<GraphTaskItem>(item => SelectTask(item.SessionId));
            SelectNodeCommand = ReactiveCommand.Create<GraphNodeViewModel>(SelectNode);
            OpenTaskCommand = ReactiveCommand.Create(() =>
            {
                if (_selectedSessionId is { } sessionId)
                {
                    _openTask(sessionId);
                }
            });
            StopRunCommand = ReactiveCommand.Create(() =>
            {
                if (_selectedRunId is { } runId)
                {
                    _tracker?.Cancel(runId);
                }
            });
            ClearSelectionCommand = ReactiveCommand.Create(() => SelectNode(null));
            ZoomInCommand = ReactiveCommand.Create(() => SetZoom((Math.Round(Zoom / ZoomStep) + 1) * ZoomStep));
            ZoomOutCommand = ReactiveCommand.Create(() => SetZoom((Math.Round(Zoom / ZoomStep) - 1) * ZoomStep));
            FitCommand = ReactiveCommand.Create(Fit);
            ResetLayoutCommand = ReactiveCommand.Create(ResetLayout);

            if (_tracker is not null)
            {
                _tracker.Changed += OnRunChanged;
            }

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Refresh();
            _ = LoadTitlesAsync();
        }

        /// <summary>Gets the tasks that started subagents, newest first.</summary>
        public ObservableCollection<GraphTaskItem> Tasks { get; } = new();

        /// <summary>Gets the nodes of the selected task's graph.</summary>
        public ObservableCollection<GraphNodeViewModel> Nodes { get; } = new();

        /// <summary>Gets the edges of the selected task's graph, from the nodes' current places.</summary>
        public IReadOnlyList<GraphEdge> Edges
        {
            get => _edges;
            private set => this.RaiseAndSetIfChanged(ref _edges, value);
        }

        /// <summary>Gets the canvas width: the nodes plus padding, and at least the viewport.</summary>
        public double CanvasWidth { get => _canvasWidth; private set => this.RaiseAndSetIfChanged(ref _canvasWidth, value); }

        /// <summary>Gets the canvas height: the nodes plus padding, and at least the viewport.</summary>
        public double CanvasHeight { get => _canvasHeight; private set => this.RaiseAndSetIfChanged(ref _canvasHeight, value); }

        /// <summary>Gets the graph's zoom, from <see cref="MinZoom"/> to <see cref="MaxZoom"/>.</summary>
        public double Zoom
        {
            get => _zoom;
            private set
            {
                this.RaiseAndSetIfChanged(ref _zoom, value);
                this.RaisePropertyChanged(nameof(ZoomText));
            }
        }

        /// <summary>Gets the zoom as a percentage, such as "100%".</summary>
        public string ZoomText => Loc.Format("Graphs.Zoom.Percent", Math.Round(Zoom * 100));

        /// <summary>Gets the command that zooms in one step.</summary>
        public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }

        /// <summary>Gets the command that zooms out one step.</summary>
        public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }

        /// <summary>Gets the command that zooms so every node fits the viewport.</summary>
        public ReactiveCommand<Unit, Unit> FitCommand { get; }

        /// <summary>Gets the command that puts the selected task's dragged nodes back at their layout places.</summary>
        public ReactiveCommand<Unit, Unit> ResetLayoutCommand { get; }

        /// <summary>Gets the command that shows a task's graph.</summary>
        public ReactiveCommand<GraphTaskItem, Unit> SelectTaskCommand { get; }

        /// <summary>Gets the command that shows a subagent's detail; the main agent node clears it.</summary>
        public ReactiveCommand<GraphNodeViewModel, Unit> SelectNodeCommand { get; }

        /// <summary>Gets the command that opens the selected task's chat.</summary>
        public ReactiveCommand<Unit, Unit> OpenTaskCommand { get; }

        /// <summary>Gets the command that stops the selected subagent.</summary>
        public ReactiveCommand<Unit, Unit> StopRunCommand { get; }

        /// <summary>Gets the command that closes the subagent detail.</summary>
        public ReactiveCommand<Unit, Unit> ClearSelectionCommand { get; }

        /// <summary>Gets whether no task has started a subagent yet.</summary>
        public bool IsEmpty => Tasks.Count == 0;

        /// <summary>Gets whether there is a graph to show.</summary>
        public bool HasGraph => !IsEmpty;

        /// <summary>Gets the selected task's session id.</summary>
        public string? SelectedSessionId => _selectedSessionId;

        /// <summary>Gets the selected task's title.</summary>
        public string SelectedTaskTitle => _selectedSessionId is null ? string.Empty : TaskTitle(_selectedSessionId);

        /// <summary>Gets the selected task's summary line, such as "4 subagents · 1 running · 12.5k tokens".</summary>
        public string SelectedTaskSummary { get; private set; } = string.Empty;

        /// <summary>Gets the selected subagent run, or null.</summary>
        public SubagentRun? SelectedRun
        {
            get => _selectedRun;
            private set
            {
                this.RaiseAndSetIfChanged(ref _selectedRun, value);
                this.RaisePropertyChanged(nameof(HasSelectedRun));
                this.RaisePropertyChanged(nameof(HasNoSelectedRun));
                this.RaisePropertyChanged(nameof(SelectedRunType));
                this.RaisePropertyChanged(nameof(SelectedRunStatusKey));
                this.RaisePropertyChanged(nameof(SelectedRunStatusText));
                this.RaisePropertyChanged(nameof(SelectedRunIsRunning));
                this.RaisePropertyChanged(nameof(SelectedRunModel));
                this.RaisePropertyChanged(nameof(SelectedRunStarted));
                this.RaisePropertyChanged(nameof(SelectedRunDuration));
                this.RaisePropertyChanged(nameof(SelectedRunTokens));
                this.RaisePropertyChanged(nameof(SelectedRunReport));
                this.RaisePropertyChanged(nameof(HasSelectedRunReport));
                this.RaisePropertyChanged(nameof(SelectedRunError));
                this.RaisePropertyChanged(nameof(HasSelectedRunError));
            }
        }

        /// <summary>Gets whether a subagent is selected.</summary>
        public bool HasSelectedRun => SelectedRun is not null;

        /// <summary>Gets whether no subagent is selected.</summary>
        public bool HasNoSelectedRun => SelectedRun is null;

        /// <summary>Gets the selected subagent's type label.</summary>
        public string SelectedRunType => SelectedRun is null ? string.Empty : AgentChatMessageViewModel.SubagentTypeLabel(SelectedRun.AgentType);

        /// <summary>Gets the selected subagent's status key.</summary>
        public string SelectedRunStatusKey => SelectedRun is null ? string.Empty : AgentGraphLayout.StatusKey(SelectedRun);

        /// <summary>Gets the selected subagent's status text.</summary>
        public string SelectedRunStatusText => SelectedRun is null ? string.Empty : StatusLabel(SelectedRunStatusKey);

        /// <summary>Gets whether the selected subagent still runs and can be stopped.</summary>
        public bool SelectedRunIsRunning => SelectedRun?.Status == SubagentRunStatus.Running;

        /// <summary>Gets the selected subagent's model.</summary>
        public string SelectedRunModel => SelectedRun is null ? string.Empty
            : string.IsNullOrWhiteSpace(SelectedRun.ModelId) ? Loc.Get("Runs.Model.Default") : SelectedRun.ModelId;

        /// <summary>Gets when the selected subagent started.</summary>
        public string SelectedRunStarted => SelectedRun is null ? string.Empty
            : SelectedRun.StartedAt.ToLocalTime().ToString("g", LocalizationService.Instance.Culture);

        /// <summary>Gets how long the selected subagent ran, or has run so far.</summary>
        public string SelectedRunDuration => SelectedRun is null ? string.Empty : Duration(SelectedRun);

        /// <summary>Gets the selected subagent's tokens.</summary>
        public string SelectedRunTokens => SelectedRun is null ? string.Empty
            : MainWindowViewModel.FormatTokenCount(SelectedRun.InputTokens + SelectedRun.OutputTokens);

        /// <summary>Gets the selected subagent's tool calls.</summary>
        public IReadOnlyList<GraphStepItem> SelectedSteps
        {
            get => _selectedSteps;
            private set
            {
                this.RaiseAndSetIfChanged(ref _selectedSteps, value);
                this.RaisePropertyChanged(nameof(HasSelectedSteps));
            }
        }

        /// <summary>Gets whether the selected subagent made tool calls.</summary>
        public bool HasSelectedSteps => SelectedSteps.Count > 0;

        /// <summary>Gets the report the main agent received.</summary>
        public string SelectedRunReport => SelectedRun?.Result is { Length: > 0 } result && SelectedRun.Status == SubagentRunStatus.Completed ? result : string.Empty;

        /// <summary>Gets whether there is a report.</summary>
        public bool HasSelectedRunReport => SelectedRunReport.Length > 0;

        /// <summary>Gets why the selected subagent failed or stopped.</summary>
        public string SelectedRunError => SelectedRun?.Error ?? string.Empty;

        /// <summary>Gets whether there is an error.</summary>
        public bool HasSelectedRunError => SelectedRunError.Length > 0;

        /// <summary>
        /// Shows a task's graph and, when given, selects one of its subagents. The chat and Runs pages use it.
        /// </summary>
        /// <param name="sessionId">The task.</param>
        /// <param name="runId">The subagent run, or null.</param>
        public void Focus(string sessionId, string? runId)
        {
            _autoFit |= _selectedSessionId != sessionId;
            _selectedSessionId = sessionId;
            _selectedRunId = runId;
            Refresh();
        }

        /// <inheritdoc />
        public override void OnNavigatedTo()
        {
            _isVisible = true;
            Refresh();
            _ = LoadTitlesAsync();
        }

        /// <inheritdoc />
        public override void OnNavigatedFrom() => _isVisible = false;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_tracker is not null)
            {
                _tracker.Changed -= OnRunChanged;
            }

            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }

        /// <summary>Gets the status label for a status key.</summary>
        /// <param name="statusKey">running, needs-you, done, failed or cancelled.</param>
        public static string StatusLabel(string statusKey) => statusKey switch
        {
            "running" => Loc.Get("Runs.Status.Running"),
            "needs-you" => Loc.Get("Runs.Status.NeedsYou"),
            "failed" => Loc.Get("Runs.Status.Failed"),
            "cancelled" => Loc.Get("Runs.Status.Cancelled"),
            _ => Loc.Get("Runs.Status.Done")
        };

        /// <summary>
        /// Sets the zoom the user chose, kept between <see cref="MinZoom"/> and <see cref="MaxZoom"/>. From then on
        /// the graph keeps that zoom instead of fitting itself to the viewport.
        /// </summary>
        /// <param name="zoom">The zoom; 1 is 100%.</param>
        public void SetZoom(double zoom)
        {
            _autoFit = false;
            ApplyZoom(zoom);
        }

        /// <summary>Sets the size of the area that shows the canvas, in screen units.</summary>
        /// <param name="viewport">The viewport size.</param>
        public void SetViewport(Size viewport)
        {
            _viewport = viewport;
            FitOrUpdateCanvas();
        }

        /// <summary>Zooms so every node fits the viewport, within the zoom limits.</summary>
        public void Fit()
        {
            if (FitZoom() is { } zoom)
            {
                ApplyZoom(zoom);
            }
        }

        private double? FitZoom()
        {
            if (_contentSize.Width <= 0 || _contentSize.Height <= 0 || _viewport.Width <= 0 || _viewport.Height <= 0)
            {
                return null;
            }

            var zoom = Math.Min(
                (_viewport.Width - ViewportSlack) / _contentSize.Width,
                (_viewport.Height - ViewportSlack) / _contentSize.Height);
            return Math.Floor(zoom * 100) / 100;
        }

        private void ApplyZoom(double zoom)
        {
            Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            UpdateCanvasSize();
        }

        private void FitOrUpdateCanvas()
        {
            if (_autoFit && FitZoom() is { } zoom)
            {
                ApplyZoom(Math.Min(1, zoom));
                return;
            }

            UpdateCanvasSize();
        }

        /// <summary>
        /// Moves a node of the selected task and keeps its offset from the layout, so refreshes and new runs keep it there.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="position">The node's new top left corner on the canvas.</param>
        public void MoveNode(string nodeId, Point position)
        {
            var node = Nodes.FirstOrDefault(item => item.Id == nodeId);
            if (node is null || _selectedSessionId is null)
            {
                return;
            }

            // The zoom stays put while the user arranges nodes; a node dragged out does not shrink the graph.
            _autoFit = false;
            node.MoveTo(position);
            if (!_offsets.TryGetValue(_selectedSessionId, out var offsets))
            {
                offsets = new Dictionary<string, Vector>(StringComparer.Ordinal);
                _offsets[_selectedSessionId] = offsets;
            }

            offsets[nodeId] = new Vector(node.X - node.LayoutX, node.Y - node.LayoutY);
            UpdateGeometry();
        }

        /// <summary>Moves a node by a pointer movement measured on screen, which the zoom scales.</summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="origin">Where the node was when the drag started.</param>
        /// <param name="pointerDelta">How far the pointer moved since then, in screen units.</param>
        public void DragNode(string nodeId, Point origin, Vector pointerDelta) => MoveNode(nodeId, origin + (pointerDelta / Zoom));

        /// <summary>
        /// Gets the scroll offset that keeps the canvas point under the pointer in place when the zoom changes.
        /// </summary>
        /// <param name="offset">The scroll offset before.</param>
        /// <param name="pointer">The pointer, relative to the viewport.</param>
        /// <param name="oldZoom">The zoom before.</param>
        /// <param name="newZoom">The zoom after.</param>
        public static Vector ZoomedOffset(Vector offset, Point pointer, double oldZoom, double newZoom)
        {
            var anchor = (offset + pointer) / oldZoom;
            var result = (anchor * newZoom) - pointer;
            return new Vector(Math.Max(0, result.X), Math.Max(0, result.Y));
        }

        /// <summary>Re-reads the runs and rebuilds the task list, the graph and the detail.</summary>
        public void Refresh()
        {
            var runs = _tracker?.List(maxCount: 500) ?? [];
            var now = _clock();
            RebuildTasks(runs, now);

            var taskRuns = _selectedSessionId is null ? [] : runs.Where(run => run.ParentSessionId == _selectedSessionId).ToList();
            RebuildGraph(taskRuns, now);

            SelectedRun = _selectedRunId is null ? null : taskRuns.FirstOrDefault(run => run.RunId == _selectedRunId);
            if (SelectedRun is null)
            {
                _selectedRunId = null;
            }

            var steps = SelectedRun?.StepList.Select(step => StepItem(step, now)).ToList() ?? [];
            if (!steps.SequenceEqual(SelectedSteps))
            {
                SelectedSteps = steps;
            }
            foreach (var node in Nodes)
            {
                node.IsSelected = node.Id == _selectedRunId;
            }

            SelectedTaskSummary = Summary(taskRuns);
            this.RaisePropertyChanged(nameof(SelectedTaskSummary));
            this.RaisePropertyChanged(nameof(SelectedSessionId));
            this.RaisePropertyChanged(nameof(SelectedTaskTitle));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(HasGraph));

            if (runs.Any(run => run.Status == SubagentRunStatus.Running))
            {
                _ = TickAsync();
            }
        }

        private void SelectTask(string sessionId)
        {
            if (_selectedSessionId != sessionId)
            {
                _selectedSessionId = sessionId;
                _selectedRunId = null;
                _autoFit = true;
                Nodes.Clear();
            }

            Refresh();
        }

        private void ResetLayout()
        {
            if (_selectedSessionId is not null && _offsets.Remove(_selectedSessionId))
            {
                Refresh();
            }
        }

        private void SelectNode(GraphNodeViewModel? node)
        {
            _selectedRunId = node is { IsAgent: true } ? node.Id : null;
            Refresh();
        }

        private void RebuildTasks(IReadOnlyList<SubagentRun> runs, DateTimeOffset now)
        {
            var groups = runs
                .GroupBy(run => run.ParentSessionId, StringComparer.Ordinal)
                .Select(group => (SessionId: group.Key, Runs: group.ToList(), Latest: group.Max(run => run.StartedAt)))
                .OrderByDescending(group => group.Latest)
                .ToList();

            if (_selectedSessionId is null || groups.All(group => group.SessionId != _selectedSessionId))
            {
                _selectedSessionId = groups.FirstOrDefault().SessionId;
                _selectedRunId = null;
            }

            var items = groups.Select(group => new GraphTaskItem
                {
                    SessionId = group.SessionId,
                    Title = TaskTitle(group.SessionId),
                    MetaText = Loc.Format(
                        "Graphs.Task.Meta",
                        group.Runs.Count,
                        AgentChatSessionViewModel.FormatRelativeTime(group.Latest, now)),
                    StatusKey = TaskStatus(group.Runs),
                    IsSelected = group.SessionId == _selectedSessionId
                }).ToList();

            // Rebuilding the list only when it changed keeps the rows steady while runs tick.
            static string Key(GraphTaskItem item) => $"{item.SessionId}|{item.Title}|{item.MetaText}|{item.StatusKey}|{item.IsSelected}";
            if (items.Select(Key).SequenceEqual(Tasks.Select(Key)))
            {
                return;
            }

            Tasks.Clear();
            foreach (var item in items)
            {
                Tasks.Add(item);
            }
        }

        private void RebuildGraph(IReadOnlyList<SubagentRun> runs, DateTimeOffset now)
        {
            if (runs.Count == 0)
            {
                Nodes.Clear();
                _links = [];
                UpdateGeometry();
                return;
            }

            var graph = AgentGraphLayout.Build(runs, now);
            var ids = graph.Nodes.Select(node => node.Id).ToList();
            if (!Nodes.Select(node => node.Id).SequenceEqual(ids))
            {
                Nodes.Clear();
                foreach (var placement in graph.Nodes)
                {
                    Nodes.Add(new GraphNodeViewModel(placement.Id, placement.Kind));
                }
            }

            var rootStatus = RootStatus(runs);
            var offsets = _selectedSessionId is not null && _offsets.TryGetValue(_selectedSessionId, out var saved) ? saved : null;
            for (var index = 0; index < graph.Nodes.Count; index++)
            {
                var placement = graph.Nodes[index];
                var offset = offsets is not null && offsets.TryGetValue(placement.Id, out var moved) ? moved : default;
                switch (placement.Kind)
                {
                    case GraphNodeKind.Root:
                        Nodes[index].Apply(placement, offset, Loc.Get("Graphs.Node.Main"), SelectedTaskTitle, rootStatus, Loc.Format("Graphs.Node.MainMeta", runs.Count), string.Empty);
                        break;
                    case GraphNodeKind.Agent:
                        var run = placement.Run!;
                        var running = run.StepList.LastOrDefault(step => step.Status is SubagentStepStatus.Running or SubagentStepStatus.AwaitingApproval);
                        Nodes[index].Apply(
                            placement,
                            offset,
                            AgentChatMessageViewModel.SubagentTypeLabel(run.AgentType),
                            run.Description,
                            AgentGraphLayout.StatusKey(run),
                            Loc.Format("Graphs.Node.AgentMeta", run.StepList.Count, Duration(run, now), MainWindowViewModel.FormatTokenCount(run.InputTokens + run.OutputTokens)),
                            running?.DisplayName ?? string.Empty);
                        break;
                    default:
                        Nodes[index].Apply(placement, offset, string.Empty, string.Empty, "done", string.Empty, string.Empty);
                        break;
                }
            }

            _links = graph.Edges;
            UpdateGeometry();
        }

        private void UpdateGeometry()
        {
            var nodes = Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
            Edges = _links
                .Where(link => nodes.ContainsKey(link.FromId) && nodes.ContainsKey(link.ToId))
                .Select(link => link with
                {
                    From = AgentGraphLayout.RightMiddle(nodes[link.FromId].Bounds),
                    To = AgentGraphLayout.LeftMiddle(nodes[link.ToId].Bounds)
                })
                .ToList();

            _contentSize = Nodes.Count == 0
                ? default
                : new Size(
                    Nodes.Max(node => node.X + node.Width) + AgentGraphLayout.Padding,
                    Nodes.Max(node => node.Y + node.Height) + AgentGraphLayout.Padding);
            FitOrUpdateCanvas();
        }

        private void UpdateCanvasSize()
        {
            if (Nodes.Count == 0)
            {
                CanvasWidth = 0;
                CanvasHeight = 0;
                return;
            }

            // The canvas fills the viewport, so empty space anywhere can be dragged to pan.
            CanvasWidth = Math.Max(_contentSize.Width, (_viewport.Width - ViewportSlack) / Zoom);
            CanvasHeight = Math.Max(_contentSize.Height, (_viewport.Height - ViewportSlack) / Zoom);
        }

        private string RootStatus(IReadOnlyList<SubagentRun> runs)
        {
            var live = _selectedSessionId is null ? null : _liveStatus?.Invoke(_selectedSessionId);
            if (live is "needs-you" || runs.Any(run => run.IsWaitingForApproval))
            {
                return "needs-you";
            }

            return live is "running" || runs.Any(run => run.Status == SubagentRunStatus.Running) ? "running" : "done";
        }

        private static string TaskStatus(IReadOnlyList<SubagentRun> runs)
        {
            if (runs.Any(run => run.IsWaitingForApproval))
            {
                return "needs-you";
            }

            if (runs.Any(run => run.Status == SubagentRunStatus.Running))
            {
                return "running";
            }

            return runs.Any(run => run.Status == SubagentRunStatus.Failed) ? "failed" : "done";
        }

        private static string Summary(IReadOnlyList<SubagentRun> runs)
        {
            if (runs.Count == 0)
            {
                return string.Empty;
            }

            var running = runs.Count(run => run.Status == SubagentRunStatus.Running);
            var tokens = runs.Sum(run => run.InputTokens + run.OutputTokens);
            return Loc.Format("Graphs.Task.Summary", runs.Count, running, MainWindowViewModel.FormatTokenCount(tokens));
        }

        private string Duration(SubagentRun run) => Duration(run, _clock());

        private static string Duration(SubagentRun run, DateTimeOffset now) =>
            AgentTurnTraceViewModel.FormatDuration(((run.CompletedAt ?? now) - run.StartedAt).Duration());

        private static GraphStepItem StepItem(SubagentStep step, DateTimeOffset now)
        {
            var status = step.Status switch
            {
                SubagentStepStatus.Running => "running",
                SubagentStepStatus.AwaitingApproval => "needs-you",
                SubagentStepStatus.Succeeded => "done",
                _ => "failed"
            };
            var duration = step.CompletedAt is { } completed
                ? AgentTurnTraceViewModel.FormatDuration((completed - step.StartedAt).Duration())
                : string.Empty;

            return new GraphStepItem(step.DisplayName, ToolStepTarget(step), status, duration, step.Summary);
        }

        private static string ToolStepTarget(SubagentStep step) =>
            AgentChatMessageViewModel.ToolStep(step.CallId, step.ToolName, step.DisplayName, step.ArgumentsJson, step.Risk).ToolTarget;

        private string TaskTitle(string sessionId) =>
            _titles.TryGetValue(sessionId, out var title) && !string.IsNullOrWhiteSpace(title) ? title : Loc.Get("Runs.Untitled");

        private async Task LoadTitlesAsync()
        {
            if (_sessions is null)
            {
                return;
            }

            try
            {
                var sessions = await _sessions.ListAsync().ConfigureAwait(false);
                _post(() =>
                {
                    foreach (var session in sessions)
                    {
                        _titles[session.Id] = session.Title;
                    }

                    Refresh();
                });
            }
            catch (Exception)
            {
                // Without titles the tasks show as untitled.
            }
        }

        private void OnRunChanged(object? sender, SubagentRun run)
        {
            // Many changes arrive per second while subagents work; one refresh handles them all.
            lock (_titles)
            {
                if (_refreshQueued)
                {
                    return;
                }

                _refreshQueued = true;
            }

            _post(() =>
            {
                lock (_titles)
                {
                    _refreshQueued = false;
                }

                if (!_titles.ContainsKey(run.ParentSessionId))
                {
                    _ = LoadTitlesAsync();
                }

                Refresh();
            });
        }

        private async Task TickAsync()
        {
            // While a subagent runs and the page is on screen, its time moves every second.
            if (_ticking || !_isVisible)
            {
                return;
            }

            _ticking = true;
            try
            {
                await Task.Delay(1000).ConfigureAwait(false);
            }
            finally
            {
                _post(() =>
                {
                    _ticking = false;
                    if (_isVisible)
                    {
                        Refresh();
                    }
                });
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = Loc.Get("Graphs.Title");
            this.RaisePropertyChanged(nameof(ZoomText));
            Refresh();
        }
    }
}

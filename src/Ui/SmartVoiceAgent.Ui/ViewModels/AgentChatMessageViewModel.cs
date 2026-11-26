using ReactiveUI;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// What a chat timeline item shows.
    /// </summary>
    public enum AgentChatItemKind
    {
        /// <summary>A message from the user, the agent or the system.</summary>
        Message,

        /// <summary>A tool call the agent made, with its approval and result.</summary>
        ToolStep,

        /// <summary>Consecutive read-only tool calls of the main agent, shown as one summary line.</summary>
        ToolGroup
    }

    /// <summary>
    /// State of a tool step in the chat timeline.
    /// </summary>
    public enum AgentToolStepState
    {
        Running,
        AwaitingApproval,
        Succeeded,
        Failed,
        Declined
    }

    /// <summary>
    /// One item in a chat thread: a message, a tool call with its approval row and result, or a group of
    /// read-only tool calls. A group and a step that started a subagent hold their steps in <see cref="Children"/>.
    /// Message content can grow while the model streams. Labels such as the author, risk and state
    /// follow the interface language; the thread refreshes them through <see cref="RefreshLocalizedText"/>.
    /// </summary>
    public sealed class AgentChatMessageViewModel : ReactiveObject
    {
        /// <summary>The role id of messages the user wrote.</summary>
        public const string UserRole = "You";

        /// <summary>The role id of the agent's replies.</summary>
        public const string AgentRole = "Kam";

        /// <summary>The role id of notices such as errors and context summaries.</summary>
        public const string SystemRole = "System";

        /// <summary>The result summary the agent runtime reports for a call the user declined.</summary>
        private const string DeclinedSummary = "Declined";

        private const int StreamRefreshMilliseconds = 50;

        private readonly StringBuilder _pendingContent = new();
        private long _lastContentRefresh;
        private bool _contentRefreshScheduled;
        private string _content;
        private bool _isStreaming;
        private AgentToolStepState _toolState;
        private readonly long _toolStartedTimestamp;
        private long _approvalStartedTimestamp;
        private TimeSpan _approvalWait;
        private string _toolDurationText = string.Empty;
        private string _toolSummary = string.Empty;
        private string? _approvalRequestId;
        private string? _alwaysAllowRule;
        private bool _isCopied;
        private bool _isExpanded;
        private bool _askedForApproval;
        private string _subagentProgressText = string.Empty;

        /// <summary>
        /// Creates a message.
        /// </summary>
        /// <param name="role">The role id: <see cref="UserRole"/>, <see cref="AgentRole"/> or <see cref="SystemRole"/>.</param>
        /// <param name="content">The message text.</param>
        /// <param name="timeText">The time shown next to the message.</param>
        public AgentChatMessageViewModel(
            string role,
            string content,
            string timeText)
        {
            RoleId = role;
            _content = content;
            TimeText = timeText;
            Kind = AgentChatItemKind.Message;
        }

        private AgentChatMessageViewModel(
            string callId,
            string toolName,
            string toolDisplayName,
            string argumentsJson,
            ToolRisk risk,
            string timeText)
            : this(AgentRole, string.Empty, timeText)
        {
            Kind = AgentChatItemKind.ToolStep;
            _toolStartedTimestamp = Stopwatch.GetTimestamp();
            CallId = callId;
            ToolName = toolName;
            ToolDisplayName = risk == ToolRisk.Agent
                ? Loc.Format("Workbench.Subagent.StepTitle", SubagentTypeLabel(ReadArgument(argumentsJson, "subagent_type")))
                : toolDisplayName;
            ArgumentsPreview = FormatArguments(argumentsJson, full: risk is ToolRisk.Delegate or ToolRisk.Agent);
            ToolTarget = risk == ToolRisk.Agent
                ? Shorten(ReadArgument(argumentsJson, "description").ReplaceLineEndings(" ").Trim(), 120)
                : SummarizeTarget(argumentsJson);
            Risk = risk;
            _toolState = AgentToolStepState.Running;
            if (risk == ToolRisk.Agent)
            {
                WatchChildren();
            }
        }

        private AgentChatMessageViewModel()
            : this(AgentRole, string.Empty, string.Empty)
        {
            Kind = AgentChatItemKind.ToolGroup;
            _toolState = AgentToolStepState.Succeeded;
            WatchChildren();
        }

        /// <summary>Gets what the item shows: a message, a tool step or a group of read-only steps.</summary>
        public AgentChatItemKind Kind { get; }

        /// <summary>
        /// Gets the role id the item was created with, such as <see cref="UserRole"/>. It does not change with the language.
        /// </summary>
        public string RoleId { get; }

        /// <summary>
        /// Gets the author name shown above the message, in the interface language.
        /// </summary>
        public string Role => RoleId switch
        {
            UserRole => Loc.Get("Workbench.Role.You"),
            SystemRole => Loc.Get("Workbench.Role.System"),
            _ => RoleId
        };

        public string Content
        {
            get => _content;
            private set => this.RaiseAndSetIfChanged(ref _content, value);
        }

        public string TimeText { get; }

        /// <summary>
        /// Gets whether the message was written by the user, which renders it as an outgoing bubble.
        /// </summary>
        public bool IsUser => Kind == AgentChatItemKind.Message && RoleId.Equals(UserRole, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Gets whether the message came from the agent or the system.
        /// </summary>
        public bool IsAgent => Kind == AgentChatItemKind.Message && !IsUser;

        /// <summary>
        /// Gets whether the message came from Kam itself, such as an error or a compaction notice,
        /// so the thread can set it apart from the agent's replies.
        /// </summary>
        public bool IsSystem => Kind == AgentChatItemKind.Message && RoleId.Equals(SystemRole, StringComparison.OrdinalIgnoreCase);

        /// <summary>Gets whether the item is a tool call.</summary>
        public bool IsToolStep => Kind == AgentChatItemKind.ToolStep;

        /// <summary>Gets whether the item is a group of read-only tool calls.</summary>
        public bool IsToolGroup => Kind == AgentChatItemKind.ToolGroup;

        /// <summary>Gets whether the item is a tool step or a group of them; the thread spaces those lines tighter than messages.</summary>
        public bool IsToolItem => Kind != AgentChatItemKind.Message;

        /// <summary>
        /// Gets the steps this item holds: a group's read-only steps, or the steps of the subagent this step started.
        /// Empty for every other item.
        /// </summary>
        public ObservableCollection<AgentChatMessageViewModel> Children { get; } = new();

        /// <summary>Gets whether <see cref="Children"/> holds any steps.</summary>
        public bool HasChildren => Children.Count > 0;

        /// <summary>Gets whether one of <see cref="Children"/> waits for the user's approval.</summary>
        public bool HasChildAwaitingApproval => Children.Any(child => child.IsAwaitingApproval);

        /// <summary>
        /// Gets a group's line: the name of the step it runs now, or a summary such as "Explored 3 files · 2 searches".
        /// </summary>
        public string GroupTitle => RunningChild() is { } running ? running.ToolDisplayName : SummarizeReadSteps(Children);

        /// <summary>Gets what the group's running step acts on; empty once all its steps finished.</summary>
        public string GroupTarget => RunningChild()?.ToolTarget ?? string.Empty;

        /// <summary>Gets how many of a finished group's steps failed, such as "1 failed"; empty when none did.</summary>
        public string GroupFailedText
        {
            get
            {
                var failed = Children.Count(child => child.IsToolFailed);
                return failed == 0 || RunningChild() is not null ? string.Empty : Loc.Format("Workbench.Group.Failed", failed);
            }
        }

        /// <summary>Gets whether <see cref="GroupFailedText"/> has anything to show.</summary>
        public bool HasGroupFailures => GroupFailedText.Length > 0;

        /// <summary>Gets whether the step asked the user for approval at some point, which keeps it out of a read group.</summary>
        public bool AskedForApproval => _askedForApproval;

        /// <summary>Gets or sets whether the model is still writing this message.</summary>
        public bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (!value)
                {
                    FlushContent();
                }

                this.RaiseAndSetIfChanged(ref _isStreaming, value);
            }
        }

        public string CallId { get; } = string.Empty;

        public string ToolName { get; } = string.Empty;

        public string ToolDisplayName { get; } = string.Empty;

        /// <summary>
        /// Gets the call's arguments as "name: value" lines, shortened except for a hand-off to another agent,
        /// whose approval card shows the whole task, folder and sandbox.
        /// </summary>
        public string ArgumentsPreview { get; } = string.Empty;

        /// <summary>Gets how many argument lines a compact card shows; 0 shows them all (a hand-off to another agent).</summary>
        public int ArgumentsMaxLines => Risk is ToolRisk.Delegate or ToolRisk.Agent ? 0 : 4;

        public bool HasArguments => ArgumentsPreview.Length > 0;

        /// <summary>
        /// Gets whether a compact card cuts the arguments short, so the Needs you page offers "Show all".
        /// </summary>
        public bool HasMoreArguments => ArgumentsMaxLines > 0
            && ArgumentsPreview.ReplaceLineEndings("\n").Split('\n').Length > ArgumentsMaxLines;

        /// <summary>
        /// Gets what the call acts on, on one line: the command, file, folder, address or search text.
        /// The step header shows it next to the tool's name, so several calls to one tool can be told apart.
        /// </summary>
        public string ToolTarget { get; } = string.Empty;

        public ToolRisk Risk { get; }

        /// <summary>Gets what the tool may do, such as "Runs programs".</summary>
        public string RiskText => Risk switch
        {
            ToolRisk.Read => Loc.Get("Workbench.Tool.Risk.Read"),
            ToolRisk.Network => Loc.Get("Workbench.Tool.Risk.Network"),
            ToolRisk.Write => Loc.Get("Workbench.Tool.Risk.Write"),
            ToolRisk.Execute => Loc.Get("Workbench.Tool.Risk.Execute"),
            ToolRisk.External => Loc.Get("Workbench.Tool.Risk.External"),
            ToolRisk.Delegate => Loc.Get("Workbench.Tool.Risk.Delegate"),
            ToolRisk.Agent => Loc.Get("Workbench.Tool.Risk.Agent"),
            _ => Risk.ToString()
        };

        /// <summary>
        /// Gets whether "Always allow" is offered. Delegating to another agent asks every time, because Kam cannot
        /// gate what that agent does.
        /// </summary>
        public bool CanAlwaysAllow => Risk != ToolRisk.Delegate;

        /// <summary>Gets the short risk label: read, web, write, shell, external or delegate.</summary>
        public string RiskTag => Risk switch
        {
            ToolRisk.Read => Loc.Get("Workbench.Tool.RiskTag.Read"),
            ToolRisk.Network => Loc.Get("Workbench.Tool.RiskTag.Network"),
            ToolRisk.Write => Loc.Get("Workbench.Tool.RiskTag.Write"),
            ToolRisk.Execute => Loc.Get("Workbench.Tool.RiskTag.Execute"),
            ToolRisk.External => Loc.Get("Workbench.Tool.RiskTag.External"),
            ToolRisk.Delegate => Loc.Get("Workbench.Tool.RiskTag.Delegate"),
            ToolRisk.Agent => Loc.Get("Workbench.Tool.RiskTag.Agent"),
            _ => Risk.ToString()
        };

        /// <summary>Gets whether the risk badge uses the info color: reads, web lookups and subagents, whose own calls are gated.</summary>
        public bool IsReadRisk => Risk is ToolRisk.Read or ToolRisk.Network or ToolRisk.Agent;

        /// <summary>
        /// Gets whether the step's risk earns a tag on its line: writes, programs, anything sent outside and hand-offs.
        /// Reads, web lookups and subagents show none.
        /// </summary>
        public bool ShowsRiskTag => IsToolStep && Risk is ToolRisk.Write or ToolRisk.Execute or ToolRisk.External or ToolRisk.Delegate;

        /// <summary>Gets whether the step started a subagent; its own steps go in <see cref="Children"/>.</summary>
        public bool IsSubagentTask => IsToolStep && Risk == ToolRisk.Agent;

        /// <summary>Gets the run id of the subagent this step started, once it started.</summary>
        public string? SubagentRunId { get; private set; }

        /// <summary>Gets the subagent type name a subagent's own step belongs to, or null for the main agent's steps.</summary>
        public string? SubagentType { get; private set; }

        /// <summary>Gets whether the step is one a subagent made.</summary>
        public bool IsSubagentStep => SubagentType is not null;

        /// <summary>Gets what the subagent started by this step is doing, such as "3 steps · Read file".</summary>
        public string SubagentProgressText
        {
            get => _subagentProgressText;
            private set
            {
                this.RaiseAndSetIfChanged(ref _subagentProgressText, value);
                this.RaisePropertyChanged(nameof(HasSubagentProgress));
            }
        }

        /// <summary>Gets whether <see cref="SubagentProgressText"/> has anything to show.</summary>
        public bool HasSubagentProgress => SubagentProgressText.Length > 0;

        /// <summary>Ties the step to the subagent run it started.</summary>
        /// <param name="runId">The run id.</param>
        public void AttachSubagentRun(string runId)
        {
            SubagentRunId = runId;
            this.RaisePropertyChanged(nameof(SubagentRunId));
        }

        /// <summary>Marks the step as one a subagent of <paramref name="agentType"/> made.</summary>
        /// <param name="agentType">The subagent type, such as "explorer".</param>
        public void MarkSubagentStep(string agentType)
        {
            SubagentType = agentType;
            this.RaisePropertyChanged(nameof(IsSubagentStep));
        }

        /// <summary>
        /// Shows how far the subagent got: its step count and what it is doing now. While the subagent runs,
        /// the step's duration shows how long it has run so far.
        /// </summary>
        /// <param name="stepCount">The subagent's tool calls so far.</param>
        /// <param name="current">The tool it runs now, or null.</param>
        public void ShowSubagentProgress(int stepCount, string? current)
        {
            var steps = Loc.Format("Workbench.Subagent.Steps", stepCount);
            SubagentProgressText = string.IsNullOrWhiteSpace(current) ? steps : $"{steps} · {current}";
            if (IsToolRunning)
            {
                ToolDurationText = AgentTurnTraceViewModel.FormatDuration(Stopwatch.GetElapsedTime(_toolStartedTimestamp));
            }
        }

        /// <summary>Gets the label of a subagent type, such as "Explorer" for "explorer".</summary>
        /// <param name="agentType">The type name.</param>
        public static string SubagentTypeLabel(string? agentType) => agentType?.Trim().ToLowerInvariant() switch
        {
            null or "" => Loc.Get("Workbench.Subagent.Type.Unknown"),
            "explorer" => Loc.Get("Workbench.Subagent.Type.Explorer"),
            "researcher" => Loc.Get("Workbench.Subagent.Type.Researcher"),
            "general" => Loc.Get("Workbench.Subagent.Type.General"),
            var other => other
        };

        /// <summary>Gets whether the risk badge uses the warning color: file writes.</summary>
        public bool IsWriteRisk => Risk == ToolRisk.Write;

        /// <summary>Gets whether the risk badge uses the danger color: programs, anything sent outside, and hand-offs to another agent.</summary>
        public bool IsShellRisk => Risk is ToolRisk.Execute or ToolRisk.External or ToolRisk.Delegate;

        public AgentToolStepState ToolState
        {
            get => _toolState;
            private set
            {
                this.RaiseAndSetIfChanged(ref _toolState, value);
                this.RaisePropertyChanged(nameof(ToolStatusText));
                this.RaisePropertyChanged(nameof(IsAwaitingApproval));
                this.RaisePropertyChanged(nameof(IsToolRunning));
                this.RaisePropertyChanged(nameof(IsToolSucceeded));
                this.RaisePropertyChanged(nameof(IsToolFailed));
                this.RaisePropertyChanged(nameof(ShowsToolDetails));
                this.RaisePropertyChanged(nameof(ShowsToolError));
                this.RaisePropertyChanged(nameof(ShowsToolResult));
            }
        }

        /// <summary>
        /// Gets or sets whether the user opened the item: a step shows its arguments and result, a group or a
        /// subagent's step shows the steps it holds.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                this.RaiseAndSetIfChanged(ref _isExpanded, value);
                this.RaisePropertyChanged(nameof(ShowsToolDetails));
            }
        }

        /// <summary>
        /// Gets whether the step shows its arguments and result: when opened, and while it waits for
        /// approval. A step is one line otherwise.
        /// </summary>
        public bool ShowsToolDetails => IsExpanded || IsAwaitingApproval;

        /// <summary>Gets whether a failed or declined step shows its one-line error under its line.</summary>
        public bool ShowsToolError => IsToolFailed && HasToolSummary;

        /// <summary>Gets whether the opened details show the result; a failed step shows it as its error line instead.</summary>
        public bool ShowsToolResult => HasToolSummary && !IsToolFailed;

        /// <summary>Gets the step's state, such as "Done"; its status glyph shows it as a tooltip.</summary>
        public string ToolStatusText => ToolState switch
        {
            AgentToolStepState.Running => Loc.Get("Workbench.State.Running"),
            AgentToolStepState.AwaitingApproval => Loc.Get("Workbench.State.NeedsApproval"),
            AgentToolStepState.Succeeded => Loc.Get("Workbench.State.Done"),
            AgentToolStepState.Failed => Loc.Get("Workbench.State.Failed"),
            AgentToolStepState.Declined => Loc.Get("Workbench.State.Declined"),
            _ => string.Empty
        };

        public bool IsAwaitingApproval => ToolState == AgentToolStepState.AwaitingApproval;

        public bool IsToolRunning => ToolState == AgentToolStepState.Running;

        public bool IsToolSucceeded => ToolState == AgentToolStepState.Succeeded;

        public bool IsToolFailed => ToolState is AgentToolStepState.Failed or AgentToolStepState.Declined;

        /// <summary>Gets the first line of the tool result.</summary>
        public string ToolSummary
        {
            get => _toolSummary;
            private set
            {
                this.RaiseAndSetIfChanged(ref _toolSummary, value);
                this.RaisePropertyChanged(nameof(HasToolSummary));
                this.RaisePropertyChanged(nameof(ShowsToolError));
                this.RaisePropertyChanged(nameof(ShowsToolResult));
            }
        }

        public bool HasToolSummary => ToolSummary.Length > 0;

        /// <summary>
        /// Gets how long the tool call took from start to finish, without the time it waited for
        /// approval; empty while it runs.
        /// </summary>
        public string ToolDurationText
        {
            get => _toolDurationText;
            private set => this.RaiseAndSetIfChanged(ref _toolDurationText, value);
        }

        /// <summary>Gets the pending approval request, while the card waits for an answer.</summary>
        public string? ApprovalRequestId => _approvalRequestId;

        public static AgentChatMessageViewModel System(string content)
        {
            return new AgentChatMessageViewModel(SystemRole, content, FormatTime(DateTime.Now));
        }

        public static AgentChatMessageViewModel Agent(string content)
        {
            return new AgentChatMessageViewModel(AgentRole, content, FormatTime(DateTime.Now));
        }

        /// <summary>
        /// Creates an empty group of read-only tool calls; <see cref="ToolStepGrouping"/> fills it.
        /// </summary>
        public static AgentChatMessageViewModel ToolGroup()
        {
            return new AgentChatMessageViewModel();
        }

        public static AgentChatMessageViewModel ToolStep(
            string callId,
            string toolName,
            string toolDisplayName,
            string argumentsJson,
            ToolRisk risk)
        {
            return new AgentChatMessageViewModel(
                callId,
                toolName,
                toolDisplayName,
                argumentsJson,
                risk,
                FormatTime(DateTime.Now));
        }

        /// <summary>
        /// Formats the time shown next to a message, such as "14:05", in the interface culture.
        /// </summary>
        /// <param name="time">The local time.</param>
        public static string FormatTime(DateTime time)
        {
            return time.ToString("HH:mm", LocalizationService.Instance.Culture);
        }

        /// <summary>
        /// Adds streamed text. While streaming on the UI thread the visible text refreshes at most
        /// every 50 ms, so a long reply is not laid out again for every token.
        /// </summary>
        /// <param name="text">The new text.</param>
        public void AppendContent(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _pendingContent.Append(text);
            var elapsed = Environment.TickCount64 - _lastContentRefresh;
            if (!IsStreaming || SynchronizationContext.Current is null || elapsed >= StreamRefreshMilliseconds)
            {
                FlushContent();
                return;
            }

            if (!_contentRefreshScheduled)
            {
                _contentRefreshScheduled = true;
                _ = FlushContentLaterAsync((int)(StreamRefreshMilliseconds - elapsed));
            }
        }

        private async Task FlushContentLaterAsync(int delayMilliseconds)
        {
            // Resumes on the UI thread, which captured the context.
            await Task.Delay(delayMilliseconds);
            _contentRefreshScheduled = false;
            FlushContent();
        }

        private void FlushContent()
        {
            if (_pendingContent.Length == 0)
            {
                return;
            }

            _lastContentRefresh = Environment.TickCount64;
            Content = _content + _pendingContent;
            _pendingContent.Clear();
        }

        /// <summary>Gets whether the message was just copied; the copy button shows a check for a moment.</summary>
        public bool IsCopied
        {
            get => _isCopied;
            private set => this.RaiseAndSetIfChanged(ref _isCopied, value);
        }

        /// <summary>
        /// Shows the copied state on the message's copy button for a moment.
        /// </summary>
        public void MarkCopied()
        {
            IsCopied = true;
            _ = ResetCopiedLaterAsync();
        }

        private async Task ResetCopiedLaterAsync()
        {
            await Task.Delay(1500);
            IsCopied = false;
        }

        /// <summary>Gets the tooltip for "Always allow", naming the rule it saves.</summary>
        public string AlwaysAllowHint => string.IsNullOrWhiteSpace(_alwaysAllowRule)
            ? Loc.Get("Workbench.Tool.AlwaysAllowHint")
            : Loc.Format("Workbench.Tool.AlwaysAllowRuleHint", _alwaysAllowRule);

        public void RequestApproval(string requestId, string? alwaysAllowRule = null)
        {
            _approvalRequestId = requestId;
            _askedForApproval = true;
            if (!string.IsNullOrWhiteSpace(alwaysAllowRule))
            {
                _alwaysAllowRule = alwaysAllowRule;
                this.RaisePropertyChanged(nameof(AlwaysAllowHint));
            }

            if (_approvalStartedTimestamp == 0)
            {
                _approvalStartedTimestamp = Stopwatch.GetTimestamp();
            }

            ToolState = AgentToolStepState.AwaitingApproval;
        }

        /// <summary>
        /// Records the user's answer on the card; the result arrives later through <see cref="CompleteTool"/>.
        /// </summary>
        public void ApprovalAnswered(bool approved)
        {
            _approvalRequestId = null;
            EndApprovalWait();
            ToolState = approved ? AgentToolStepState.Running : AgentToolStepState.Declined;
        }

        /// <summary>
        /// Shows the tool's result. A failed call whose summary is the runtime's "Declined" shows as declined.
        /// </summary>
        /// <param name="success">Whether the call succeeded.</param>
        /// <param name="summary">The first line of the result.</param>
        /// <param name="duration">
        /// How long the call took when it is known from elsewhere, such as a saved subagent run. A step rebuilt
        /// from a saved thread passes <paramref name="measured"/> false and no duration, so it shows none.
        /// </param>
        /// <param name="measured">Whether the call ran in this session, so its time since it started is its duration.</param>
        public void CompleteTool(bool success, string summary, TimeSpan? duration = null, bool measured = true)
        {
            _approvalRequestId = null;
            var declined = !success && summary == DeclinedSummary;
            _askedForApproval |= declined;
            ToolSummary = declined ? Loc.Get("Workbench.State.Declined") : summary;
            ToolState = success
                ? AgentToolStepState.Succeeded
                : declined ? AgentToolStepState.Declined : AgentToolStepState.Failed;
            ToolDurationText = duration is { } known
                ? AgentTurnTraceViewModel.FormatDuration(known)
                : measured ? AgentTurnTraceViewModel.FormatDuration(RunTime()) : string.Empty;
        }

        /// <summary>Shows a duration known from elsewhere, such as a saved subagent run.</summary>
        /// <param name="duration">How long it took.</param>
        public void ShowDuration(TimeSpan duration)
        {
            ToolDurationText = AgentTurnTraceViewModel.FormatDuration(duration);
        }

        /// <summary>
        /// Marks a step that never finished because the turn stopped.
        /// </summary>
        public void Abandon()
        {
            if (ToolState is AgentToolStepState.Running or AgentToolStepState.AwaitingApproval)
            {
                _approvalRequestId = null;
                ToolSummary = Loc.Get("Workbench.Tool.Stopped");
                ToolState = AgentToolStepState.Failed;
                ToolDurationText = AgentTurnTraceViewModel.FormatDuration(RunTime());
            }
        }

        /// <summary>
        /// The time since the call started, less the time it waited for the user to answer.
        /// </summary>
        private TimeSpan RunTime()
        {
            EndApprovalWait();
            return Stopwatch.GetElapsedTime(_toolStartedTimestamp) - _approvalWait;
        }

        private void EndApprovalWait()
        {
            if (_approvalStartedTimestamp == 0)
            {
                return;
            }

            _approvalWait += Stopwatch.GetElapsedTime(_approvalStartedTimestamp);
            _approvalStartedTimestamp = 0;
        }

        /// <summary>
        /// Re-reads the labels that follow the interface language: the author, risk, state and approval hint.
        /// Message text and tool results already written stay as they are.
        /// </summary>
        public void RefreshLocalizedText()
        {
            this.RaisePropertyChanged(nameof(Role));
            foreach (var child in Children)
            {
                child.RefreshLocalizedText();
            }

            if (IsToolGroup)
            {
                RaiseGroupText();
                return;
            }

            if (!IsToolStep)
            {
                return;
            }

            this.RaisePropertyChanged(nameof(RiskText));
            this.RaisePropertyChanged(nameof(RiskTag));
            this.RaisePropertyChanged(nameof(ToolStatusText));
            this.RaisePropertyChanged(nameof(AlwaysAllowHint));
        }

        /// <summary>
        /// Summarizes read-only steps by what they did, such as "Explored 3 files · 2 searches", or "Ran 4 read steps"
        /// when none of them read files, searched or opened a web page. Tools are told apart by their names.
        /// </summary>
        /// <param name="steps">The steps.</param>
        public static string SummarizeReadSteps(IEnumerable<AgentChatMessageViewModel> steps)
        {
            int files = 0, searches = 0, pages = 0, other = 0;
            foreach (var step in steps)
            {
                var name = step.ToolName;
                if (name.Contains("search", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("find", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("grep", StringComparison.OrdinalIgnoreCase))
                {
                    searches++;
                }
                else if (name.StartsWith("web_", StringComparison.OrdinalIgnoreCase))
                {
                    pages++;
                }
                else if (name.StartsWith("file", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("workspace_", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("code_", StringComparison.OrdinalIgnoreCase))
                {
                    files++;
                }
                else
                {
                    other++;
                }
            }

            if (files + searches + pages == 0)
            {
                return Loc.Format("Workbench.Group.ReadSteps", other);
            }

            var parts = new List<string>(4);
            AddCount(parts, files, "Workbench.Group.File", "Workbench.Group.Files");
            AddCount(parts, searches, "Workbench.Group.Search", "Workbench.Group.Searches");
            AddCount(parts, pages, "Workbench.Group.Page", "Workbench.Group.Pages");
            AddCount(parts, other, "Workbench.Group.OtherStep", "Workbench.Group.OtherSteps");
            return Loc.Format("Workbench.Group.Explored", string.Join(" · ", parts));
        }

        private static void AddCount(List<string> parts, int count, string oneKey, string manyKey)
        {
            if (count > 0)
            {
                parts.Add(count == 1 ? Loc.Get(oneKey) : Loc.Format(manyKey, count));
            }
        }

        private AgentChatMessageViewModel? RunningChild() => Children.LastOrDefault(child => child.IsToolRunning);

        private void WatchChildren()
        {
            Children.CollectionChanged += OnChildrenChanged;
        }

        private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (AgentChatMessageViewModel child in e.OldItems)
                {
                    child.PropertyChanged -= OnChildPropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                foreach (AgentChatMessageViewModel child in e.NewItems)
                {
                    child.PropertyChanged += OnChildPropertyChanged;
                }
            }

            this.RaisePropertyChanged(nameof(HasChildren));
            this.RaisePropertyChanged(nameof(HasChildAwaitingApproval));
            UpdateGroupState();
        }

        private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IsAwaitingApproval))
            {
                // A subagent's step that asks for approval opens its card, so the approval row is in sight.
                if (IsSubagentTask && sender is AgentChatMessageViewModel { IsAwaitingApproval: true })
                {
                    IsExpanded = true;
                }

                this.RaisePropertyChanged(nameof(HasChildAwaitingApproval));
            }
            else if (e.PropertyName == nameof(ToolState))
            {
                UpdateGroupState();
            }
        }

        private void UpdateGroupState()
        {
            if (!IsToolGroup)
            {
                return;
            }

            ToolState = Children.Any(child => child.IsToolRunning || child.IsAwaitingApproval)
                ? AgentToolStepState.Running
                : AgentToolStepState.Succeeded;
            RaiseGroupText();
        }

        private void RaiseGroupText()
        {
            this.RaisePropertyChanged(nameof(GroupTitle));
            this.RaisePropertyChanged(nameof(GroupTarget));
            this.RaisePropertyChanged(nameof(GroupFailedText));
            this.RaisePropertyChanged(nameof(HasGroupFailures));
        }

        private static string FormatArguments(string argumentsJson, bool full)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson) || argumentsJson == "{}")
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Shorten(argumentsJson, 400);
                }

                var lines = new System.Collections.Generic.List<string>();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText();
                    lines.Add(full
                        ? $"{property.Name}: {value.ReplaceLineEndings(Environment.NewLine)}"
                        : $"{property.Name}: {Shorten(value.ReplaceLineEndings(" "), 160)}");
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (JsonException)
            {
                return Shorten(argumentsJson, 400);
            }
        }

        private static readonly string[] TargetArgumentNames =
        [
            "command", "filePath", "path", "directoryPath", "directory", "url", "query", "pattern", "applicationName", "task"
        ];

        private static string SummarizeTarget(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                string? first = null;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(property.Value.GetString()))
                    {
                        continue;
                    }

                    var value = property.Value.GetString()!;
                    if (Array.Exists(TargetArgumentNames, name => name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        first = value;
                        break;
                    }

                    first ??= value;
                }

                return first is null ? string.Empty : Shorten(first.ReplaceLineEndings(" ").Trim(), 120);
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string ReadArgument(string argumentsJson, string name)
        {
            try
            {
                using var document = JsonDocument.Parse(argumentsJson);
                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                        ? value.GetString() ?? string.Empty
                        : string.Empty;
            }
            catch (JsonException)
            {
                return string.Empty;
            }
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text[..max] + "...";
        }
    }
}

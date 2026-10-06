using ReactiveUI;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.Services;
using System;
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
        ToolStep
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
    /// One item in a chat thread: a message, or a tool call with its approval card and result.
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
        private string _toolSummary = string.Empty;
        private string? _approvalRequestId;
        private string? _alwaysAllowRule;
        private bool _isCopied;

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
            CallId = callId;
            ToolName = toolName;
            ToolDisplayName = toolDisplayName;
            ArgumentsPreview = FormatArguments(argumentsJson);
            Risk = risk;
            _toolState = AgentToolStepState.Running;
        }

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

        /// <summary>Gets whether the item is a tool call.</summary>
        public bool IsToolStep => Kind == AgentChatItemKind.ToolStep;

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

        /// <summary>Gets the call's arguments as short "name: value" lines.</summary>
        public string ArgumentsPreview { get; } = string.Empty;

        public bool HasArguments => ArgumentsPreview.Length > 0;

        public ToolRisk Risk { get; }

        /// <summary>Gets what the tool may do, such as "Runs programs".</summary>
        public string RiskText => Risk switch
        {
            ToolRisk.Read => Loc.Get("Workbench.Tool.Risk.Read"),
            ToolRisk.Network => Loc.Get("Workbench.Tool.Risk.Network"),
            ToolRisk.Write => Loc.Get("Workbench.Tool.Risk.Write"),
            ToolRisk.Execute => Loc.Get("Workbench.Tool.Risk.Execute"),
            ToolRisk.External => Loc.Get("Workbench.Tool.Risk.External"),
            _ => Risk.ToString()
        };

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
            }
        }

        /// <summary>Gets the step's state as its pill shows it, such as "Done".</summary>
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
            }
        }

        public bool HasToolSummary => ToolSummary.Length > 0;

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
            if (!string.IsNullOrWhiteSpace(alwaysAllowRule))
            {
                _alwaysAllowRule = alwaysAllowRule;
                this.RaisePropertyChanged(nameof(AlwaysAllowHint));
            }

            ToolState = AgentToolStepState.AwaitingApproval;
        }

        /// <summary>
        /// Records the user's answer on the card; the result arrives later through <see cref="CompleteTool"/>.
        /// </summary>
        public void ApprovalAnswered(bool approved)
        {
            _approvalRequestId = null;
            ToolState = approved ? AgentToolStepState.Running : AgentToolStepState.Declined;
        }

        /// <summary>
        /// Shows the tool's result. A failed call whose summary is the runtime's "Declined" shows as declined.
        /// </summary>
        /// <param name="success">Whether the call succeeded.</param>
        /// <param name="summary">The first line of the result.</param>
        public void CompleteTool(bool success, string summary)
        {
            _approvalRequestId = null;
            var declined = !success && summary == DeclinedSummary;
            ToolSummary = declined ? Loc.Get("Workbench.State.Declined") : summary;
            ToolState = success
                ? AgentToolStepState.Succeeded
                : declined ? AgentToolStepState.Declined : AgentToolStepState.Failed;
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
            }
        }

        /// <summary>
        /// Re-reads the labels that follow the interface language: the author, risk, state and approval hint.
        /// Message text and tool results already written stay as they are.
        /// </summary>
        public void RefreshLocalizedText()
        {
            this.RaisePropertyChanged(nameof(Role));
            if (!IsToolStep)
            {
                return;
            }

            this.RaisePropertyChanged(nameof(RiskText));
            this.RaisePropertyChanged(nameof(ToolStatusText));
            this.RaisePropertyChanged(nameof(AlwaysAllowHint));
        }

        private static string FormatArguments(string argumentsJson)
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
                    lines.Add($"{property.Name}: {Shorten(value.ReplaceLineEndings(" "), 160)}");
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (JsonException)
            {
                return Shorten(argumentsJson, 400);
            }
        }

        private static string Shorten(string text, int max)
        {
            return text.Length <= max ? text : text[..max] + "...";
        }
    }
}

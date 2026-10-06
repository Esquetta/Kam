using ReactiveUI;
using SmartVoiceAgent.Core.Models.Agents;
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
    /// Message content can grow while the model streams.
    /// </summary>
    public sealed class AgentChatMessageViewModel : ReactiveObject
    {
        private const int StreamRefreshMilliseconds = 50;

        private readonly StringBuilder _pendingContent = new();
        private long _lastContentRefresh;
        private bool _contentRefreshScheduled;
        private string _content;
        private bool _isStreaming;
        private AgentToolStepState _toolState;
        private string _toolSummary = string.Empty;
        private string? _approvalRequestId;

        public AgentChatMessageViewModel(
            string role,
            string content,
            string timeText)
        {
            Role = role;
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
            : this("Kam", string.Empty, timeText)
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

        public string Role { get; }

        public string Content
        {
            get => _content;
            private set => this.RaiseAndSetIfChanged(ref _content, value);
        }

        public string TimeText { get; }

        /// <summary>
        /// Gets whether the message was written by the user, which renders it as an outgoing bubble.
        /// </summary>
        public bool IsUser => Kind == AgentChatItemKind.Message && Role.Equals("You", StringComparison.OrdinalIgnoreCase);

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

        public string RiskText => Risk switch
        {
            ToolRisk.Read => "Read",
            ToolRisk.Network => "Web",
            ToolRisk.Write => "Writes files",
            ToolRisk.Execute => "Runs programs",
            ToolRisk.External => "Sends externally",
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

        public string ToolStatusText => ToolState switch
        {
            AgentToolStepState.Running => "Running",
            AgentToolStepState.AwaitingApproval => "Needs approval",
            AgentToolStepState.Succeeded => "Done",
            AgentToolStepState.Failed => "Failed",
            AgentToolStepState.Declined => "Declined",
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
            return new AgentChatMessageViewModel("System", content, DateTime.Now.ToString("HH:mm"));
        }

        public static AgentChatMessageViewModel Agent(string content)
        {
            return new AgentChatMessageViewModel("Kam", content, DateTime.Now.ToString("HH:mm"));
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
                DateTime.Now.ToString("HH:mm"));
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

        /// <summary>Gets the tooltip for "Always allow", naming the rule it saves.</summary>
        public string AlwaysAllowHint { get; private set; } = "Run this tool without asking from now on";

        public void RequestApproval(string requestId, string? alwaysAllowRule = null)
        {
            _approvalRequestId = requestId;
            if (!string.IsNullOrWhiteSpace(alwaysAllowRule))
            {
                AlwaysAllowHint = $"Stop asking for {alwaysAllowRule}";
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

        public void CompleteTool(bool success, string summary)
        {
            _approvalRequestId = null;
            ToolSummary = summary;
            ToolState = success
                ? AgentToolStepState.Succeeded
                : summary == "Declined" ? AgentToolStepState.Declined : AgentToolStepState.Failed;
        }

        /// <summary>
        /// Marks a step that never finished because the turn stopped.
        /// </summary>
        public void Abandon()
        {
            if (ToolState is AgentToolStepState.Running or AgentToolStepState.AwaitingApproval)
            {
                _approvalRequestId = null;
                ToolSummary = "Stopped before it finished.";
                ToolState = AgentToolStepState.Failed;
            }
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

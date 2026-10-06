using Microsoft.Extensions.AI;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// An approval mode as offered in the composer. Its name and description follow the interface
    /// language when the main window calls <see cref="RefreshLocalizedText"/>.
    /// </summary>
    public sealed class ApprovalModeOption : ReactiveObject
    {
        /// <summary>
        /// Creates the option for <paramref name="mode"/>.
        /// </summary>
        /// <param name="mode">The mode.</param>
        public ApprovalModeOption(ApprovalMode mode)
        {
            Mode = mode;
        }

        /// <summary>Gets the mode.</summary>
        public ApprovalMode Mode { get; }

        /// <summary>Gets the short name, such as "Ask first".</summary>
        public string Label => Mode switch
        {
            ApprovalMode.AutoEdit => Loc.Get("Workbench.Approval.AutoEdit"),
            ApprovalMode.FullAuto => Loc.Get("Workbench.Approval.FullAuto"),
            _ => Loc.Get("Workbench.Approval.Ask")
        };

        /// <summary>Gets what runs without asking.</summary>
        public string Description => Mode switch
        {
            ApprovalMode.AutoEdit => Loc.Get("Workbench.Approval.AutoEditDescription"),
            ApprovalMode.FullAuto => Loc.Get("Workbench.Approval.FullAutoDescription"),
            _ => Loc.Get("Workbench.Approval.AskDescription")
        };

        /// <summary>
        /// Re-reads the name and description after the interface language changes.
        /// </summary>
        public void RefreshLocalizedText()
        {
            this.RaisePropertyChanged(nameof(Label));
            this.RaisePropertyChanged(nameof(Description));
        }

        /// <inheritdoc />
        public override string ToString() => Label;
    }

    /// <summary>
    /// A message sent while another turn was running, waiting for that turn to finish.
    /// </summary>
    /// <param name="Session">The chat the message was sent in.</param>
    /// <param name="DisplayText">What the user typed or said, shown in the thread when it runs.</param>
    /// <param name="Message">What goes to the agent, with attachments or command expansion applied.</param>
    public sealed record QueuedAgentMessage(AgentChatSessionViewModel Session, string DisplayText, string Message, bool FromVoice = false);

    /// <summary>
    /// Chat side of the tool-calling agent runtime: streaming replies, tool steps and approval cards.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private IAgentRuntime? _agentRuntime;
        private IToolPermissionService? _toolPermissions;
        private IAgentSessionStore? _agentSessionStore;
        private CancellationTokenSource? _agentTurnCancellation;
        private bool _isAgentTurnRunning;
        private readonly List<QueuedAgentMessage> _queuedAgentMessages = [];
        private ApprovalModeOption _selectedApprovalMode = ApprovalModes[0];

        /// <summary>
        /// Gets the approval modes the user can pick from.
        /// </summary>
        public static IReadOnlyList<ApprovalModeOption> ApprovalModes { get; } =
        [
            new(ApprovalMode.Ask),
            new(ApprovalMode.AutoEdit),
            new(ApprovalMode.FullAuto)
        ];

        /// <summary>
        /// Gets the notice shown where earlier messages were summarized.
        /// </summary>
        private static string CompactedNotice => Loc.Get("Workbench.Chat.Compacted");

        public ICommand ApproveToolCallCommand { get; }

        public ICommand AlwaysAllowToolCallCommand { get; }

        public ICommand DenyToolCallCommand { get; }

        public ICommand StopAgentTurnCommand { get; }

        public ICommand ClearQueuedAgentMessagesCommand { get; }

        /// <summary>
        /// Gets whether chat runs through the tool-calling agent.
        /// </summary>
        public bool IsAgentRuntimeEnabled => _agentRuntime is not null;

        /// <summary>
        /// Gets whether an agent turn is running; the composer shows Stop instead of Send.
        /// </summary>
        public bool IsAgentTurnRunning
        {
            get => _isAgentTurnRunning;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isAgentTurnRunning, value);
                this.RaisePropertyChanged(nameof(IsSendVisible));
                UpdateStatusProperties();
            }
        }

        public bool IsSendVisible => !IsAgentTurnRunning;

        /// <summary>
        /// Gets the messages waiting for the running turn to finish, oldest first.
        /// </summary>
        public IReadOnlyList<QueuedAgentMessage> QueuedAgentMessages => _queuedAgentMessages;

        /// <summary>
        /// Gets whether messages are waiting for the running turn to finish.
        /// </summary>
        public bool HasQueuedAgentMessages => _queuedAgentMessages.Count > 0;

        /// <summary>
        /// Gets the composer line naming what will be sent once the running turn finishes.
        /// </summary>
        public string QueuedAgentMessagesText => _queuedAgentMessages.Count switch
        {
            0 => string.Empty,
            1 => Loc.Format("Workbench.Composer.SendsNext", _queuedAgentMessages[0].DisplayText),
            var count => Loc.Format("Workbench.Composer.SendsNextMore", _queuedAgentMessages[0].DisplayText, count - 1)
        };

        public ApprovalModeOption SelectedApprovalMode
        {
            get => _selectedApprovalMode;
            set
            {
                if (value is null || value == _selectedApprovalMode)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedApprovalMode, value);
                if (_toolPermissions is not null && _toolPermissions.Mode != value.Mode)
                {
                    _toolPermissions.Mode = value.Mode;
                    AddLog($"APPROVAL_MODE: {value.Label}");
                }
            }
        }

        /// <summary>
        /// Connects chat to the agent runtime and lists threads saved by earlier runs.
        /// </summary>
        public void SetAgentRuntime(
            IAgentRuntime agentRuntime,
            IToolPermissionService toolPermissions,
            IAgentSessionStore sessionStore,
            IAgentCommandCatalog? agentCommands = null)
        {
            _agentRuntime = agentRuntime;
            _agentCommands = agentCommands;
            _toolPermissions = toolPermissions;
            _agentSessionStore = sessionStore;
            _selectedApprovalMode = ApprovalModes.FirstOrDefault(option => option.Mode == toolPermissions.Mode) ?? ApprovalModes[0];
            this.RaisePropertyChanged(nameof(SelectedApprovalMode));
            this.RaisePropertyChanged(nameof(IsAgentRuntimeEnabled));
            UpdateStatusProperties();
            _ = LoadSavedAgentSessionsAsync();
        }

        /// <summary>
        /// Runs one agent turn in a thread and renders its events as they arrive.
        /// </summary>
        /// <param name="session">The chat to run the turn in.</param>
        /// <param name="message">What goes to the agent.</param>
        /// <param name="fromVoice">Whether the turn started from a voice command, so its reply can be read aloud.</param>
        public async Task RunAgentTurnAsync(AgentChatSessionViewModel session, string message, bool fromVoice = false)
        {
            if (_agentRuntime is null || IsAgentTurnRunning)
            {
                return;
            }

            using var cancellation = new CancellationTokenSource();
            _agentTurnCancellation = cancellation;
            IsAgentTurnRunning = true;
            session.IsRunning = true;

            AgentChatMessageViewModel? streaming = null;
            var wroteText = false;
            var steps = new Dictionary<string, AgentChatMessageViewModel>(StringComparer.Ordinal);
            var approvalRequests = new List<string>();

            try
            {
                await foreach (var agentEvent in _agentRuntime.RunTurnAsync(session.SessionId, message, cancellation.Token))
                {
                    // Text deltas only grow the streaming message; the thread list and counters
                    // change when an item is added, so they refresh then and not per token.
                    var timelineChanged = agentEvent is not AgentTextDelta;
                    switch (agentEvent)
                    {
                        case AgentTextDelta delta:
                            if (streaming is null)
                            {
                                streaming = AgentChatMessageViewModel.Agent(string.Empty);
                                streaming.IsStreaming = true;
                                session.AddMessage(streaming);
                                timelineChanged = true;
                            }

                            streaming.AppendContent(delta.Text);
                            wroteText = true;
                            break;

                        case AgentToolCallStarted started:
                            FinishStreaming(session, ref streaming);
                            var step = AgentChatMessageViewModel.ToolStep(
                                started.CallId,
                                started.ToolName,
                                started.DisplayName,
                                started.ArgumentsJson,
                                started.Risk);
                            steps[started.CallId] = step;
                            session.AddMessage(step);
                            AddLog($"TOOL: {started.ToolName}");
                            break;

                        case AgentApprovalRequested approval:
                            if (steps.TryGetValue(approval.CallId, out var waiting))
                            {
                                waiting.RequestApproval(approval.RequestId, approval.AlwaysAllowRule);
                                approvalRequests.Add(approval.RequestId);
                                NotifyApprovalRequested(session, waiting, approval.RequestId);
                            }

                            AddLog($"TOOL_APPROVAL_NEEDED: {approval.ToolName}");
                            break;

                        case AgentToolCallCompleted completed:
                            if (!steps.TryGetValue(completed.CallId, out var finished))
                            {
                                finished = AgentChatMessageViewModel.ToolStep(
                                    completed.CallId,
                                    completed.ToolName,
                                    completed.ToolName,
                                    "{}",
                                    ToolRisk.Read);
                                steps[completed.CallId] = finished;
                                session.AddMessage(finished);
                            }

                            finished.CompleteTool(completed.Success, completed.Summary);
                            AddLog(completed.Success
                                ? $"TOOL_DONE: {completed.ToolName}"
                                : $"TOOL_FAILED: {completed.ToolName} {completed.Summary}");
                            break;

                        case AgentContextCompacted compacted:
                            FinishStreaming(session, ref streaming);
                            session.AddMessage(AgentChatMessageViewModel.System(CompactedNotice));
                            AddLog($"CONTEXT_COMPACTED: {compacted.SummarizedMessageCount} messages, ~{compacted.TokensBefore} to ~{compacted.TokensAfter} tokens");
                            break;

                        case AgentTurnCompleted done:
                            FinishStreaming(session, ref streaming);
                            if (!wroteText && !string.IsNullOrWhiteSpace(done.FinalText))
                            {
                                session.AddMessage(AgentChatMessageViewModel.Agent(done.FinalText));
                            }

                            if (!string.IsNullOrWhiteSpace(done.FinalText))
                            {
                                session.Summary = done.FinalText.ReplaceLineEndings(" ");
                                ReadReplyAloud(done.FinalText, fromVoice);
                            }

                            AddLog($"AGENT_TURN_DONE: {done.ToolCallCount} tool calls, {done.InputTokens + done.OutputTokens} tokens");
                            break;

                        case AgentTurnFailed failed:
                            FinishStreaming(session, ref streaming);
                            session.AddMessage(AgentChatMessageViewModel.System(
                                failed.Message == "Stopped."
                                    ? Loc.Get("Workbench.Chat.Stopped")
                                    : Loc.Format("Workbench.Chat.Error", failed.Message)));
                            AddLog($"AGENT_TURN_FAILED: {failed.Message}");
                            break;
                    }

                    if (timelineChanged)
                    {
                        session.MarkActivity(DateTimeOffset.Now);
                        RaiseAgentChatStateChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                FinishStreaming(session, ref streaming);
                session.AddMessage(AgentChatMessageViewModel.System(Loc.Format("Workbench.Chat.Error", ex.Message)));
                AddLog($"AGENT_TURN_FAILED: {ex.Message}");
            }
            finally
            {
                FinishStreaming(session, ref streaming);
                foreach (var step in steps.Values)
                {
                    step.Abandon();
                }

                foreach (var requestId in approvalRequests)
                {
                    DismissApprovalNotice(requestId);
                }

                _agentTurnCancellation = null;
                session.IsRunning = false;
                IsAgentTurnRunning = false;
                RaiseAgentChatStateChanged();
            }

            await RunNextQueuedAgentMessageAsync();
        }

        /// <summary>
        /// Starts a turn, or queues it when another turn is running so nothing sent mid-turn is lost.
        /// </summary>
        /// <param name="session">The chat to run the turn in.</param>
        /// <param name="displayText">What the user typed or said.</param>
        /// <param name="message">What goes to the agent.</param>
        /// <param name="fromVoice">Whether the turn started from a voice command.</param>
        private async Task StartOrQueueAgentTurnAsync(
            AgentChatSessionViewModel session,
            string displayText,
            string message,
            bool fromVoice = false)
        {
            if (IsAgentTurnRunning)
            {
                _queuedAgentMessages.Add(new QueuedAgentMessage(session, displayText, message, fromVoice));
                RaiseQueuedAgentMessagesChanged();
                AddLog($"QUEUED: {displayText}");
                return;
            }

            AddAgentChatMessage(session, AgentChatMessageViewModel.UserRole, displayText);
            await RunAgentTurnAsync(session, message, fromVoice);
        }

        /// <summary>
        /// Queues typed input while a turn runs. Plain messages, Markdown commands and /compact
        /// wait for the turn; built-in commands such as /settings still run at once.
        /// </summary>
        /// <param name="input">The trimmed composer text.</param>
        /// <returns>True when the input was queued.</returns>
        private bool TryQueueAgentInput(string input)
        {
            if (!IsAgentTurnRunning || _agentRuntime is null || SelectedAgentChatSession is not { } session)
            {
                return false;
            }

            string message;
            if (input.Equals("/compact", StringComparison.OrdinalIgnoreCase))
            {
                message = input;
            }
            else if (input.StartsWith('/'))
            {
                if (_agentCommands is null
                    || IsBuiltInSlashCommand(input)
                    || !_agentCommands.TryExpand(input, out var commandPrompt))
                {
                    return false;
                }

                message = commandPrompt;
            }
            else
            {
                message = BuildCommandSubmission(input, ComposerAttachments);
                ClearComposerAttachments();
            }

            CommandInputText = string.Empty;
            SlashCommandSuggestions.Clear();
            IsSlashCommandPaletteVisible = false;
            _queuedAgentMessages.Add(new QueuedAgentMessage(session, input, message));
            RaiseQueuedAgentMessagesChanged();
            AddLog($"QUEUED: {input}");
            return true;
        }

        private async Task RunNextQueuedAgentMessageAsync()
        {
            while (!IsAgentTurnRunning && _queuedAgentMessages.Count > 0)
            {
                var next = _queuedAgentMessages[0];
                _queuedAgentMessages.RemoveAt(0);
                RaiseQueuedAgentMessagesChanged();

                // The chat may have been closed while the message waited.
                if (!AgentChatSessions.Contains(next.Session))
                {
                    continue;
                }

                AddAgentChatMessage(next.Session, AgentChatMessageViewModel.UserRole, next.DisplayText);

                // That turn runs whatever is still queued when it finishes.
                await RunAgentTurnAsync(next.Session, next.Message, next.FromVoice);
                return;
            }
        }

        private void ClearQueuedAgentMessages()
        {
            if (_queuedAgentMessages.Count == 0)
            {
                return;
            }

            _queuedAgentMessages.Clear();
            RaiseQueuedAgentMessagesChanged();
            AddLog("QUEUE_CLEARED");
        }

        private void RaiseQueuedAgentMessagesChanged()
        {
            this.RaisePropertyChanged(nameof(QueuedAgentMessages));
            this.RaisePropertyChanged(nameof(HasQueuedAgentMessages));
            this.RaisePropertyChanged(nameof(QueuedAgentMessagesText));
        }

        private void StopAgentTurn()
        {
            _agentTurnCancellation?.Cancel();
        }

        private void ResolveToolApproval(AgentChatMessageViewModel? step, bool approved, bool alwaysAllow)
        {
            if (step?.ApprovalRequestId is not { } requestId || _agentRuntime is null)
            {
                return;
            }

            DismissApprovalNotice(requestId);
            if (_agentRuntime.ResolveApproval(requestId, approved, alwaysAllow))
            {
                step.ApprovalAnswered(approved);
                AddLog(approved
                    ? alwaysAllow ? $"TOOL_ALWAYS_ALLOWED: {step.ToolName}" : $"TOOL_APPROVED: {step.ToolName}"
                    : $"TOOL_DECLINED: {step.ToolName}");
            }
        }

        private static void FinishStreaming(AgentChatSessionViewModel session, ref AgentChatMessageViewModel? streaming)
        {
            if (streaming is null)
            {
                return;
            }

            streaming.IsStreaming = false;
            if (string.IsNullOrWhiteSpace(streaming.Content))
            {
                session.RemoveMessage(streaming);
            }

            streaming = null;
        }

        private async Task LoadSavedAgentSessionsAsync()
        {
            if (_agentSessionStore is null)
            {
                return;
            }

            try
            {
                var saved = await _agentSessionStore.ListAsync();
                foreach (var summary in saved)
                {
                    if (AgentChatSessions.Any(session => session.SessionId == summary.Id))
                    {
                        continue;
                    }

                    AgentChatSessions.Add(AgentChatSessionViewModel.CreateSaved(
                        summary.Id,
                        summary.Title,
                        FormatRelativeTime(summary.UpdatedAt, DateTimeOffset.Now),
                        summary.MessageCount,
                        summary.ModelId,
                        summary.HasCustomTitle,
                        summary.UpdatedAt));
                }

                ApplyAgentChatSearch();
                RaiseAgentChatStateChanged();
            }
            catch (Exception ex)
            {
                AddLog($"AGENT_THREADS_UNAVAILABLE: {ex.Message}");
            }
        }

        private async Task LoadAgentSessionHistoryAsync(AgentChatSessionViewModel session)
        {
            if (_agentSessionStore is null)
            {
                return;
            }

            try
            {
                var history = await _agentSessionStore.LoadAsync(session.SessionId);
                session.LoadHistory(BuildTimeline(history));
                RaiseAgentChatStateChanged();
            }
            catch (Exception ex)
            {
                AddLog($"AGENT_THREAD_LOAD_FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Turns a saved conversation back into chat bubbles and tool steps.
        /// </summary>
        public static IReadOnlyList<AgentChatMessageViewModel> BuildTimeline(IEnumerable<ChatMessage> history)
        {
            var items = new List<AgentChatMessageViewModel>();
            var steps = new Dictionary<string, AgentChatMessageViewModel>(StringComparer.Ordinal);

            foreach (var message in history)
            {
                // The runtime only saves system messages for compaction summaries.
                if (message.Role == ChatRole.System)
                {
                    items.Add(AgentChatMessageViewModel.System(CompactedNotice));
                    continue;
                }

                if (message.Role == ChatRole.User)
                {
                    if (!string.IsNullOrWhiteSpace(message.Text))
                    {
                        items.Add(new AgentChatMessageViewModel(AgentChatMessageViewModel.UserRole, message.Text, string.Empty));
                    }

                    continue;
                }

                if (message.Role == ChatRole.Assistant)
                {
                    var text = new StringBuilder();
                    foreach (var content in message.Contents)
                    {
                        switch (content)
                        {
                            case TextContent textContent:
                                text.Append(textContent.Text);
                                break;
                            case FunctionCallContent call:
                                FlushText(items, text);
                                var step = AgentChatMessageViewModel.ToolStep(
                                    call.CallId,
                                    call.Name,
                                    call.Name,
                                    SerializeArguments(call.Arguments),
                                    ToolRisk.Read);
                                steps[call.CallId] = step;
                                items.Add(step);
                                break;
                        }
                    }

                    FlushText(items, text);
                    continue;
                }

                if (message.Role == ChatRole.Tool)
                {
                    foreach (var result in message.Contents.OfType<FunctionResultContent>())
                    {
                        if (!steps.TryGetValue(result.CallId, out var step))
                        {
                            continue;
                        }

                        var resultText = result.Result?.ToString() ?? string.Empty;
                        if (resultText.StartsWith("The user declined", StringComparison.Ordinal))
                        {
                            step.CompleteTool(false, "Declined");
                        }
                        else
                        {
                            var firstLine = resultText.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
                            step.CompleteTool(
                                !resultText.StartsWith("Error", StringComparison.OrdinalIgnoreCase),
                                firstLine.Length <= 160 ? firstLine : firstLine[..160] + "...");
                        }
                    }
                }
            }

            foreach (var step in steps.Values)
            {
                step.Abandon();
            }

            return items;
        }

        /// <summary>
        /// Formats a thread's last activity like "5m", "3h", "Yesterday" or "Oct 2" in the interface language.
        /// </summary>
        public static string FormatRelativeTime(DateTimeOffset value, DateTimeOffset now)
        {
            return AgentChatSessionViewModel.FormatRelativeTime(value, now);
        }

        /// <summary>
        /// Formats a thread's last activity in the language of <paramref name="localization"/>,
        /// such as "5 dk önce" or "Dün" in Turkish.
        /// </summary>
        public static string FormatRelativeTime(DateTimeOffset value, DateTimeOffset now, LocalizationService localization)
        {
            return AgentChatSessionViewModel.FormatRelativeTime(value, now, localization);
        }

        private static void FlushText(List<AgentChatMessageViewModel> items, StringBuilder text)
        {
            if (text.Length == 0)
            {
                return;
            }

            var content = text.ToString();
            text.Clear();
            if (!string.IsNullOrWhiteSpace(content))
            {
                items.Add(new AgentChatMessageViewModel(AgentChatMessageViewModel.AgentRole, content, string.Empty));
            }
        }

        private static string SerializeArguments(IDictionary<string, object?>? arguments)
        {
            if (arguments is null || arguments.Count == 0)
            {
                return "{}";
            }

            try
            {
                return System.Text.Json.JsonSerializer.Serialize(arguments, AIJsonUtilities.DefaultOptions);
            }
            catch (NotSupportedException)
            {
                return "{}";
            }
        }
    }
}

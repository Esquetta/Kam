using Microsoft.Extensions.AI;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Agents;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// An approval mode as offered in the chat header.
    /// </summary>
    /// <param name="Mode">The mode.</param>
    /// <param name="Label">Short name.</param>
    /// <param name="Description">What runs without asking.</param>
    public sealed record ApprovalModeOption(ApprovalMode Mode, string Label, string Description)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Chat side of the tool-calling agent runtime: streaming replies, tool steps and approval cards.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private const int MaxSavedThreadsShown = 40;
        private const string CompactedNotice = "Earlier messages were summarized to save context.";

        private IAgentRuntime? _agentRuntime;
        private IToolPermissionService? _toolPermissions;
        private IAgentSessionStore? _agentSessionStore;
        private CancellationTokenSource? _agentTurnCancellation;
        private bool _isAgentTurnRunning;
        private ApprovalModeOption _selectedApprovalMode = ApprovalModes[0];

        /// <summary>
        /// Gets the approval modes the user can pick from.
        /// </summary>
        public static IReadOnlyList<ApprovalModeOption> ApprovalModes { get; } =
        [
            new(ApprovalMode.Ask, "Ask first", "Reading and web lookups run on their own. Edits, programs and messages ask first."),
            new(ApprovalMode.AutoEdit, "Auto-edit", "File and clipboard edits run on their own. Programs and messages ask first."),
            new(ApprovalMode.FullAuto, "Full auto", "Every tool runs without asking.")
        ];

        public ICommand ApproveToolCallCommand { get; }

        public ICommand AlwaysAllowToolCallCommand { get; }

        public ICommand DenyToolCallCommand { get; }

        public ICommand StopAgentTurnCommand { get; }

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
            }
        }

        public bool IsSendVisible => !IsAgentTurnRunning;

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
            IAgentSessionStore sessionStore)
        {
            _agentRuntime = agentRuntime;
            _toolPermissions = toolPermissions;
            _agentSessionStore = sessionStore;
            _selectedApprovalMode = ApprovalModes.FirstOrDefault(option => option.Mode == toolPermissions.Mode) ?? ApprovalModes[0];
            this.RaisePropertyChanged(nameof(SelectedApprovalMode));
            this.RaisePropertyChanged(nameof(IsAgentRuntimeEnabled));
            _ = LoadSavedAgentSessionsAsync();
        }

        /// <summary>
        /// Runs one agent turn in a thread and renders its events as they arrive.
        /// </summary>
        public async Task RunAgentTurnAsync(AgentChatSessionViewModel session, string message)
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

            try
            {
                await foreach (var agentEvent in _agentRuntime.RunTurnAsync(session.SessionId, message, cancellation.Token))
                {
                    switch (agentEvent)
                    {
                        case AgentTextDelta delta:
                            if (streaming is null)
                            {
                                streaming = AgentChatMessageViewModel.Agent(string.Empty);
                                streaming.IsStreaming = true;
                                session.AddMessage(streaming);
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
                            }

                            AddLog($"AGENT_TURN_DONE: {done.ToolCallCount} tool calls, {done.InputTokens + done.OutputTokens} tokens");
                            break;

                        case AgentTurnFailed failed:
                            FinishStreaming(session, ref streaming);
                            session.AddMessage(AgentChatMessageViewModel.System(
                                failed.Message == "Stopped." ? "Stopped." : $"Error: {failed.Message}"));
                            AddLog($"AGENT_TURN_FAILED: {failed.Message}");
                            break;
                    }

                    session.RelativeTimeText = "now";
                    RaiseAgentChatStateChanged();
                }
            }
            catch (Exception ex)
            {
                FinishStreaming(session, ref streaming);
                session.AddMessage(AgentChatMessageViewModel.System($"Error: {ex.Message}"));
                AddLog($"AGENT_TURN_FAILED: {ex.Message}");
            }
            finally
            {
                FinishStreaming(session, ref streaming);
                foreach (var step in steps.Values)
                {
                    step.Abandon();
                }

                _agentTurnCancellation = null;
                session.IsRunning = false;
                IsAgentTurnRunning = false;
                RaiseAgentChatStateChanged();
            }
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
                foreach (var summary in saved.Take(MaxSavedThreadsShown))
                {
                    if (AgentChatSessions.Any(session => session.SessionId == summary.Id))
                    {
                        continue;
                    }

                    AgentChatSessions.Add(AgentChatSessionViewModel.CreateSaved(
                        summary.Id,
                        summary.Title,
                        FormatRelativeTime(summary.UpdatedAt, DateTimeOffset.Now),
                        summary.MessageCount));
                }

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
                        items.Add(new AgentChatMessageViewModel("You", message.Text, string.Empty));
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
        /// Formats a thread's last activity like "5m", "3h", "Yesterday" or "Oct 2".
        /// </summary>
        public static string FormatRelativeTime(DateTimeOffset value, DateTimeOffset now)
        {
            var elapsed = now - value;
            if (elapsed < TimeSpan.FromMinutes(1))
            {
                return "now";
            }

            if (elapsed < TimeSpan.FromHours(1))
            {
                return $"{(int)elapsed.TotalMinutes}m";
            }

            if (elapsed < TimeSpan.FromHours(24) && value.ToLocalTime().Date == now.ToLocalTime().Date)
            {
                return $"{(int)elapsed.TotalHours}h";
            }

            if (value.ToLocalTime().Date == now.ToLocalTime().Date.AddDays(-1))
            {
                return "Yesterday";
            }

            return value.ToLocalTime().ToString("MMM d", CultureInfo.InvariantCulture);
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
                items.Add(new AgentChatMessageViewModel("Kam", content, string.Empty));
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

using SmartVoiceAgent.Core.Models.Agents;
using System;
using System.Collections.Generic;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// Places the steps of a turn's subagents in the chat: each subagent's steps go into the
    /// <see cref="AgentChatMessageViewModel.Children"/> of the step that started it, marked with its type, so the
    /// thread shows one card per subagent. The starting step shows the subagent's step count and current tool,
    /// and opens when one of its steps asks for approval.
    /// </summary>
    public sealed class SubagentTimeline
    {
        private readonly AgentChatSessionViewModel _session;
        private readonly Dictionary<string, AgentChatMessageViewModel> _steps;
        private readonly Dictionary<string, int> _stepCountByRun = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _pendingByRun = new(StringComparer.Ordinal);

        /// <summary>
        /// Creates the timeline for one turn.
        /// </summary>
        /// <param name="session">The chat the turn runs in; it takes a step whose starting step is unknown.</param>
        /// <param name="steps">The turn's steps by call id; subagent steps are added under "runId/callId".</param>
        public SubagentTimeline(AgentChatSessionViewModel session, Dictionary<string, AgentChatMessageViewModel> steps)
        {
            _session = session;
            _steps = steps;
        }

        /// <summary>A subagent step that waits for the user's approval.</summary>
        /// <param name="Step">The step.</param>
        /// <param name="RequestId">The approval request.</param>
        public sealed record PendingApproval(AgentChatMessageViewModel Step, string RequestId);

        /// <summary>Ties the starting step to its run.</summary>
        public void Started(AgentSubagentStarted started)
        {
            _stepCountByRun[started.RunId] = 0;
            if (_steps.TryGetValue(started.CallId, out var taskStep))
            {
                taskStep.AttachSubagentRun(started.RunId);
                taskStep.ShowSubagentProgress(0, null);
            }
        }

        /// <summary>
        /// Shows what a subagent did. Returns the approval to announce when the subagent asks for one.
        /// </summary>
        public PendingApproval? Apply(AgentSubagentActivity activity)
        {
            switch (activity.Inner)
            {
                case AgentToolCallStarted started:
                    var step = AgentChatMessageViewModel.ToolStep(
                        started.CallId,
                        started.ToolName,
                        started.DisplayName,
                        started.ArgumentsJson,
                        started.Risk);
                    step.MarkSubagentStep(activity.AgentType);
                    _steps[Key(activity.RunId, started.CallId)] = step;
                    var count = _stepCountByRun[activity.RunId] = _stepCountByRun.GetValueOrDefault(activity.RunId) + 1;
                    if (TaskStep(activity) is { } taskStep)
                    {
                        taskStep.Children.Add(step);
                        taskStep.ShowSubagentProgress(count, started.DisplayName);
                    }
                    else
                    {
                        _session.AddMessage(step);
                    }

                    return null;

                case AgentApprovalRequested approval when _steps.TryGetValue(Key(activity.RunId, approval.CallId), out var waiting):
                    _pendingByRun.TryAdd(activity.RunId, []);
                    _pendingByRun[activity.RunId].Add(approval.RequestId);
                    waiting.RequestApproval(approval.RequestId, approval.AlwaysAllowRule);
                    return new PendingApproval(waiting, approval.RequestId);

                case AgentToolCallCompleted completed when _steps.TryGetValue(Key(activity.RunId, completed.CallId), out var finished):
                    finished.CompleteTool(completed.Success, completed.Summary);
                    TaskStep(activity)?.ShowSubagentProgress(_stepCountByRun.GetValueOrDefault(activity.RunId), null);
                    return null;

                default:
                    return null;
            }
        }

        /// <summary>
        /// Shows the subagent's final step count on the starting step and ends its steps that never finished, such as
        /// a step still waiting for approval when the subagent was stopped. Returns the approval requests it asked for,
        /// so their notices can be dismissed.
        /// </summary>
        public IReadOnlyList<string> Completed(AgentSubagentCompleted completed)
        {
            if (_steps.TryGetValue(completed.CallId, out var taskStep))
            {
                taskStep.ShowSubagentProgress(completed.ToolCallCount, null);
            }

            var prefix = completed.RunId + "/";
            foreach (var (key, step) in _steps)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    step.Abandon();
                }
            }

            return _pendingByRun.Remove(completed.RunId, out var requests) ? requests : [];
        }

        private AgentChatMessageViewModel? TaskStep(AgentSubagentActivity activity) =>
            _steps.TryGetValue(activity.CallId, out var step) ? step : null;

        private static string Key(string runId, string callId) => runId + "/" + callId;
    }
}

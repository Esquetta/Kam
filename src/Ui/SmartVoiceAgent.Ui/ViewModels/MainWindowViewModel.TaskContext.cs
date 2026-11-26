using ReactiveUI;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.Services;
using System;
using System.ComponentModel;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// The context meter in the composer bar: how full the selected task's context window is,
    /// how often it was compacted, and a way to compact it now.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private int _contextTokenBudget = new AgentRuntimeOptions().ContextTokenBudget;
        private AgentChatSessionViewModel? _contextWatchedSession;

        /// <summary>Summarizes the selected task's older messages now, the same as typing /compact.</summary>
        public ICommand CompactAgentChatCommand { get; private set; } = null!;

        /// <summary>Gets whether the selected task has a measured context size.</summary>
        public bool HasSelectedContextTokens => SelectedAgentChatSession?.ContextTokens > 0;

        /// <summary>Gets the selected task's context size against the budget, such as "12.4k of 64k tokens".</summary>
        public string SelectedContextText
        {
            get
            {
                var tokens = SelectedAgentChatSession?.ContextTokens ?? 0;
                if (tokens <= 0)
                {
                    return Loc.Get("Activity.Task.ContextUnknown");
                }

                return _contextTokenBudget > 0
                    ? Loc.Format("Activity.Task.ContextUsage", FormatTokenCount(tokens), FormatTokenCount(_contextTokenBudget))
                    : Loc.Format("Activity.Task.ContextUsageNoBudget", FormatTokenCount(tokens));
            }
        }

        /// <summary>Gets how full the context window is, 0 to 100, for the meter.</summary>
        public double SelectedContextPercent => _contextTokenBudget > 0 && SelectedAgentChatSession is { ContextTokens: > 0 } session
            ? Math.Min(100, session.ContextTokens * 100.0 / _contextTokenBudget)
            : 0;

        /// <summary>Gets the meter's label, such as "42%" ("%42" in Turkish).</summary>
        public string SelectedContextPercentText =>
            Loc.Format("Activity.Task.ContextPercent", Math.Round(SelectedContextPercent).ToString("0", LocalizationService.Instance.Culture));

        /// <summary>Gets whether the composer bar shows the meter: only once the context passes half the budget.</summary>
        public bool ShowsContextMeter => HasSelectedContextTokens && SelectedContextPercent > 50;

        /// <summary>Gets whether the context is close to the budget, so the meter warns.</summary>
        public bool IsSelectedContextNearBudget => SelectedContextPercent >= 80;

        /// <summary>Gets how often the selected task was compacted since Kam started.</summary>
        public string SelectedCompactionText => (SelectedAgentChatSession?.CompactionCount ?? 0) switch
        {
            0 => Loc.Get("Activity.Task.NotCompacted"),
            1 => Loc.Get("Activity.Task.CompactedOnce"),
            var count => Loc.Format("Activity.Task.CompactedMany", count)
        };

        /// <summary>Gets whether "Compact now" can run: the agent is on and the task has messages.</summary>
        public bool CanCompactSelectedTask => _agentRuntime is not null && HasAgentChatMessages;

        /// <summary>
        /// Sets the context budget the runtime compacts at (AgentRuntime:ContextTokenBudget); 0 means off.
        /// </summary>
        /// <param name="budget">The budget in tokens.</param>
        public void SetContextTokenBudget(int budget)
        {
            _contextTokenBudget = Math.Max(0, budget);
            RaiseSelectedContext();
        }

        /// <summary>
        /// Formats a token count briefly in the interface culture: "850", "12.4k", "128k".
        /// </summary>
        /// <param name="tokens">The token count.</param>
        public static string FormatTokenCount(long tokens)
        {
            var culture = LocalizationService.Instance.Culture;
            return tokens < 1000
                ? tokens.ToString("N0", culture)
                : (tokens / 1000.0).ToString(tokens < 100_000 ? "0.#" : "0", culture) + "k";
        }

        private void InitializeTaskContext()
        {
            CompactAgentChatCommand = ReactiveCommand.Create(CompactSelectedTask);
        }

        private void CompactSelectedTask()
        {
            if (!CanCompactSelectedTask || SelectedAgentChatSession is not { } session)
            {
                return;
            }

            // While a turn runs, /compact waits for it like a typed /compact does.
            if (!TryQueueAgentInput("/compact"))
            {
                _ = RunAgentTurnAsync(session, "/compact");
            }
        }

        /// <summary>
        /// Follows the selected task so the meter updates when its context size changes.
        /// </summary>
        private void WatchSelectedSessionContext(AgentChatSessionViewModel? session)
        {
            if (_contextWatchedSession is not null)
            {
                _contextWatchedSession.PropertyChanged -= OnWatchedSessionContextChanged;
            }

            _contextWatchedSession = session;
            if (session is not null)
            {
                session.PropertyChanged += OnWatchedSessionContextChanged;
            }

            RaiseSelectedContext();
        }

        private void OnWatchedSessionContextChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AgentChatSessionViewModel.ContextTokens)
                or nameof(AgentChatSessionViewModel.CompactionCount)
                or nameof(AgentChatSessionViewModel.MessageCountText))
            {
                RaiseSelectedContext();
            }
        }

        /// <summary>Raises the meter's properties; also after the interface language changes.</summary>
        private void RaiseSelectedContext()
        {
            this.RaisePropertyChanged(nameof(HasSelectedContextTokens));
            this.RaisePropertyChanged(nameof(SelectedContextText));
            this.RaisePropertyChanged(nameof(SelectedContextPercent));
            this.RaisePropertyChanged(nameof(SelectedContextPercentText));
            this.RaisePropertyChanged(nameof(ShowsContextMeter));
            this.RaisePropertyChanged(nameof(IsSelectedContextNearBudget));
            this.RaisePropertyChanged(nameof(SelectedCompactionText));
            this.RaisePropertyChanged(nameof(CanCompactSelectedTask));
        }
    }
}

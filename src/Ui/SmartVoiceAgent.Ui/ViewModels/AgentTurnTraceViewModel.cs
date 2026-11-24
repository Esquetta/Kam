using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// How an agent turn ended, as the Activity panel shows it.
    /// </summary>
    public enum AgentTurnOutcome
    {
        /// <summary>The turn is still running.</summary>
        Running,

        /// <summary>The agent answered.</summary>
        Done,

        /// <summary>The turn ended with an error.</summary>
        Failed,

        /// <summary>The user stopped the turn.</summary>
        Stopped
    }

    /// <summary>
    /// The steps of one agent turn for the Activity panel: each tool call with its outcome and time,
    /// and, once the turn ends, the total time and tokens.
    /// </summary>
    public sealed class AgentTurnTraceViewModel : ReactiveObject
    {
        private readonly long _startedTimestamp = Stopwatch.GetTimestamp();
        private AgentTurnOutcome _outcome = AgentTurnOutcome.Running;
        private string _summaryText = string.Empty;

        /// <summary>
        /// Starts a trace for a turn.
        /// </summary>
        /// <param name="request">What the user asked, shown as the trace's title.</param>
        public AgentTurnTraceViewModel(string request)
        {
            Request = (request ?? string.Empty).ReplaceLineEndings(" ").Trim();
            Steps.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasSteps));
        }

        /// <summary>Gets what the user asked.</summary>
        public string Request { get; }

        /// <summary>Gets the tool calls of the turn, in order; the same items as the chat's tool steps.</summary>
        public ObservableCollection<AgentChatMessageViewModel> Steps { get; } = new();

        /// <summary>Gets whether the turn called any tool.</summary>
        public bool HasSteps => Steps.Count > 0;

        /// <summary>Gets how the turn ended, or <see cref="AgentTurnOutcome.Running"/>.</summary>
        public AgentTurnOutcome Outcome
        {
            get => _outcome;
            private set
            {
                this.RaiseAndSetIfChanged(ref _outcome, value);
                this.RaisePropertyChanged(nameof(OutcomeText));
                this.RaisePropertyChanged(nameof(IsRunning));
                this.RaisePropertyChanged(nameof(IsDone));
                this.RaisePropertyChanged(nameof(IsFailed));
            }
        }

        /// <summary>Gets whether the turn is still running.</summary>
        public bool IsRunning => Outcome == AgentTurnOutcome.Running;

        /// <summary>Gets whether the agent answered.</summary>
        public bool IsDone => Outcome == AgentTurnOutcome.Done;

        /// <summary>Gets whether the turn failed or was stopped.</summary>
        public bool IsFailed => Outcome is AgentTurnOutcome.Failed or AgentTurnOutcome.Stopped;

        /// <summary>Gets the outcome in words: working, done, failed or stopped.</summary>
        public string OutcomeText => Loc.Get($"Activity.Trace.Outcome.{Outcome}");

        /// <summary>Gets the totals line, such as "3 tool calls · 4.2 s · 9,120 tokens in, 310 out".</summary>
        public string SummaryText
        {
            get => _summaryText;
            private set => this.RaiseAndSetIfChanged(ref _summaryText, value);
        }

        /// <summary>
        /// Adds a tool step as it starts.
        /// </summary>
        /// <param name="step">The chat's tool step for the call.</param>
        public void AddStep(AgentChatMessageViewModel step)
        {
            Steps.Add(step);
        }

        /// <summary>
        /// Ends the trace after the agent answered.
        /// </summary>
        /// <param name="toolCallCount">Tool calls in the turn.</param>
        /// <param name="inputTokens">Input tokens over all model calls.</param>
        /// <param name="outputTokens">Output tokens over all model calls.</param>
        public void Complete(int toolCallCount, long inputTokens, long outputTokens)
        {
            var culture = LocalizationService.Instance.Culture;
            SummaryText = Loc.Format(
                "Activity.Trace.Summary",
                toolCallCount,
                FormatDuration(Stopwatch.GetElapsedTime(_startedTimestamp)),
                inputTokens.ToString("N0", culture),
                outputTokens.ToString("N0", culture));
            Outcome = AgentTurnOutcome.Done;
        }

        /// <summary>
        /// Ends the trace without an answer.
        /// </summary>
        /// <param name="stopped">Whether the user stopped the turn, rather than an error ending it.</param>
        public void End(bool stopped)
        {
            if (!IsRunning)
            {
                return;
            }

            SummaryText = Loc.Format(
                "Activity.Trace.SummaryNoTokens",
                Steps.Count,
                FormatDuration(Stopwatch.GetElapsedTime(_startedTimestamp)));
            Outcome = stopped ? AgentTurnOutcome.Stopped : AgentTurnOutcome.Failed;
        }

        /// <summary>
        /// Formats a duration as "850 ms", "4.2 s" or "2 min 5 s" in the interface language.
        /// </summary>
        /// <param name="duration">The duration.</param>
        public static string FormatDuration(TimeSpan duration)
        {
            var culture = LocalizationService.Instance.Culture;
            if (duration.TotalSeconds < 1)
            {
                return Loc.Format("Activity.Duration.Milliseconds", Math.Max(1, (int)duration.TotalMilliseconds));
            }

            if (duration.TotalMinutes < 1)
            {
                return Loc.Format("Activity.Duration.Seconds", duration.TotalSeconds.ToString(duration.TotalSeconds < 10 ? "0.0" : "0", culture));
            }

            return Loc.Format("Activity.Duration.Minutes", (int)duration.TotalMinutes, duration.Seconds);
        }
    }
}

using ReactiveUI;
using SmartVoiceAgent.Core.Models.Agents;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Globalization;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// The Loops page: the limits every agent turn runs under. Values are saved as settings and apply to the
    /// next message without a restart. Typed text is clamped to the allowed range; text that is not a number
    /// goes back to the saved value.
    /// </summary>
    public sealed class LoopsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settings;
        private string _maxIterationsText = string.Empty;
        private string _maxToolResultCharactersText = string.Empty;
        private string _contextTokenBudgetText = string.Empty;

        /// <summary>
        /// Creates the page model from the saved settings.
        /// </summary>
        /// <param name="settings">Stores the loop limits.</param>
        public LoopsViewModel(ISettingsService settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Title = Loc.Get("Loops.Title");
            ResetCommand = ReactiveCommand.Create(ResetToDefaults);
            Refresh();
        }

        /// <summary>Gets what a loop is, in one sentence.</summary>
        public string Explanation => Loc.Get("Loops.Subtitle");

        /// <summary>Gets or sets the step limit per turn (1-200).</summary>
        public string MaxIterationsText
        {
            get => _maxIterationsText;
            set => Commit(
                value,
                AgentRuntimeOptions.ClampMaxIterations,
                limit => _settings.AgentMaxIterations = limit,
                () => _settings.AgentMaxIterations,
                ref _maxIterationsText,
                nameof(MaxIterationsText));
        }

        /// <summary>Gets or sets the longest tool result sent back to the model, in characters (1000-200000).</summary>
        public string MaxToolResultCharactersText
        {
            get => _maxToolResultCharactersText;
            set => Commit(
                value,
                AgentRuntimeOptions.ClampMaxToolResultCharacters,
                limit => _settings.AgentMaxToolResultCharacters = limit,
                () => _settings.AgentMaxToolResultCharacters,
                ref _maxToolResultCharactersText,
                nameof(MaxToolResultCharactersText));
        }

        /// <summary>Gets or sets the context budget before compaction, in tokens (0 = off, otherwise 8000-1000000).</summary>
        public string ContextTokenBudgetText
        {
            get => _contextTokenBudgetText;
            set => Commit(
                value,
                AgentRuntimeOptions.ClampContextTokenBudget,
                limit => _settings.AgentContextTokenBudget = limit,
                () => _settings.AgentContextTokenBudget,
                ref _contextTokenBudgetText,
                nameof(ContextTokenBudgetText));
        }

        /// <summary>Gets the command that puts all three limits back to their defaults.</summary>
        public ICommand ResetCommand { get; }

        /// <inheritdoc />
        public override void OnNavigatedTo() => Refresh();

        private void ResetToDefaults()
        {
            var defaults = new AgentRuntimeOptions();
            _settings.AgentMaxIterations = defaults.MaxIterations;
            _settings.AgentMaxToolResultCharacters = defaults.MaxToolResultCharacters;
            _settings.AgentContextTokenBudget = defaults.ContextTokenBudget;
            Refresh();
        }

        private void Refresh()
        {
            this.RaiseAndSetIfChanged(ref _maxIterationsText, Format(_settings.AgentMaxIterations), nameof(MaxIterationsText));
            this.RaiseAndSetIfChanged(ref _maxToolResultCharactersText, Format(_settings.AgentMaxToolResultCharacters), nameof(MaxToolResultCharactersText));
            this.RaiseAndSetIfChanged(ref _contextTokenBudgetText, Format(_settings.AgentContextTokenBudget), nameof(ContextTokenBudgetText));
        }

        private void Commit(
            string? input,
            Func<int, int> clamp,
            Action<int> save,
            Func<int> read,
            ref string field,
            string propertyName)
        {
            if (int.TryParse(input?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var number))
            {
                save(clamp(number));
            }

            // Always show the saved value, so clamped or invalid text is replaced even when the field is unchanged.
            field = Format(read());
            this.RaisePropertyChanged(propertyName);
        }

        private static string Format(int value) => value.ToString(CultureInfo.CurrentCulture);
    }
}

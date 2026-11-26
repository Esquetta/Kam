using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// Something that works today and is a step toward a planned Build page, with a link to where it lives.
    /// </summary>
    /// <param name="Text">What works today.</param>
    /// <param name="LinkText">The page the link opens.</param>
    /// <param name="Open">Opens that page.</param>
    public sealed record PlannedStepLink(string Text, string LinkText, ICommand Open);

    /// <summary>
    /// A Build ladder step that Kam has no runtime for yet (Agent Builder). The page says what the
    /// step will do, what already works toward it and where, and what is still missing. It never shows sample data.
    /// </summary>
    public sealed class PlannedStepViewModel : ViewModelBase
    {
        private readonly string _key;
        private readonly Action<NavView> _navigate;

        /// <summary>
        /// Creates the page for <paramref name="view"/>.
        /// </summary>
        /// <param name="view">Builder.</param>
        /// <param name="navigate">Opens another page.</param>
        public PlannedStepViewModel(NavView view, Action<NavView> navigate)
        {
            if (view is not NavView.Builder)
            {
                throw new ArgumentOutOfRangeException(nameof(view), view, "Only Builder is a planned step.");
            }

            View = view;
            _key = "Builder";
            _navigate = navigate;
            Refresh();
            LocalizationService.Instance.LanguageChanged += (_, _) => Refresh();
        }

        /// <summary>Gets the page.</summary>
        public NavView View { get; }

        /// <summary>Gets the step number on the Build ladder.</summary>
        public int Step => 5;

        /// <summary>Gets the overline, such as "BUILD · STEP 4".</summary>
        public string Overline => Loc.Format("Build.Overline", Step);

        /// <summary>Gets the page title.</summary>
        public string PageTitle => Loc.Get($"Build.{_key}.Title");

        /// <summary>Gets what the step is for.</summary>
        public string Explanation => Loc.Get($"Build.{_key}.Explain");

        /// <summary>Gets what already works toward this step, each with a link to its page.</summary>
        public IReadOnlyList<PlannedStepLink> Today { get; private set; } = [];

        /// <summary>Gets what is still missing.</summary>
        public IReadOnlyList<string> Coming { get; private set; } = [];

        /// <summary>Re-reads the text after the interface language changed.</summary>
        public void Refresh()
        {
            Today =
            [
                Link("Build.Builder.Today.Prompts", NavView.Prompts),
                Link("Build.Builder.Today.Skills", NavView.Plugins),
                Link("Build.Builder.Today.Loops", NavView.Loops)
            ];

            Coming =
            [
                Loc.Get($"Build.{_key}.Coming.1"),
                Loc.Get($"Build.{_key}.Coming.2"),
                Loc.Get($"Build.{_key}.Coming.3"),
                Loc.Get($"Build.{_key}.Coming.4")
            ];

            this.RaisePropertyChanged(nameof(Overline));
            this.RaisePropertyChanged(nameof(PageTitle));
            this.RaisePropertyChanged(nameof(Explanation));
            this.RaisePropertyChanged(nameof(Today));
            this.RaisePropertyChanged(nameof(Coming));
        }

        /// <inheritdoc/>
        public override void OnNavigatedTo() => Refresh();

        private PlannedStepLink Link(string key, NavView target) =>
            new(Loc.Get(key), Loc.Get($"Build.Link.{target}"), ReactiveCommand.Create(() => _navigate(target)));
    }
}

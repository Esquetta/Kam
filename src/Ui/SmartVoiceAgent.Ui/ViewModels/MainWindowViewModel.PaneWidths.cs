using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// The widths the user dragged the rail and the Activity panel to. They are read from settings at start, limited
    /// while dragging and saved when a drag ends.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private double _railWidth = PaneWidths.RailDefault;
        private double _activityWidth = PaneWidths.ActivityDefault;

        /// <summary>Gets or sets the rail width when it shows labels; the compact rail stays 60 px.</summary>
        public double RailWidth
        {
            get => _railWidth;
            set => SetPaneWidth(ref _railWidth, value, PaneWidths.ClampRail(value), nameof(RailWidth));
        }

        /// <summary>Gets or sets the Activity panel width while it is shown.</summary>
        public double ActivityWidth
        {
            get => _activityWidth;
            set
            {
                SetPaneWidth(ref _activityWidth, value, PaneWidths.ClampActivity(value), nameof(ActivityWidth));
                this.RaisePropertyChanged(nameof(ActivityPanelWidth));
            }
        }

        /// <summary>Gets the rail width a double click on its edge restores.</summary>
        public double RailDefaultWidth => PaneWidths.RailDefault;

        /// <summary>Gets the Activity panel width a double click on its edge restores.</summary>
        public double ActivityDefaultWidth => PaneWidths.ActivityDefault;

        /// <summary>Saves both widths; the resize handles run it when a drag ends.</summary>
        public ICommand SavePaneWidthsCommand { get; private set; } = null!;

        private void InitializePaneWidths()
        {
            _railWidth = PaneWidths.ClampRail(_pageSettingsService.RailWidth);
            _activityWidth = PaneWidths.ClampActivity(_pageSettingsService.ActivityPanelWidth);
            SavePaneWidthsCommand = ReactiveCommand.Create(SavePaneWidths);
        }

        private void SavePaneWidths()
        {
            _pageSettingsService.RailWidth = RailWidth;
            _pageSettingsService.ActivityPanelWidth = ActivityWidth;
        }

        /// <summary>
        /// Stores the limited width and always reports a change when the requested width was outside the limits,
        /// so a handle dragged past a limit snaps back to it.
        /// </summary>
        private void SetPaneWidth(ref double field, double requested, double limited, string propertyName)
        {
            if (field == limited && requested == limited)
            {
                return;
            }

            field = limited;
            this.RaisePropertyChanged(propertyName);
        }
    }
}

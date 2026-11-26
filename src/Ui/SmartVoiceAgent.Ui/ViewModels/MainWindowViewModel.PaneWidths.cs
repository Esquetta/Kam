using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// The width the user dragged the rail to. It is read from settings at start, limited while dragging and saved
    /// when a drag ends.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private double _railWidth = PaneWidths.RailDefault;

        /// <summary>Gets or sets the rail width when it shows labels; the compact rail stays 60 px.</summary>
        public double RailWidth
        {
            get => _railWidth;
            set
            {
                var limited = PaneWidths.ClampRail(value);

                // A width outside the limits is reported too, so a handle dragged past a limit snaps back to it.
                if (_railWidth == limited && value == limited)
                {
                    return;
                }

                _railWidth = limited;
                this.RaisePropertyChanged(nameof(RailWidth));
            }
        }

        /// <summary>Gets the rail width a double click on its edge restores.</summary>
        public double RailDefaultWidth => PaneWidths.RailDefault;

        /// <summary>Saves the rail width; the resize handle runs it when a drag ends.</summary>
        public ICommand SavePaneWidthsCommand { get; private set; } = null!;

        private void InitializePaneWidths()
        {
            _railWidth = PaneWidths.ClampRail(_pageSettingsService.RailWidth);
            SavePaneWidthsCommand = ReactiveCommand.Create(() => { _pageSettingsService.RailWidth = RailWidth; });
        }
    }
}

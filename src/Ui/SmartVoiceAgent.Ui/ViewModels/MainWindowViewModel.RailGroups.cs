using ReactiveUI;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// The rail's collapsible Build and Manage groups. A click on a group's label toggles it and saves the choice;
    /// until then a group is collapsed only while the window is short.
    /// </summary>
    public partial class MainWindowViewModel
    {
        /// <summary>The <see cref="ToggleRailGroupCommand"/> parameter for the Build group.</summary>
        public const string BuildRailGroup = "Build";

        /// <summary>The <see cref="ToggleRailGroupCommand"/> parameter for the Manage group.</summary>
        public const string ManageRailGroup = "Manage";

        private bool _isWindowShort;

        /// <summary>
        /// Gets or sets whether the window is short (below 760 px); the window sets it from
        /// <see cref="Services.WindowStateManager.IsShort"/>.
        /// </summary>
        public bool IsWindowShort
        {
            get => _isWindowShort;
            set
            {
                if (_isWindowShort == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _isWindowShort, value);
                this.RaisePropertyChanged(nameof(IsBuildGroupCollapsed));
                this.RaisePropertyChanged(nameof(IsManageGroupCollapsed));
            }
        }

        /// <summary>Gets whether the rail's Build group is collapsed.</summary>
        public bool IsBuildGroupCollapsed => _pageSettingsService.RailBuildCollapsed ?? _isWindowShort;

        /// <summary>Gets whether the rail's Manage group is collapsed.</summary>
        public bool IsManageGroupCollapsed => _pageSettingsService.RailManageCollapsed ?? _isWindowShort;

        /// <summary>
        /// Collapses or opens the rail group named by the parameter (<see cref="BuildRailGroup"/> or
        /// <see cref="ManageRailGroup"/>) and saves the choice.
        /// </summary>
        public ICommand ToggleRailGroupCommand { get; private set; } = null!;

        private void InitializeRailGroups()
        {
            ToggleRailGroupCommand = ReactiveCommand.Create<string?>(ToggleRailGroup);
        }

        private void ToggleRailGroup(string? group)
        {
            switch (group)
            {
                case BuildRailGroup:
                    _pageSettingsService.RailBuildCollapsed = !IsBuildGroupCollapsed;
                    this.RaisePropertyChanged(nameof(IsBuildGroupCollapsed));
                    break;
                case ManageRailGroup:
                    _pageSettingsService.RailManageCollapsed = !IsManageGroupCollapsed;
                    this.RaisePropertyChanged(nameof(IsManageGroupCollapsed));
                    break;
            }
        }
    }
}

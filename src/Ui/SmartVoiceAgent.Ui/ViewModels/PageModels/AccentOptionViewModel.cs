using Avalonia;
using Avalonia.Media;
using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// One accent color swatch in Settings > Appearance.
    /// </summary>
    public sealed class AccentOptionViewModel : ReactiveObject
    {
        private bool _isSelected;

        /// <summary>
        /// Creates the swatch for <paramref name="preset"/>.
        /// </summary>
        /// <param name="preset">The accent preset.</param>
        /// <param name="select">Called when the swatch is clicked.</param>
        public AccentOptionViewModel(AccentPreset preset, Action<AccentOptionViewModel> select)
        {
            Preset = preset;
            var swatch = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
            };
            swatch.GradientStops.Add(new GradientStop(preset.BrandStart, 0));
            swatch.GradientStops.Add(new GradientStop(preset.BrandEnd, 1));
            Swatch = swatch;
            SelectCommand = ReactiveCommand.Create(() => select(this));
        }

        /// <summary>Gets the preset.</summary>
        public AccentPreset Preset { get; }

        /// <summary>Gets the preset's name in the interface language.</summary>
        public string Name => Preset.Name;

        /// <summary>Gets the brush the swatch is painted with, the same gradient as the brand mark.</summary>
        public IBrush Swatch { get; }

        /// <summary>Picks this accent.</summary>
        public ICommand SelectCommand { get; }

        /// <summary>Gets or sets whether this is the accent in use.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        /// <summary>Re-reads the name after the interface language changed.</summary>
        public void RefreshLocalizedText() => this.RaisePropertyChanged(nameof(Name));
    }
}

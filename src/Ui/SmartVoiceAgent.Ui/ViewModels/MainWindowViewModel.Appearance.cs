using Avalonia.Threading;
using SmartVoiceAgent.Ui.Services;
using System;

namespace SmartVoiceAgent.Ui.ViewModels
{
    /// <summary>
    /// Theme and accent color: read from settings at start, saved when they change, and applied
    /// when Settings > Appearance changes them.
    /// </summary>
    public partial class MainWindowViewModel
    {
        private void InitializeAppearance()
        {
            _pageSettingsService.SettingChanged += OnAppearanceSettingChanged;
        }

        /// <summary>
        /// Applies the saved theme and accent color. Called once the UI thread runs.
        /// </summary>
        private void ApplySavedAppearance()
        {
            ApplyAccent(_pageSettingsService.AccentColor);
            IsDarkMode = !IsLightTheme(_pageSettingsService.Theme);
        }

        private void OnAppearanceSettingChanged(object? sender, SettingChangedEventArgs e)
        {
            switch (e.SettingName)
            {
                case nameof(ISettingsService.AccentColor):
                    Dispatcher.UIThread.Post(() => ApplyAccent(_pageSettingsService.AccentColor));
                    break;
                case nameof(ISettingsService.Theme):
                    var dark = !IsLightTheme(_pageSettingsService.Theme);
                    if (dark != IsDarkMode)
                    {
                        Dispatcher.UIThread.Post(() => IsDarkMode = dark);
                    }

                    break;
            }
        }

        private void ApplyAccent(string? id)
        {
            if (global::Avalonia.Application.Current is { } app)
            {
                var preset = AccentPalette.Find(id);
                AccentPalette.Apply(app, preset);
                AddLog($"ACCENT_CHANGED: {preset.Id.ToUpperInvariant()}");
            }
        }

        private static bool IsLightTheme(string? theme) =>
            string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
    }
}

using System;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// Service for managing application settings
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Event raised when any setting changes
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// Gets or sets whether the application should start automatically with Windows
    /// </summary>
    bool AutoStart { get; set; }

    /// <summary>
    /// Gets or sets whether the application should start minimized to tray
    /// </summary>
    bool StartMinimized { get; set; }

    /// <summary>
    /// Gets or sets whether to show the main window on startup
    /// </summary>
    bool ShowOnStartup { get; set; }

    /// <summary>
    /// Gets or sets the startup behavior mode
    /// 0 = Normal, 1 = Minimized, 2 = Tray only
    /// </summary>
    int StartupBehavior { get; set; }

    /// <summary>
    /// Saves all settings to persistent storage
    /// </summary>
    void Save();

    /// <summary>
    /// Loads settings from persistent storage
    /// </summary>
    void Load();
}

/// <summary>
/// Event args for setting change notifications
/// </summary>
public class SettingChangedEventArgs : EventArgs
{
    public string SettingName { get; set; } = string.Empty;
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
}

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

    #region Integration Settings

    /// <summary>
    /// Gets or sets the Todoist API key for task management integration
    /// </summary>
    string TodoistApiKey { get; set; }

    #region Email (SMTP) Settings

    /// <summary>
    /// Gets or sets the SMTP server host
    /// </summary>
    string SmtpHost { get; set; }

    /// <summary>
    /// Gets or sets the SMTP server port
    /// </summary>
    int SmtpPort { get; set; }

    /// <summary>
    /// Gets or sets the SMTP username
    /// </summary>
    string SmtpUsername { get; set; }

    /// <summary>
    /// Gets or sets the SMTP password
    /// </summary>
    string SmtpPassword { get; set; }

    /// <summary>
    /// Gets or sets the sender email address
    /// </summary>
    string SenderEmail { get; set; }

    /// <summary>
    /// Gets or sets whether to use SSL/TLS for SMTP
    /// </summary>
    bool SmtpEnableSsl { get; set; }

    /// <summary>
    /// Gets or sets the email provider type (Gmail, Outlook, Yahoo, Custom)
    /// </summary>
    string EmailProvider { get; set; }

    #endregion

    #region SMS (Twilio) Settings

    /// <summary>
    /// Gets or sets the Twilio Account SID
    /// </summary>
    string TwilioAccountSid { get; set; }

    /// <summary>
    /// Gets or sets the Twilio Auth Token
    /// </summary>
    string TwilioAuthToken { get; set; }

    /// <summary>
    /// Gets or sets the Twilio phone number
    /// </summary>
    string TwilioPhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets whether SMS service is enabled
    /// </summary>
    bool SmsEnabled { get; set; }

    #endregion

    #endregion

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

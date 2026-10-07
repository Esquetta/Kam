using System;
using System.Collections.Generic;
using SmartVoiceAgent.Core.Models.AI;

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

    /// <summary>
    /// Gets or sets the GitHub App id used for repository access.
    /// </summary>
    string GitHubAppId { get; set; }

    /// <summary>
    /// Gets or sets the GitHub App installation id used for repository access.
    /// </summary>
    string GitHubAppInstallationId { get; set; }

    /// <summary>
    /// Gets or sets the local PEM file path for the GitHub App private key.
    /// </summary>
    string GitHubAppPrivateKeyPath { get; set; }

    /// <summary>
    /// Gets or sets the Google Custom Search API key the web search tool uses.
    /// </summary>
    string WebSearchApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Google Programmable Search engine ID the web search tool uses.
    /// </summary>
    string WebSearchEngineId { get; set; }

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

    #region Voice Settings

    /// <summary>
    /// Gets or sets the selected input (microphone) device ID
    /// </summary>
    string SelectedInputDeviceId { get; set; }

    /// <summary>
    /// Gets or sets the selected output (headset/speaker) device ID
    /// </summary>
    string SelectedOutputDeviceId { get; set; }

    /// <summary>
    /// Gets or sets the input volume level (0.0 to 1.0)
    /// </summary>
    float InputVolume { get; set; }

    /// <summary>
    /// Gets or sets the output volume level (0.0 to 1.0)
    /// </summary>
    float OutputVolume { get; set; }

    /// <summary>
    /// Gets or sets the spoken language: empty follows the interface language, <c>auto</c> detects it,
    /// otherwise a two-letter code such as <c>tr</c>.
    /// </summary>
    string VoiceLanguage { get; set; }

    /// <summary>
    /// Gets or sets where speech is turned into text: <c>Local</c> (Whisper on this computer) or <c>OpenAI</c>
    /// (an OpenAI-compatible transcription API).
    /// </summary>
    string SpeechEngine { get; set; }

    /// <summary>
    /// Gets or sets the local Whisper model: <c>base</c>, <c>small</c> or <c>large-v3-turbo</c>.
    /// </summary>
    string LocalSpeechModel { get; set; }

    /// <summary>
    /// Gets or sets the base URL of the OpenAI-compatible transcription API.
    /// </summary>
    string SpeechApiEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the transcription model of the OpenAI-compatible API.
    /// </summary>
    string SpeechApiModel { get; set; }

    /// <summary>
    /// Gets or sets the API key of the OpenAI-compatible transcription API. Stored in the secret store.
    /// </summary>
    string SpeechApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether Kam listens for the wake word.
    /// </summary>
    bool WakeWordEnabled { get; set; }

    /// <summary>
    /// Gets or sets the wake phrase, such as <c>Hey Kam</c>.
    /// </summary>
    string WakeWord { get; set; }

    /// <summary>
    /// Gets or sets the system-wide shortcut that starts and stops talking, such as <c>Ctrl+Alt+Space</c>.
    /// </summary>
    string TalkShortcut { get; set; }

    /// <summary>
    /// Gets or sets which replies are read aloud: <c>Off</c>, <c>Voice</c> (replies to voice commands) or <c>All</c>.
    /// </summary>
    string SpokenReplies { get; set; }

    /// <summary>
    /// Gets or sets the voice replies are read in; empty picks one for the spoken language.
    /// </summary>
    string SpeechVoice { get; set; }

    /// <summary>
    /// Gets or sets the speaking rate from -5 (slow) to 5 (fast).
    /// </summary>
    int SpeechRate { get; set; }

    #endregion

    #region Interface Settings

    /// <summary>
    /// Gets or sets the interface language as a culture name such as <c>tr-TR</c>; empty follows the system.
    /// </summary>
    string Language { get; set; }

    #endregion

    #region AI Runtime Settings

    /// <summary>
    /// Gets or sets model provider profiles available to Kam runtime.
    /// </summary>
    IReadOnlyList<ModelProviderProfile> ModelProviderProfiles { get; set; }

    /// <summary>
    /// Gets or sets the active planner model provider profile id.
    /// </summary>
    string ActivePlannerProfileId { get; set; }

    /// <summary>
    /// Gets or sets the active chat and skill execution model provider profile id.
    /// </summary>
    string ActiveChatProfileId { get; set; }

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

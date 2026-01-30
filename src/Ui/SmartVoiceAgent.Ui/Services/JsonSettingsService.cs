using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// JSON file-based implementation of settings service
/// </summary>
public class JsonSettingsService : ISettingsService, IDisposable
{
    private readonly string _settingsPath;
    private readonly object _lock = new();
    private SettingsData _data = new();
    private FileSystemWatcher? _watcher;

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public JsonSettingsService(string? settingsDirectory = null)
    {
        var directory = settingsDirectory ?? GetDefaultSettingsDirectory();
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
        
        Load();
        SetupFileWatcher();
    }

    #region General Properties

    public bool AutoStart
    {
        get => _data.AutoStart;
        set => SetProperty(nameof(AutoStart), value, v => _data.AutoStart = v);
    }

    public bool StartMinimized
    {
        get => _data.StartMinimized;
        set => SetProperty(nameof(StartMinimized), value, v => _data.StartMinimized = v);
    }

    public bool ShowOnStartup
    {
        get => _data.ShowOnStartup;
        set => SetProperty(nameof(ShowOnStartup), value, v => _data.ShowOnStartup = v);
    }

    public int StartupBehavior
    {
        get => _data.StartupBehavior;
        set => SetProperty(nameof(StartupBehavior), value, v => _data.StartupBehavior = v);
    }

    #endregion

    #region Integration Properties

    public string TodoistApiKey
    {
        get => _data.TodoistApiKey ?? string.Empty;
        set => SetProperty(nameof(TodoistApiKey), value, v => _data.TodoistApiKey = v);
    }

    #region Email (SMTP) Properties

    public string SmtpHost
    {
        get => _data.SmtpHost ?? string.Empty;
        set => SetProperty(nameof(SmtpHost), value, v => _data.SmtpHost = v);
    }

    public int SmtpPort
    {
        get => _data.SmtpPort;
        set => SetProperty(nameof(SmtpPort), value, v => _data.SmtpPort = v);
    }

    public string SmtpUsername
    {
        get => _data.SmtpUsername ?? string.Empty;
        set => SetProperty(nameof(SmtpUsername), value, v => _data.SmtpUsername = v);
    }

    public string SmtpPassword
    {
        get => _data.SmtpPassword ?? string.Empty;
        set => SetProperty(nameof(SmtpPassword), value, v => _data.SmtpPassword = v);
    }

    public string SenderEmail
    {
        get => _data.SenderEmail ?? string.Empty;
        set => SetProperty(nameof(SenderEmail), value, v => _data.SenderEmail = v);
    }

    public bool SmtpEnableSsl
    {
        get => _data.SmtpEnableSsl;
        set => SetProperty(nameof(SmtpEnableSsl), value, v => _data.SmtpEnableSsl = v);
    }

    public string EmailProvider
    {
        get => _data.EmailProvider ?? "Custom";
        set => SetProperty(nameof(EmailProvider), value, v => _data.EmailProvider = v);
    }

    #endregion

    #region SMS (Twilio) Properties

    public string TwilioAccountSid
    {
        get => _data.TwilioAccountSid ?? string.Empty;
        set => SetProperty(nameof(TwilioAccountSid), value, v => _data.TwilioAccountSid = v);
    }

    public string TwilioAuthToken
    {
        get => _data.TwilioAuthToken ?? string.Empty;
        set => SetProperty(nameof(TwilioAuthToken), value, v => _data.TwilioAuthToken = v);
    }

    public string TwilioPhoneNumber
    {
        get => _data.TwilioPhoneNumber ?? string.Empty;
        set => SetProperty(nameof(TwilioPhoneNumber), value, v => _data.TwilioPhoneNumber = v);
    }

    public bool SmsEnabled
    {
        get => _data.SmsEnabled;
        set => SetProperty(nameof(SmsEnabled), value, v => _data.SmsEnabled = v);
    }

    #endregion

    #endregion

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex}");
            }
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var loaded = JsonSerializer.Deserialize<SettingsData>(json);
                    if (loaded != null)
                    {
                        _data = loaded;
                    }
                }
                else
                {
                    // First run - set defaults
                    _data = new SettingsData
                    {
                        ShowOnStartup = true,
                        AutoStart = false,
                        StartMinimized = false,
                        StartupBehavior = 0,
                        SmtpPort = 587,
                        SmtpEnableSsl = true,
                        EmailProvider = "Gmail"
                    };
                    Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex}");
                _data = new SettingsData();
            }
        }
    }

    private void SetProperty<T>(string name, T value, Action<T> setter)
    {
        var oldValue = GetPropertyValue<T>(name);
        if (!Equals(oldValue, value))
        {
            setter(value);
            Save();
            SettingChanged?.Invoke(this, new SettingChangedEventArgs
            {
                SettingName = name,
                OldValue = oldValue,
                NewValue = value
            });
        }
    }

    private T? GetPropertyValue<T>(string name)
    {
        return name switch
        {
            nameof(AutoStart) => (T?)(object?)_data.AutoStart,
            nameof(StartMinimized) => (T?)(object?)_data.StartMinimized,
            nameof(ShowOnStartup) => (T?)(object?)_data.ShowOnStartup,
            nameof(StartupBehavior) => (T?)(object?)_data.StartupBehavior,
            nameof(TodoistApiKey) => (T?)(object?)_data.TodoistApiKey,
            nameof(SmtpHost) => (T?)(object?)_data.SmtpHost,
            nameof(SmtpPort) => (T?)(object?)_data.SmtpPort,
            nameof(SmtpUsername) => (T?)(object?)_data.SmtpUsername,
            nameof(SmtpPassword) => (T?)(object?)_data.SmtpPassword,
            nameof(SenderEmail) => (T?)(object?)_data.SenderEmail,
            nameof(SmtpEnableSsl) => (T?)(object?)_data.SmtpEnableSsl,
            nameof(EmailProvider) => (T?)(object?)_data.EmailProvider,
            nameof(TwilioAccountSid) => (T?)(object?)_data.TwilioAccountSid,
            nameof(TwilioAuthToken) => (T?)(object?)_data.TwilioAuthToken,
            nameof(TwilioPhoneNumber) => (T?)(object?)_data.TwilioPhoneNumber,
            nameof(SmsEnabled) => (T?)(object?)_data.SmsEnabled,
            _ => default
        };
    }

    private static string GetDefaultSettingsDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "SmartVoiceAgent");
    }

    private void SetupFileWatcher()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            var fileName = Path.GetFileName(_settingsPath);
            
            if (directory != null)
            {
                _watcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite
                };
                _watcher.Changed += OnSettingsFileChanged;
                _watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to setup file watcher: {ex}");
        }
    }

    private void OnSettingsFileChanged(object sender, FileSystemEventArgs e)
    {
        // Reload settings if file was modified externally
        Thread.Sleep(100); // Brief delay to let write complete
        Load();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        Save();
    }

    /// <summary>
    /// Data model for settings serialization
    /// </summary>
    private class SettingsData
    {
        // General
        public bool AutoStart { get; set; }
        public bool StartMinimized { get; set; }
        public bool ShowOnStartup { get; set; } = true;
        public int StartupBehavior { get; set; }

        // Integrations
        public string? TodoistApiKey { get; set; }

        // Email (SMTP)
        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public string? SmtpUsername { get; set; }
        public string? SmtpPassword { get; set; }
        public string? SenderEmail { get; set; }
        public bool SmtpEnableSsl { get; set; } = true;
        public string? EmailProvider { get; set; } = "Gmail";

        // SMS (Twilio)
        public string? TwilioAccountSid { get; set; }
        public string? TwilioAuthToken { get; set; }
        public string? TwilioPhoneNumber { get; set; }
        public bool SmsEnabled { get; set; }

        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}

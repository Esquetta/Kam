using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using SmartVoiceAgent.Core.Models.AI;

namespace SmartVoiceAgent.Ui.Services;

/// <summary>
/// JSON file-based implementation of settings service
/// </summary>
public class JsonSettingsService : ISettingsService, IDisposable
{
    private const string ModelProviderProfileSecretPrefix = "ModelProviderProfiles:";

    private readonly string _settingsPath;
    private readonly ISettingsSecretStore _secretStore;
    private readonly object _lock = new();
    private SettingsData _data = new();
    private FileSystemWatcher? _watcher;
    private DateTime _suppressWatcherUntilUtc;
    private string? _lastSavedJson;

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public JsonSettingsService(string? settingsDirectory = null, ISettingsSecretStore? secretStore = null)
    {
        var directory = settingsDirectory ?? GetDefaultSettingsDirectory();
        Directory.CreateDirectory(directory);
        _settingsPath = Path.Combine(directory, "settings.json");
        _secretStore = secretStore ?? new JsonFileSettingsSecretStore(directory);
        
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

    #region Voice Settings

    public string SelectedInputDeviceId
    {
        get => _data.SelectedInputDeviceId ?? string.Empty;
        set => SetProperty(nameof(SelectedInputDeviceId), value, v => _data.SelectedInputDeviceId = v);
    }

    public string SelectedOutputDeviceId
    {
        get => _data.SelectedOutputDeviceId ?? string.Empty;
        set => SetProperty(nameof(SelectedOutputDeviceId), value, v => _data.SelectedOutputDeviceId = v);
    }

    public float InputVolume
    {
        get => _data.InputVolume;
        set => SetProperty(nameof(InputVolume), value, v => _data.InputVolume = v);
    }

    public float OutputVolume
    {
        get => _data.OutputVolume;
        set => SetProperty(nameof(OutputVolume), value, v => _data.OutputVolume = v);
    }

    public bool IsNoiseSuppressionEnabled
    {
        get => _data.IsNoiseSuppressionEnabled;
        set => SetProperty(nameof(IsNoiseSuppressionEnabled), value, v => _data.IsNoiseSuppressionEnabled = v);
    }

    #endregion

    #endregion

    #region AI Runtime Settings

    public IReadOnlyList<ModelProviderProfile> ModelProviderProfiles
    {
        get => _data.ModelProviderProfiles.Select(CloneProfile).ToList().AsReadOnly();
        set => SetProperty(
            nameof(ModelProviderProfiles),
            value,
            v => _data.ModelProviderProfiles = v.Select(CloneProfile).ToList());
    }

    public string ActivePlannerProfileId
    {
        get => _data.ActivePlannerProfileId ?? string.Empty;
        set => SetProperty(nameof(ActivePlannerProfileId), value, v => _data.ActivePlannerProfileId = v);
    }

    public string ActiveChatProfileId
    {
        get => _data.ActiveChatProfileId ?? string.Empty;
        set => SetProperty(nameof(ActiveChatProfileId), value, v => _data.ActiveChatProfileId = v);
    }

    #endregion

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                SaveSecrets();
                var json = JsonSerializer.Serialize(CreateSerializableData(), new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    IgnoreReadOnlyProperties = true,
                    WriteIndented = true
                });
                _suppressWatcherUntilUtc = DateTime.UtcNow.AddSeconds(1);
                File.WriteAllText(_settingsPath, json);
                _lastSavedJson = json;
                _suppressWatcherUntilUtc = DateTime.UtcNow.AddSeconds(1);
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
                        var migrated = MigratePlaintextSecrets(json);
                        LoadSecrets();
                        if (migrated)
                        {
                            Save();
                        }
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
            nameof(ModelProviderProfiles) => (T?)(object?)_data.ModelProviderProfiles.Select(CloneProfile).ToList().AsReadOnly(),
            nameof(ActivePlannerProfileId) => (T?)(object?)_data.ActivePlannerProfileId,
            nameof(ActiveChatProfileId) => (T?)(object?)_data.ActiveChatProfileId,
            _ => default
        };
    }

    private bool MigratePlaintextSecrets(string json)
    {
        var migrated = false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            migrated |= MigratePlaintextSecret(root, nameof(TodoistApiKey), value => _data.TodoistApiKey = value);
            migrated |= MigratePlaintextSecret(root, nameof(SmtpPassword), value => _data.SmtpPassword = value);
            migrated |= MigratePlaintextSecret(root, nameof(TwilioAuthToken), value => _data.TwilioAuthToken = value);

            if (root.TryGetProperty(nameof(SettingsData.ModelProviderProfiles), out var profilesElement)
                && profilesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var profileElement in profilesElement.EnumerateArray())
                {
                    if (!profileElement.TryGetProperty(nameof(ModelProviderProfile.Id), out var idElement)
                        || idElement.ValueKind != JsonValueKind.String
                        || !profileElement.TryGetProperty(nameof(ModelProviderProfile.ApiKey), out var apiKeyElement)
                        || apiKeyElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var id = idElement.GetString();
                    var apiKey = apiKeyElement.GetString();
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrEmpty(apiKey))
                    {
                        continue;
                    }

                    _secretStore.SetSecret(GetModelProviderApiKeySecretName(id), apiKey);
                    migrated = true;
                }
            }
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to inspect settings secrets for migration: {ex}");
        }

        return migrated;
    }

    private bool MigratePlaintextSecret(JsonElement root, string propertyName, Action<string> setValue)
    {
        if (!root.TryGetProperty(propertyName, out var valueElement)
            || valueElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = valueElement.GetString();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        _secretStore.SetSecret(propertyName, value);
        setValue(value);
        return true;
    }

    private void LoadSecrets()
    {
        _data.TodoistApiKey = _secretStore.GetSecret(nameof(TodoistApiKey)) ?? _data.TodoistApiKey;
        _data.SmtpPassword = _secretStore.GetSecret(nameof(SmtpPassword)) ?? _data.SmtpPassword;
        _data.TwilioAuthToken = _secretStore.GetSecret(nameof(TwilioAuthToken)) ?? _data.TwilioAuthToken;

        foreach (var profile in _data.ModelProviderProfiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Id))
            {
                continue;
            }

            profile.ApiKey = _secretStore.GetSecret(GetModelProviderApiKeySecretName(profile.Id)) ?? profile.ApiKey;
        }
    }

    private void SaveSecrets()
    {
        SaveSecret(nameof(TodoistApiKey), _data.TodoistApiKey);
        SaveSecret(nameof(SmtpPassword), _data.SmtpPassword);
        SaveSecret(nameof(TwilioAuthToken), _data.TwilioAuthToken);

        var activeProfileSecretNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in _data.ModelProviderProfiles.Where(p => !string.IsNullOrWhiteSpace(p.Id)))
        {
            var secretName = GetModelProviderApiKeySecretName(profile.Id);
            activeProfileSecretNames.Add(secretName);
            SaveSecret(secretName, profile.ApiKey);
        }

        foreach (var name in _secretStore.GetSecretNames()
                     .Where(name => name.StartsWith(ModelProviderProfileSecretPrefix, StringComparison.Ordinal)
                         && !activeProfileSecretNames.Contains(name)))
        {
            _secretStore.RemoveSecret(name);
        }
    }

    private void SaveSecret(string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            _secretStore.RemoveSecret(name);
            return;
        }

        _secretStore.SetSecret(name, value);
    }

    private SettingsData CreateSerializableData()
    {
        var data = CloneSettingsData(_data);
        data.TodoistApiKey = null;
        data.SmtpPassword = null;
        data.TwilioAuthToken = null;

        foreach (var profile in data.ModelProviderProfiles)
        {
            profile.ApiKey = null!;
        }

        data.LastModified = DateTime.UtcNow;
        return data;
    }

    private static SettingsData CloneSettingsData(SettingsData source)
    {
        return new SettingsData
        {
            AutoStart = source.AutoStart,
            StartMinimized = source.StartMinimized,
            ShowOnStartup = source.ShowOnStartup,
            StartupBehavior = source.StartupBehavior,
            TodoistApiKey = source.TodoistApiKey,
            SmtpHost = source.SmtpHost,
            SmtpPort = source.SmtpPort,
            SmtpUsername = source.SmtpUsername,
            SmtpPassword = source.SmtpPassword,
            SenderEmail = source.SenderEmail,
            SmtpEnableSsl = source.SmtpEnableSsl,
            EmailProvider = source.EmailProvider,
            TwilioAccountSid = source.TwilioAccountSid,
            TwilioAuthToken = source.TwilioAuthToken,
            TwilioPhoneNumber = source.TwilioPhoneNumber,
            SmsEnabled = source.SmsEnabled,
            SelectedInputDeviceId = source.SelectedInputDeviceId,
            SelectedOutputDeviceId = source.SelectedOutputDeviceId,
            InputVolume = source.InputVolume,
            OutputVolume = source.OutputVolume,
            IsNoiseSuppressionEnabled = source.IsNoiseSuppressionEnabled,
            ModelProviderProfiles = source.ModelProviderProfiles.Select(CloneProfile).ToList(),
            ActivePlannerProfileId = source.ActivePlannerProfileId,
            ActiveChatProfileId = source.ActiveChatProfileId,
            LastModified = source.LastModified
        };
    }

    private static string GetModelProviderApiKeySecretName(string profileId)
    {
        return $"{ModelProviderProfileSecretPrefix}{profileId}:ApiKey";
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
        if (DateTime.UtcNow <= _suppressWatcherUntilUtc)
        {
            return;
        }

        // Reload settings if file was modified externally
        Thread.Sleep(100); // Brief delay to let write complete
        try
        {
            if (File.Exists(_settingsPath)
                && string.Equals(File.ReadAllText(_settingsPath), _lastSavedJson, StringComparison.Ordinal))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to inspect changed settings file: {ex}");
        }

        Load();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        Save();
    }

    private static ModelProviderProfile CloneProfile(ModelProviderProfile profile)
    {
        return new ModelProviderProfile
        {
            Id = profile.Id,
            Provider = profile.Provider,
            DisplayName = profile.DisplayName,
            Endpoint = profile.Endpoint,
            ApiKey = profile.ApiKey,
            ModelId = profile.ModelId,
            Roles = profile.Roles.ToList(),
            Temperature = profile.Temperature,
            MaxTokens = profile.MaxTokens,
            Enabled = profile.Enabled
        };
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

        // Voice Settings
        public string? SelectedInputDeviceId { get; set; }
        public string? SelectedOutputDeviceId { get; set; }
        public float InputVolume { get; set; } = 1.0f;
        public float OutputVolume { get; set; } = 1.0f;
        public bool IsNoiseSuppressionEnabled { get; set; } = true;

        // AI Runtime Settings
        public List<ModelProviderProfile> ModelProviderProfiles { get; set; } = [];
        public string? ActivePlannerProfileId { get; set; }
        public string? ActiveChatProfileId { get; set; }

        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}

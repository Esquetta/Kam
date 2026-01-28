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

    #region Properties

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
                        StartupBehavior = 0
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
        public bool AutoStart { get; set; }
        public bool StartMinimized { get; set; }
        public bool ShowOnStartup { get; set; } = true;
        public int StartupBehavior { get; set; }
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
    }
}

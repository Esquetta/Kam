using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.IO;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel? _mainViewModel;
        private readonly ISettingsService _settingsService;
        private int _selectedLanguageIndex;

        public SettingsViewModel()
        {
            Title = "SETTINGS";
            _selectedLanguageIndex = 0;
            _settingsService = new JsonSettingsService();
            
            // Load saved settings
            _settingsService.Load();
            RefreshStartupSettings();
            
            // Subscribe to setting changes
            _settingsService.SettingChanged += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Setting changed: {e.SettingName} = {e.NewValue}");
            };
        }

        public SettingsViewModel(MainWindowViewModel mainViewModel) : this()
        {
            _mainViewModel = mainViewModel;
            _selectedLanguageIndex = mainViewModel.SelectedLanguageIndex;
        }

        #region Language

        public int SelectedLanguageIndex
        {
            get => _selectedLanguageIndex;
            set
            {
                if (_selectedLanguageIndex != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedLanguageIndex, value);

                    // Store in main view model for persistence
                    if (_mainViewModel != null)
                    {
                        _mainViewModel.SelectedLanguageIndex = value;
                    }

                    UpdateLanguage();
                }
            }
        }

        private void UpdateLanguage()
        {
            string langCode = _selectedLanguageIndex switch
            {
                0 => "en-US",
                1 => "es-ES",
                2 => "fr-FR",
                3 => "de-DE",
                4 => "zh-CN",
                5 => "ja-JP",
                6 => "tr-TR",
                _ => "en-US"
            };
            LocalizationService.Instance.SetLanguage(langCode);
        }

        #endregion

        #region Startup Behavior

        /// <summary>
        /// Controls whether the application starts automatically with Windows
        /// </summary>
        public bool AutoStart
        {
            get => CheckRegistryAutoStart();
            set
            {
                var current = CheckRegistryAutoStart();
                if (current != value)
                {
                    _settingsService.AutoStart = value;
                    this.RaisePropertyChanged();
                    ApplyAutoStartSetting(value);
                }
            }
        }

        /// <summary>
        /// Checks if auto-start is enabled in the registry
        /// </summary>
        private bool CheckRegistryAutoStart()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                
                if (key != null)
                {
                    // Check for "Kam" registry value (previously "KAM Neural Core", "SmartVoiceAgent")
                    var value1 = key.GetValue("Kam");
                    var value2 = key.GetValue("KAM Neural Core");
                    var value3 = key.GetValue("SmartVoiceAgent");
                    return value1 != null || value2 != null || value3 != null;
                }
            }
            catch { }
            
            return _settingsService.AutoStart;
        }

        /// <summary>
        /// Controls whether the application starts minimized to tray
        /// </summary>
        public bool StartMinimized
        {
            get => _settingsService.StartMinimized;
            set
            {
                if (_settingsService.StartMinimized != value)
                {
                    _settingsService.StartMinimized = value;
                    this.RaisePropertyChanged();
                }
            }
        }

        /// <summary>
        /// Controls startup behavior (0 = Normal, 1 = Minimized, 2 = Tray only)
        /// </summary>
        public int StartupBehavior
        {
            get => _settingsService.StartupBehavior;
            set
            {
                if (_settingsService.StartupBehavior != value)
                {
                    _settingsService.StartupBehavior = value;
                    this.RaisePropertyChanged();
                    
                    // Sync related properties
                    StartMinimized = value == 1;
                    this.RaisePropertyChanged(nameof(StartMinimized));
                }
            }
        }

        /// <summary>
        /// Whether to show main window on startup (inverse of StartMinimized for toggle binding)
        /// </summary>
        public bool ShowOnStartup
        {
            get => !_settingsService.StartMinimized;
            set
            {
                bool newMinimized = !value;
                if (_settingsService.StartMinimized != newMinimized)
                {
                    _settingsService.StartMinimized = newMinimized;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(StartMinimized));
                    
                    // Update behavior mode
                    StartupBehavior = newMinimized ? 1 : 0;
                }
            }
        }

        /// <summary>
        /// Refreshes all startup-related properties (call after settings load)
        /// </summary>
        public void RefreshStartupSettings()
        {
            this.RaisePropertyChanged(nameof(AutoStart));
            this.RaisePropertyChanged(nameof(StartMinimized));
            this.RaisePropertyChanged(nameof(StartupBehavior));
            this.RaisePropertyChanged(nameof(ShowOnStartup));
        }

        /// <summary>
        /// Applies the auto-start setting to the system registry
        /// </summary>
        private void ApplyAutoStartSetting(bool enable)
        {
            try
            {
                // Get the correct executable path
                string? executablePath = GetExecutablePath();
                
                if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Auto-start: Could not find valid executable path");
                    return;
                }

                // Quote the path if it contains spaces (required for Task Manager to recognize it)
                if (executablePath.Contains(' ') && !executablePath.StartsWith("\""))
                {
                    executablePath = $"\"{executablePath}\"";
                }
                
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                
                if (key != null)
                {
                    if (enable)
                    {
                        // Use "Kam" as the registry value name (appears in Task Manager Startup Apps)
                        key.SetValue("Kam", executablePath);
                        System.Diagnostics.Debug.WriteLine($"Auto-start enabled: {executablePath}");
                    }
                    else
                    {
                        // Try to delete current and old names for compatibility
                        try { key.DeleteValue("Kam", false); } catch { }
                        try { key.DeleteValue("KAM Neural Core", false); } catch { }
                        try { key.DeleteValue("SmartVoiceAgent", false); } catch { }
                        System.Diagnostics.Debug.WriteLine("Auto-start disabled");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Auto-start: Could not open registry key");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set auto-start: {ex}");
            }
        }

        /// <summary>
        /// Gets the actual executable path, prioritizing the .exe over DLL
        /// </summary>
        private string? GetExecutablePath()
        {
            // Try multiple methods to get the correct EXE path
            
            // Method 1: Process.MainModule (most reliable for running app)
            try
            {
                using var process = System.Diagnostics.Process.GetCurrentProcess();
                var mainModulePath = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(mainModulePath) && mainModulePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return mainModulePath;
                }
            }
            catch { }

            // Method 2: Environment.ProcessPath (.NET 6+)
            try
            {
                var path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }
            catch { }

            // Method 3: Entry assembly location (convert DLL path to EXE)
            try
            {
                var assemblyPath = System.Reflection.Assembly.GetEntryAssembly()?.Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    // If it's a DLL, try to find the corresponding EXE
                    if (assemblyPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        var exePath = assemblyPath.Substring(0, assemblyPath.Length - 4) + ".exe";
                        if (File.Exists(exePath))
                        {
                            return exePath;
                        }
                    }
                    else if (assemblyPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return assemblyPath;
                    }
                }
            }
            catch { }

            // Method 4: Executing assembly with exe substitution
            try
            {
                var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    var exePath = assemblyPath.Substring(0, assemblyPath.Length - 4) + ".exe";
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
            catch { }

            return null;
        }

        #endregion
    }
}

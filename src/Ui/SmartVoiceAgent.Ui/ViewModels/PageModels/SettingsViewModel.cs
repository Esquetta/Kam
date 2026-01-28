using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System;

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
            get => _settingsService.AutoStart;
            set
            {
                if (_settingsService.AutoStart != value)
                {
                    _settingsService.AutoStart = value;
                    this.RaisePropertyChanged();
                    ApplyAutoStartSetting(value);
                }
            }
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
                var executablePath = Environment.ProcessPath ?? 
                    System.Reflection.Assembly.GetExecutingAssembly().Location;
                
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                
                if (key != null)
                {
                    if (enable)
                    {
                        key.SetValue("SmartVoiceAgent", executablePath);
                    }
                    else
                    {
                        key.DeleteValue("SmartVoiceAgent", false);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set auto-start: {ex}");
            }
        }

        #endregion
    }
}

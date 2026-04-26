using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class SettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly MainWindowViewModel? _mainViewModel;
        private readonly ISettingsService _settingsService;
        private readonly AudioDeviceService _audioDeviceService;
        private readonly VoiceTestService? _voiceTestService;
        private int _selectedLanguageIndex;
        private CancellationTokenSource? _inputLevelCts;

        public ReactiveCommand<Unit, Unit> StartMicTestCommand { get; }
        public ReactiveCommand<Unit, Unit> StopMicTestCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayTestRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
        public ReactiveCommand<Unit, Unit> TestAiConnectionCommand { get; }

        public SettingsViewModel() : this(new JsonSettingsService(), null)
        {
        }

        public SettingsViewModel(ISettingsService settingsService) : this(settingsService, null)
        {
        }

        public SettingsViewModel(MainWindowViewModel mainViewModel) : this(new JsonSettingsService(), mainViewModel)
        {
        }

        private SettingsViewModel(ISettingsService settingsService, MainWindowViewModel? mainViewModel)
        {
            _mainViewModel = mainViewModel;
            Title = "SETTINGS";
            _selectedLanguageIndex = mainViewModel?.SelectedLanguageIndex ?? 0;
            _settingsService = settingsService;
            _audioDeviceService = new AudioDeviceService();
            
            // Initialize voice test service with factory from DI if available
            var voiceRecognitionFactory = App.Services?.GetService(typeof(IVoiceRecognitionFactory)) as IVoiceRecognitionFactory;
            _voiceTestService = voiceRecognitionFactory != null 
                ? new VoiceTestService(voiceRecognitionFactory)
                : null;

            // Initialize commands
            StartMicTestCommand = ReactiveCommand.Create(StartMicTest);
            StopMicTestCommand = ReactiveCommand.Create(StopMicTest);
            PlayTestRecordingCommand = ReactiveCommand.Create(PlayTestRecording);
            RefreshDevicesCommand = ReactiveCommand.Create(RefreshAudioDevices);
            TestAiConnectionCommand = ReactiveCommand.Create(TestAiProfileSettings);
            
            // Load saved settings
            _settingsService.Load();
            InitializeAiSettings();
            RefreshStartupSettings();
            
            // Subscribe to setting changes
            _settingsService.SettingChanged += (s, e) =>
            {
                System.Diagnostics.Debug.WriteLine($"Setting changed: {e.SettingName} = {e.NewValue}");
            };

            // Initialize voice settings
            InitializeVoiceSettings();
        }

        #region AI Runtime Settings

        private bool _isInitializingAiSettings;
        private string _aiProvider = "OpenRouter";
        private string _aiEndpoint = "https://openrouter.ai/api/v1";
        private string _aiModelId = "openai/gpt-4.1-mini";
        private string _aiApiKey = string.Empty;
        private string _activePlannerProfileId = "openrouter-primary";
        private string _aiProfileStatus = "Profile not validated.";
        private bool _isAiProfileValid;

        public IReadOnlyList<string> AiProviders { get; } =
        [
            "OpenRouter",
            "OpenAICompatible",
            "Ollama"
        ];

        public string AiProvider
        {
            get => _aiProvider;
            set
            {
                if (_aiProvider != value)
                {
                    this.RaiseAndSetIfChanged(ref _aiProvider, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string AiEndpoint
        {
            get => _aiEndpoint;
            set
            {
                if (_aiEndpoint != value)
                {
                    this.RaiseAndSetIfChanged(ref _aiEndpoint, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string AiModelId
        {
            get => _aiModelId;
            set
            {
                if (_aiModelId != value)
                {
                    this.RaiseAndSetIfChanged(ref _aiModelId, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string AiApiKey
        {
            get => _aiApiKey;
            set
            {
                if (_aiApiKey != value)
                {
                    this.RaiseAndSetIfChanged(ref _aiApiKey, value);
                    this.RaisePropertyChanged(nameof(MaskedAiApiKey));
                    SaveAiProfileSettings();
                }
            }
        }

        public string MaskedAiApiKey => new ModelProviderProfile { ApiKey = _aiApiKey }.MaskedApiKey;

        public string ActivePlannerProfileId
        {
            get => _activePlannerProfileId;
            set
            {
                if (_activePlannerProfileId != value)
                {
                    this.RaiseAndSetIfChanged(ref _activePlannerProfileId, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string AiProfileStatus
        {
            get => _aiProfileStatus;
            private set => this.RaiseAndSetIfChanged(ref _aiProfileStatus, value);
        }

        public bool IsAiProfileValid
        {
            get => _isAiProfileValid;
            private set => this.RaiseAndSetIfChanged(ref _isAiProfileValid, value);
        }

        private void InitializeAiSettings()
        {
            var shouldSeedDefaultProfile = false;
            _isInitializingAiSettings = true;
            try
            {
                var activeProfileId = _settingsService.ActivePlannerProfileId;
                var profile = _settingsService.ModelProviderProfiles.FirstOrDefault(p => p.Id == activeProfileId)
                    ?? _settingsService.ModelProviderProfiles.FirstOrDefault(p => p.Roles.Contains(ModelProviderRole.Planner))
                    ?? CreateDefaultPlannerProfile();

                _activePlannerProfileId = profile.Id;
                _aiProvider = profile.Provider.ToString();
                _aiEndpoint = profile.Endpoint;
                _aiModelId = profile.ModelId;
                _aiApiKey = profile.ApiKey;

                shouldSeedDefaultProfile = _settingsService.ModelProviderProfiles.All(p => p.Id != profile.Id);
            }
            finally
            {
                _isInitializingAiSettings = false;
            }

            if (shouldSeedDefaultProfile)
            {
                SaveAiProfileSettings();
            }
        }

        private void SaveAiProfileSettings()
        {
            if (_isInitializingAiSettings)
            {
                return;
            }

            var profile = CreatePlannerProfile();

            var profiles = _settingsService.ModelProviderProfiles
                .Where(p => !p.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))
                .Concat([profile])
                .ToList();

            _settingsService.ModelProviderProfiles = profiles;
            _settingsService.ActivePlannerProfileId = profile.Id;
        }

        private void TestAiProfileSettings()
        {
            var profile = CreatePlannerProfile();
            var validation = profile.Validate();

            IsAiProfileValid = validation.IsValid;
            AiProfileStatus = validation.IsValid
                ? "Profile is valid. Restart Kam to apply runtime changes."
                : string.Join(" ", validation.Errors);

            SaveAiProfileSettings();
        }

        private ModelProviderProfile CreatePlannerProfile()
        {
            var provider = ParseProvider(_aiProvider);

            return new ModelProviderProfile
            {
                Id = string.IsNullOrWhiteSpace(_activePlannerProfileId) ? "openrouter-primary" : _activePlannerProfileId,
                Provider = provider,
                DisplayName = $"{_aiProvider} Planner",
                Endpoint = _aiEndpoint,
                ApiKey = _aiApiKey,
                ModelId = _aiModelId,
                Roles = [ModelProviderRole.Planner],
                Enabled = provider == ModelProviderType.Ollama || !string.IsNullOrWhiteSpace(_aiApiKey)
            };
        }

        private static ModelProviderProfile CreateDefaultPlannerProfile()
        {
            return new ModelProviderProfile
            {
                Id = "openrouter-primary",
                Provider = ModelProviderType.OpenRouter,
                DisplayName = "OpenRouter Planner",
                Endpoint = "https://openrouter.ai/api/v1",
                ModelId = "openai/gpt-4.1-mini",
                Roles = [ModelProviderRole.Planner],
                Enabled = false
            };
        }

        private static ModelProviderType ParseProvider(string provider)
        {
            return Enum.TryParse<ModelProviderType>(provider, ignoreCase: true, out var parsed)
                ? parsed
                : ModelProviderType.OpenAICompatible;
        }

        #endregion

        #region Voice Settings

        private List<AudioDeviceInfo> _inputDevices = new();
        public List<AudioDeviceInfo> InputDevices
        {
            get => _inputDevices;
            private set => this.RaiseAndSetIfChanged(ref _inputDevices, value);
        }

        private List<AudioDeviceInfo> _outputDevices = new();
        public List<AudioDeviceInfo> OutputDevices
        {
            get => _outputDevices;
            private set => this.RaiseAndSetIfChanged(ref _outputDevices, value);
        }

        private AudioDeviceInfo? _selectedInputDevice;
        public AudioDeviceInfo? SelectedInputDevice
        {
            get => _selectedInputDevice;
            set
            {
                if (_selectedInputDevice != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedInputDevice, value);
                    if (value != null)
                    {
                        _voiceTestService?.SetInputDevice(value.Id);
                        _settingsService.SelectedInputDeviceId = value.Id;
                    }
                }
            }
        }

        private AudioDeviceInfo? _selectedOutputDevice;
        public AudioDeviceInfo? SelectedOutputDevice
        {
            get => _selectedOutputDevice;
            set
            {
                if (_selectedOutputDevice != value)
                {
                    this.RaiseAndSetIfChanged(ref _selectedOutputDevice, value);
                    if (value != null)
                    {
                        _settingsService.SelectedOutputDeviceId = value.Id;
                    }
                }
            }
        }

        private float _inputVolume = 1.0f;
        public float InputVolume
        {
            get => _inputVolume;
            set
            {
                if (_inputVolume != value)
                {
                    this.RaiseAndSetIfChanged(ref _inputVolume, value);
                    if (SelectedInputDevice != null)
                    {
                        _audioDeviceService.SetInputVolume(SelectedInputDevice.Id, value);
                    }
                }
            }
        }

        private float _outputVolume = 1.0f;
        public float OutputVolume
        {
            get => _outputVolume;
            set
            {
                if (_outputVolume != value)
                {
                    this.RaiseAndSetIfChanged(ref _outputVolume, value);
                    if (SelectedOutputDevice != null)
                    {
                        _audioDeviceService.SetOutputVolume(SelectedOutputDevice.Id, value);
                    }
                }
            }
        }

        private float _inputLevel = 0;
        public float InputLevel
        {
            get => _inputLevel;
            private set => this.RaiseAndSetIfChanged(ref _inputLevel, value);
        }

        private bool _isMicTesting;
        public bool IsMicTesting
        {
            get => _isMicTesting;
            private set => this.RaiseAndSetIfChanged(ref _isMicTesting, value);
        }

        private bool _isRecordingTest;
        public bool IsRecordingTest
        {
            get => _isRecordingTest;
            private set => this.RaiseAndSetIfChanged(ref _isRecordingTest, value);
        }

        private bool _hasTestRecording;
        public bool HasTestRecording
        {
            get => _hasTestRecording;
            private set => this.RaiseAndSetIfChanged(ref _hasTestRecording, value);
        }

        private bool _isNoiseSuppressionEnabled = true;
        public bool IsNoiseSuppressionEnabled
        {
            get => _isNoiseSuppressionEnabled;
            set
            {
                if (_isNoiseSuppressionEnabled != value)
                {
                    this.RaiseAndSetIfChanged(ref _isNoiseSuppressionEnabled, value);
                    _settingsService.IsNoiseSuppressionEnabled = value;
                }
            }
        }

        private string? _audioErrorMessage;
        public string? AudioErrorMessage
        {
            get => _audioErrorMessage;
            private set => this.RaiseAndSetIfChanged(ref _audioErrorMessage, value);
        }

        private bool _hasInputDevices;
        public bool HasInputDevices
        {
            get => _hasInputDevices;
            private set => this.RaiseAndSetIfChanged(ref _hasInputDevices, value);
        }

        private bool _hasOutputDevices;
        public bool HasOutputDevices
        {
            get => _hasOutputDevices;
            private set => this.RaiseAndSetIfChanged(ref _hasOutputDevices, value);
        }

        private bool _isAudioAvailable;
        public bool IsAudioAvailable
        {
            get => _isAudioAvailable;
            private set => this.RaiseAndSetIfChanged(ref _isAudioAvailable, value);
        }

        private void InitializeVoiceSettings()
        {
            // Check audio availability
            IsAudioAvailable = _audioDeviceService.IsAvailable;
            if (!IsAudioAvailable)
            {
                AudioErrorMessage = _audioDeviceService.LastError ?? "Audio system is not available.";
            }

            // Subscribe to device changes
            _audioDeviceService.DevicesChanged += OnDevicesChanged;

            // Load devices
            RefreshAudioDevices();

            // Subscribe to voice test events
            if (_voiceTestService != null)
            {
                _voiceTestService.OnInputLevelChanged += OnInputLevelChanged;
                _voiceTestService.OnRecordingStateChanged += OnRecordingStateChanged;
                _voiceTestService.OnPlaybackStateChanged += OnPlaybackStateChanged;
            }

            // Load saved device selections
            var savedInputId = _settingsService.SelectedInputDeviceId;
            var savedOutputId = _settingsService.SelectedOutputDeviceId;
            _isNoiseSuppressionEnabled = _settingsService.IsNoiseSuppressionEnabled;

            // Validate saved devices are still available
            if (!string.IsNullOrEmpty(savedInputId) && _audioDeviceService.IsDeviceAvailable(savedInputId))
            {
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.Id == savedInputId);
            }
            else if (!string.IsNullOrEmpty(savedInputId))
            {
                // Saved device no longer available, clear it
                _settingsService.SelectedInputDeviceId = null;
            }

            if (!string.IsNullOrEmpty(savedOutputId) && _audioDeviceService.IsDeviceAvailable(savedOutputId))
            {
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == savedOutputId);
            }
            else if (!string.IsNullOrEmpty(savedOutputId))
            {
                // Saved device no longer available, clear it
                _settingsService.SelectedOutputDeviceId = null;
            }

            // Start monitoring input levels
            StartInputLevelMonitoring();
        }

        private void OnDevicesChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Check if selected devices are still available
                if (SelectedInputDevice != null && !_audioDeviceService.IsDeviceAvailable(SelectedInputDevice.Id))
                {
                    // Device disconnected, select default
                    RefreshAudioDevices();
                    AudioErrorMessage = "Selected microphone was disconnected. Switched to default device.";
                }
                else if (SelectedOutputDevice != null && !_audioDeviceService.IsDeviceAvailable(SelectedOutputDevice.Id))
                {
                    RefreshAudioDevices();
                    AudioErrorMessage = "Selected output device was disconnected. Switched to default device.";
                }
                else
                {
                    RefreshAudioDevices();
                }
            });
        }

        private void RefreshAudioDevices()
        {
            InputDevices = _audioDeviceService.GetInputDevices();
            OutputDevices = _audioDeviceService.GetOutputDevices();

            HasInputDevices = InputDevices.Count > 0;
            HasOutputDevices = OutputDevices.Count > 0;
            IsAudioAvailable = _audioDeviceService.IsAvailable;

            // Select default if no selection or current selection invalid
            if (SelectedInputDevice == null || !InputDevices.Any(d => d.Id == SelectedInputDevice.Id))
            {
                SelectedInputDevice = InputDevices.FirstOrDefault(d => d.IsDefault) ?? InputDevices.FirstOrDefault();
            }
            
            if (SelectedOutputDevice == null || !OutputDevices.Any(d => d.Id == SelectedOutputDevice.Id))
            {
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.IsDefault) ?? OutputDevices.FirstOrDefault();
            }

            // Clear error if devices are now available
            if (HasInputDevices && HasOutputDevices)
            {
                AudioErrorMessage = null;
            }
        }

        private void StartInputLevelMonitoring()
        {
            _inputLevelCts?.Cancel();
            _inputLevelCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                // Performance: Throttle to 10 FPS (100ms) instead of 20 FPS to reduce UI thread load
                // This is still smooth enough for VU meter visualization
                const int updateIntervalMs = 100;
                float lastLevel = 0;

                while (!_inputLevelCts.Token.IsCancellationRequested)
                {
                    if (SelectedInputDevice != null && !IsRecordingTest)
                    {
                        var level = _audioDeviceService.GetInputLevel(SelectedInputDevice.Id);
                        
                        // Only update UI if level changed significantly (> 0.05) or on every 5th update
                        // This reduces unnecessary UI refreshes
                        if (Math.Abs(level - lastLevel) > 0.05f || Environment.TickCount % 5 == 0)
                        {
                            lastLevel = level;
                            Dispatcher.UIThread.Post(() => InputLevel = level);
                        }
                    }
                    await Task.Delay(updateIntervalMs, _inputLevelCts.Token);
                }
            }, _inputLevelCts.Token);
        }

        private void OnInputLevelChanged(object? sender, float level)
        {
            Dispatcher.UIThread.Post(() => InputLevel = level);
        }

        private void OnRecordingStateChanged(object? sender, bool isRecording)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRecordingTest = isRecording;
                HasTestRecording = !isRecording && _hasTestRecording;
            });
        }

        private void OnPlaybackStateChanged(object? sender, bool isPlaying)
        {
            Dispatcher.UIThread.Post(() => { });
        }

        public void StartMicTest()
        {
            _voiceTestService?.StartRecording(10);
            HasTestRecording = true;
        }

        public void StopMicTest()
        {
            _voiceTestService?.StopRecording();
        }

        public void PlayTestRecording()
        {
            _voiceTestService?.StartPlayback();
        }

        public void StopTestPlayback()
        {
            _voiceTestService?.StopPlayback();
        }

        #endregion

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

        public void Dispose()
        {
            _inputLevelCts?.Cancel();
            
            if (_audioDeviceService != null)
            {
                _audioDeviceService.DevicesChanged -= OnDevicesChanged;
                _audioDeviceService.Dispose();
            }
            
            _voiceTestService?.Dispose();
        }
    }
}

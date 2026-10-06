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
        private readonly IModelCatalogService _modelCatalogService;
        private readonly IModelConnectionTestService _modelConnectionTestService;
        private readonly IAutoStartRegistrationService _autoStartRegistrationService;
        private readonly bool _ownsModelCatalogService;
        private readonly bool _ownsModelConnectionTestService;
        private readonly AudioDeviceService _audioDeviceService;
        private readonly VoiceTestService? _voiceTestService;
        private CancellationTokenSource? _inputLevelCts;

        public ReactiveCommand<Unit, Unit> StartMicTestCommand { get; }
        public ReactiveCommand<Unit, Unit> StopMicTestCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayTestRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
        public ReactiveCommand<Unit, Unit> TestAiConnectionCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshAiModelsCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshChatModelsCommand { get; }

        public SettingsViewModel() : this(new JsonSettingsService(), null, null, null, null)
        {
        }

        public SettingsViewModel(ISettingsService settingsService) : this(settingsService, null, null, null, null)
        {
        }

        public SettingsViewModel(ISettingsService settingsService, IModelCatalogService modelCatalogService)
            : this(settingsService, null, modelCatalogService, null, null)
        {
        }

        public SettingsViewModel(
            ISettingsService settingsService,
            IModelCatalogService modelCatalogService,
            IModelConnectionTestService modelConnectionTestService)
            : this(settingsService, null, modelCatalogService, modelConnectionTestService, null)
        {
        }

        public SettingsViewModel(
            ISettingsService settingsService,
            IModelCatalogService modelCatalogService,
            IModelConnectionTestService modelConnectionTestService,
            IAutoStartRegistrationService autoStartRegistrationService)
            : this(settingsService, null, modelCatalogService, modelConnectionTestService, autoStartRegistrationService)
        {
        }

        public SettingsViewModel(MainWindowViewModel mainViewModel) : this(new JsonSettingsService(), mainViewModel, null, null, null)
        {
        }

        public SettingsViewModel(ISettingsService settingsService, MainWindowViewModel mainViewModel)
            : this(settingsService, mainViewModel, null, null, null)
        {
        }

        private SettingsViewModel(
            ISettingsService settingsService,
            MainWindowViewModel? mainViewModel,
            IModelCatalogService? modelCatalogService,
            IModelConnectionTestService? modelConnectionTestService,
            IAutoStartRegistrationService? autoStartRegistrationService)
        {
            _mainViewModel = mainViewModel;
            Title = Loc.Get("Settings.Title");
            _settingsService = settingsService;
            _modelCatalogService = modelCatalogService ?? CompositeModelCatalogService.CreateDefault();
            _modelConnectionTestService = modelConnectionTestService ?? new ModelConnectionTestService();
            _autoStartRegistrationService = autoStartRegistrationService ?? new WindowsAutoStartRegistrationService();
            _ownsModelCatalogService = modelCatalogService is null;
            _ownsModelConnectionTestService = modelConnectionTestService is null;
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
            TestAiConnectionCommand = ReactiveCommand.CreateFromTask(TestAiProfileSettingsAsync);
            RefreshAiModelsCommand = ReactiveCommand.CreateFromTask(RefreshPlannerModelsAsync);
            RefreshChatModelsCommand = ReactiveCommand.CreateFromTask(RefreshChatModelsAsync);
            DownloadSpeechModelCommand = ReactiveCommand.CreateFromTask(DownloadSpeechModelAsync);
            PreviewSpeechCommand = ReactiveCommand.CreateFromTask(PreviewSpeechAsync);
            StopSpeechPreviewCommand = ReactiveCommand.Create(StopSpeechPreview);
            
            // Load saved settings
            _settingsService.Load();
            InitializeAiSettings();
            RefreshStartupSettings();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

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
        private string _aiModelId = "openai/gpt-5.4-mini";
        private string _aiApiKey = string.Empty;
        private string _activePlannerProfileId = "openrouter-primary";
        private string _chatProvider = "OpenRouter";
        private string _chatEndpoint = "https://openrouter.ai/api/v1";
        private string _chatModelId = "openai/gpt-5.4-mini";
        private string _chatApiKey = string.Empty;
        private string _activeChatProfileId = "openrouter-chat";
        private Func<string> _aiProfileStatusText = static () => Loc.Get("Settings.Status.NotTested");
        private string _aiProfileStatus = Loc.Get("Settings.Status.NotTested");
        private bool _isAiProfileValid;
        private IReadOnlyList<string> _aiModelOptions = CreateDefaultModelOptions("OpenRouter", "openai/gpt-5.4-mini");
        private IReadOnlyList<string> _chatModelOptions = CreateDefaultModelOptions("OpenRouter", "openai/gpt-5.4-mini");
        private IReadOnlyList<ModelCatalogEntry> _aiModelCatalogEntries = CreateDefaultModelCatalogEntries("OpenRouter", "openai/gpt-5.4-mini");
        private IReadOnlyList<ModelCatalogEntry> _chatModelCatalogEntries = CreateDefaultModelCatalogEntries("OpenRouter", "openai/gpt-5.4-mini");
        private bool _isRefreshingAiModels;
        private bool _isRefreshingChatModels;
        private bool _isTestingAiConnection;
        private bool _isPlannerModelCatalogBacked = true;
        private bool _isChatModelCatalogBacked = true;

        public IReadOnlyList<string> AiProviders { get; } =
        [
            "OpenAI",
            "Anthropic",
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
                    ApplyProviderDefaults(ModelProviderRole.Planner);
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

        public string ChatProvider
        {
            get => _chatProvider;
            set
            {
                if (_chatProvider != value)
                {
                    this.RaiseAndSetIfChanged(ref _chatProvider, value);
                    ApplyProviderDefaults(ModelProviderRole.Chat);
                    SaveAiProfileSettings();
                }
            }
        }

        public string ChatEndpoint
        {
            get => _chatEndpoint;
            set
            {
                if (_chatEndpoint != value)
                {
                    this.RaiseAndSetIfChanged(ref _chatEndpoint, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string ChatModelId
        {
            get => _chatModelId;
            set
            {
                if (_chatModelId != value)
                {
                    this.RaiseAndSetIfChanged(ref _chatModelId, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string ChatApiKey
        {
            get => _chatApiKey;
            set
            {
                if (_chatApiKey != value)
                {
                    this.RaiseAndSetIfChanged(ref _chatApiKey, value);
                    this.RaisePropertyChanged(nameof(MaskedChatApiKey));
                    SaveAiProfileSettings();
                }
            }
        }

        public string MaskedChatApiKey => new ModelProviderProfile { ApiKey = _chatApiKey }.MaskedApiKey;

        public IReadOnlyList<string> AiModelOptions
        {
            get => _aiModelOptions;
            private set => this.RaiseAndSetIfChanged(ref _aiModelOptions, value);
        }

        public IReadOnlyList<string> ChatModelOptions
        {
            get => _chatModelOptions;
            private set => this.RaiseAndSetIfChanged(ref _chatModelOptions, value);
        }

        public IReadOnlyList<ModelCatalogEntry> AiModelCatalogEntries
        {
            get => _aiModelCatalogEntries;
            private set => this.RaiseAndSetIfChanged(ref _aiModelCatalogEntries, value);
        }

        public IReadOnlyList<ModelCatalogEntry> ChatModelCatalogEntries
        {
            get => _chatModelCatalogEntries;
            private set => this.RaiseAndSetIfChanged(ref _chatModelCatalogEntries, value);
        }

        public bool IsPlannerModelCatalogBacked
        {
            get => _isPlannerModelCatalogBacked;
            private set => this.RaiseAndSetIfChanged(ref _isPlannerModelCatalogBacked, value);
        }

        public bool IsChatModelCatalogBacked
        {
            get => _isChatModelCatalogBacked;
            private set => this.RaiseAndSetIfChanged(ref _isChatModelCatalogBacked, value);
        }

        public bool IsRefreshingAiModels
        {
            get => _isRefreshingAiModels;
            private set => this.RaiseAndSetIfChanged(ref _isRefreshingAiModels, value);
        }

        public bool IsRefreshingChatModels
        {
            get => _isRefreshingChatModels;
            private set => this.RaiseAndSetIfChanged(ref _isRefreshingChatModels, value);
        }

        public bool IsTestingAiConnection
        {
            get => _isTestingAiConnection;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isTestingAiConnection, value);
                this.RaisePropertyChanged(nameof(AiConnectionTestButtonText));
            }
        }

        public string AiConnectionTestButtonText => IsTestingAiConnection
            ? Loc.Get("Settings.Testing")
            : Loc.Get("Settings.TestConnection");

        public string ActiveChatProfileId
        {
            get => _activeChatProfileId;
            set
            {
                if (_activeChatProfileId != value)
                {
                    this.RaiseAndSetIfChanged(ref _activeChatProfileId, value);
                    SaveAiProfileSettings();
                }
            }
        }

        public string AiProfileStatus
        {
            get => _aiProfileStatus;
            private set => this.RaiseAndSetIfChanged(ref _aiProfileStatus, value);
        }

        /// <summary>
        /// Shows a profile status and keeps how it was built, so it can be shown again in another language.
        /// </summary>
        /// <param name="text">Builds the status text in the current language.</param>
        private void SetAiProfileStatus(Func<string> text)
        {
            _aiProfileStatusText = text;
            AiProfileStatus = text();
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
                var profiles = _settingsService.ModelProviderProfiles;
                var profile = profiles.FirstOrDefault(p => p.Id == activeProfileId)
                    ?? profiles.FirstOrDefault(p => p.Roles.Contains(ModelProviderRole.Planner))
                    ?? CreateDefaultPlannerProfile();
                var activeChatProfileId = _settingsService.ActiveChatProfileId;
                var chatProfile = profiles.FirstOrDefault(p => p.Id == activeChatProfileId)
                    ?? profiles.FirstOrDefault(p => p.Roles.Contains(ModelProviderRole.Chat))
                    ?? CreateDefaultChatProfile();

                _activePlannerProfileId = profile.Id;
                _aiProvider = profile.Provider.ToString();
                _aiEndpoint = profile.Endpoint;
                _aiModelId = profile.ModelId;
                _aiApiKey = profile.ApiKey;
                _activeChatProfileId = chatProfile.Id;
                _chatProvider = chatProfile.Provider.ToString();
                _chatEndpoint = chatProfile.Endpoint;
                _chatModelId = chatProfile.ModelId;
                _chatApiKey = chatProfile.ApiKey;
                _aiModelOptions = CreateDefaultModelOptions(_aiProvider, _aiModelId);
                _chatModelOptions = CreateDefaultModelOptions(_chatProvider, _chatModelId);
                _aiModelCatalogEntries = CreateDefaultModelCatalogEntries(_aiProvider, _aiModelId);
                _chatModelCatalogEntries = CreateDefaultModelCatalogEntries(_chatProvider, _chatModelId);
                _isPlannerModelCatalogBacked = IsCatalogBackedProvider(_aiProvider);
                _isChatModelCatalogBacked = IsCatalogBackedProvider(_chatProvider);

                shouldSeedDefaultProfile =
                    profiles.All(p => p.Id != profile.Id)
                    || profiles.All(p => p.Id != chatProfile.Id);
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
            var chatProfile = CreateChatProfile();
            var profileIds = new[] { profile.Id, chatProfile.Id };

            var profiles = _settingsService.ModelProviderProfiles
                .Where(p => !profileIds.Contains(p.Id, StringComparer.OrdinalIgnoreCase))
                .Concat([profile, chatProfile])
                .ToList();

            _settingsService.ModelProviderProfiles = profiles;
            _settingsService.ActivePlannerProfileId = profile.Id;
            _settingsService.ActiveChatProfileId = chatProfile.Id;
        }

        private async Task TestAiProfileSettingsAsync()
        {
            var profile = CreatePlannerProfile();
            var chatProfile = CreateChatProfile();
            var targets = CreateConnectionTestTargets(profile, chatProfile);
            var validationErrors = targets
                .SelectMany(ValidateProfileForConnectionTest)
                .ToArray();

            if (validationErrors.Length > 0)
            {
                IsAiProfileValid = false;
                SetAiProfileStatus(() => string.Join(" ", validationErrors.Select(error => error())));
                SaveAiProfileSettings();
                return;
            }

            try
            {
                IsTestingAiConnection = true;
                var testedProvider = profile.Provider;
                var testedModelId = profile.ModelId;
                SetAiProfileStatus(() => Loc.Format("Settings.Status.Testing", testedProvider, testedModelId));
                var results = new List<Func<string>>();

                foreach (var target in targets.Where(target => target.Required || ShouldTestOptionalProfile(target.Profile)))
                {
                    var result = await _modelConnectionTestService.TestAsync(target.Profile).ConfigureAwait(true);
                    if (!result.Success)
                    {
                        IsAiProfileValid = false;
                        SetAiProfileStatus(FormatConnectionFailure(target, result));
                        SaveAiProfileSettings();
                        return;
                    }

                    results.Add(FormatConnectionSuccess(target, result));
                }

                IsAiProfileValid = true;
                var verified = results.ToArray();
                SetAiProfileStatus(() => Loc.Format(
                    "Settings.Status.Verified",
                    string.Join("; ", verified.Select(text => text()))));
                SaveAiProfileSettings();
            }
            finally
            {
                IsTestingAiConnection = false;
            }
        }

        public Task RefreshPlannerModelsAsync()
        {
            return RefreshModelOptionsAsync(ModelProviderRole.Planner);
        }

        public Task RefreshChatModelsAsync()
        {
            return RefreshModelOptionsAsync(ModelProviderRole.Chat);
        }

        private async Task RefreshModelOptionsAsync(ModelProviderRole role)
        {
            var isPlanner = role == ModelProviderRole.Planner;
            var profile = isPlanner ? CreatePlannerProfile() : CreateChatProfile();

            if (!IsCatalogBackedProvider(profile.Provider))
            {
                SetModelOptions(role, CreateDefaultModelOptions(profile.Provider.ToString(), profile.ModelId));
                SetCatalogBacked(role, false);
                SetAiProfileStatus(static () => Loc.Get("Settings.Status.ManualModelEntry"));
                return;
            }

            SetCatalogBacked(role, true);

            try
            {
                SetIsRefreshingModels(role, true);
                var models = await _modelCatalogService.GetModelsAsync(profile).ConfigureAwait(true);
                SetModelOptions(role, models.Count > 0
                    ? models
                    : CreateDefaultModelCatalogEntries(profile.Provider.ToString(), profile.ModelId));

                var hasLiveAvailability = models.Any(model => model.IsAvailable);
                var provider = profile.Provider;
                SetAiProfileStatus(hasLiveAvailability
                    ? () => Loc.Format("Settings.Status.ModelListLoaded", GetRoleLabel(role), provider)
                    : () => Loc.Format("Settings.Status.ModelRegistryLoaded", GetRoleLabel(role)));
                SaveAiProfileSettings();
            }
            catch (Exception ex)
            {
                SetModelOptions(role, CreateDefaultModelCatalogEntries(profile.Provider.ToString(), profile.ModelId));
                var message = DescribeProviderMessage(ex.Message, profile);
                SetAiProfileStatus(() => Loc.Format("Settings.Status.ModelListFailed", message()));
            }
            finally
            {
                SetIsRefreshingModels(role, false);
            }
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

        private ModelProviderProfile CreateChatProfile()
        {
            var provider = ParseProvider(_chatProvider);

            return new ModelProviderProfile
            {
                Id = string.IsNullOrWhiteSpace(_activeChatProfileId) ? "openrouter-chat" : _activeChatProfileId,
                Provider = provider,
                DisplayName = $"{_chatProvider} Chat",
                Endpoint = _chatEndpoint,
                ApiKey = _chatApiKey,
                ModelId = _chatModelId,
                Roles = [ModelProviderRole.Chat],
                Enabled = provider == ModelProviderType.Ollama || !string.IsNullOrWhiteSpace(_chatApiKey)
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
                ModelId = "openai/gpt-5.4-mini",
                Roles = [ModelProviderRole.Planner],
                Enabled = false
            };
        }

        private static ModelProviderProfile CreateDefaultChatProfile()
        {
            return new ModelProviderProfile
            {
                Id = "openrouter-chat",
                Provider = ModelProviderType.OpenRouter,
                DisplayName = "OpenRouter Chat",
                Endpoint = "https://openrouter.ai/api/v1",
                ModelId = "openai/gpt-5.4-mini",
                Roles = [ModelProviderRole.Chat],
                Enabled = false
            };
        }

        private static ModelProviderType ParseProvider(string provider)
        {
            return Enum.TryParse<ModelProviderType>(provider, ignoreCase: true, out var parsed)
                ? parsed
                : ModelProviderType.OpenAICompatible;
        }

        private void ApplyProviderDefaults(ModelProviderRole role)
        {
            var isPlanner = role == ModelProviderRole.Planner;
            var providerText = isPlanner ? _aiProvider : _chatProvider;
            var provider = ParseProvider(providerText);
            var currentModel = isPlanner ? _aiModelId : _chatModelId;

            SetCatalogBacked(role, IsCatalogBackedProvider(provider));

            var endpoint = GetDefaultEndpoint(provider);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                if (isPlanner)
                {
                    this.RaiseAndSetIfChanged(ref _aiEndpoint, endpoint, nameof(AiEndpoint));
                }
                else
                {
                    this.RaiseAndSetIfChanged(ref _chatEndpoint, endpoint, nameof(ChatEndpoint));
                }
            }

            var model = NormalizeModelForProvider(provider, currentModel);
            if (isPlanner)
            {
                this.RaiseAndSetIfChanged(ref _aiModelId, model, nameof(AiModelId));
            }
            else
            {
                this.RaiseAndSetIfChanged(ref _chatModelId, model, nameof(ChatModelId));
            }

            SetModelOptions(role, CreateDefaultModelCatalogEntries(provider.ToString(), model));
        }

        private void SetModelOptions(ModelProviderRole role, IReadOnlyList<string> modelIds)
        {
            SetModelOptions(role, CreateModelCatalogEntries(ParseProvider(role == ModelProviderRole.Planner ? _aiProvider : _chatProvider), modelIds, "default"));
        }

        private void SetModelOptions(ModelProviderRole role, IReadOnlyList<ModelCatalogEntry> models)
        {
            var isPlanner = role == ModelProviderRole.Planner;
            var currentModel = isPlanner ? _aiModelId : _chatModelId;
            var options = MergeModelOptions(models.Select(model => model.ModelId), currentModel);
            if (options.Count == 0)
            {
                return;
            }

            var catalogEntries = MergeCatalogEntries(models, options, isPlanner ? _aiProvider : _chatProvider);
            if (isPlanner)
            {
                AiModelOptions = options;
                AiModelCatalogEntries = catalogEntries;
                if (!options.Contains(_aiModelId, StringComparer.OrdinalIgnoreCase))
                {
                    AiModelId = options[0];
                }
            }
            else
            {
                ChatModelOptions = options;
                ChatModelCatalogEntries = catalogEntries;
                if (!options.Contains(_chatModelId, StringComparer.OrdinalIgnoreCase))
                {
                    ChatModelId = options[0];
                }
            }
        }

        private void SetCatalogBacked(ModelProviderRole role, bool isCatalogBacked)
        {
            if (role == ModelProviderRole.Planner)
            {
                IsPlannerModelCatalogBacked = isCatalogBacked;
            }
            else
            {
                IsChatModelCatalogBacked = isCatalogBacked;
            }
        }

        private void SetIsRefreshingModels(ModelProviderRole role, bool isRefreshing)
        {
            if (role == ModelProviderRole.Planner)
            {
                IsRefreshingAiModels = isRefreshing;
            }
            else
            {
                IsRefreshingChatModels = isRefreshing;
            }
        }

        private static bool IsCatalogBackedProvider(string provider)
        {
            return IsCatalogBackedProvider(ParseProvider(provider));
        }

        private static bool IsCatalogBackedProvider(ModelProviderType provider)
        {
            return provider is ModelProviderType.OpenAI
                or ModelProviderType.Anthropic
                or ModelProviderType.OpenRouter
                or ModelProviderType.Ollama;
        }

        private static IReadOnlyList<ConnectionTestTarget> CreateConnectionTestTargets(
            ModelProviderProfile plannerProfile,
            ModelProviderProfile chatProfile)
        {
            return
            [
                new ConnectionTestTarget(ModelProviderRole.Planner, plannerProfile, Required: true),
                new ConnectionTestTarget(ModelProviderRole.Chat, chatProfile, Required: false)
            ];
        }

        private static IEnumerable<Func<string>> ValidateProfileForConnectionTest(ConnectionTestTarget target)
        {
            var profile = target.Profile;
            if (!target.Required && !ShouldTestOptionalProfile(profile))
            {
                yield break;
            }

            foreach (var error in profile.Validate().Errors)
            {
                yield return () => Loc.Format(
                    "Settings.Status.ProfileError",
                    GetRoleLabel(target.Role),
                    LocalizeProfileError(error));
            }

            if (profile.Provider != ModelProviderType.Ollama && string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                yield return () => Loc.Format("Settings.Status.ApiKeyRequired", GetRoleLabel(target.Role));
            }
        }

        /// <summary>
        /// Returns the name of a model role as the status messages show it.
        /// </summary>
        private static string GetRoleLabel(ModelProviderRole role)
        {
            return role == ModelProviderRole.Planner
                ? Loc.Get("Settings.Role.Planner")
                : Loc.Get("Settings.Role.Chat");
        }

        /// <summary>
        /// Translates a known <see cref="ModelProviderProfile.Validate"/> error; any other message is shown as it is.
        /// </summary>
        private static string LocalizeProfileError(string error)
        {
            return error switch
            {
                "Profile id is required." => Loc.Get("Settings.ProfileError.IdRequired"),
                "A valid endpoint is required." => Loc.Get("Settings.ProfileError.EndpointRequired"),
                "API key is required for enabled profiles." => Loc.Get("Settings.ProfileError.ApiKeyRequired"),
                "Model id is required." => Loc.Get("Settings.ProfileError.ModelRequired"),
                "At least one model role is required." => Loc.Get("Settings.ProfileError.RoleRequired"),
                "Max tokens must be greater than zero." => Loc.Get("Settings.ProfileError.MaxTokens"),
                "Temperature must be between 0 and 2." => Loc.Get("Settings.ProfileError.Temperature"),
                _ => error
            };
        }

        private static bool ShouldTestOptionalProfile(ModelProviderProfile profile)
        {
            return profile.Provider == ModelProviderType.Ollama
                || !string.IsNullOrWhiteSpace(profile.ApiKey);
        }

        private sealed record ConnectionTestTarget(
            ModelProviderRole Role,
            ModelProviderProfile Profile,
            bool Required);

        private static Func<string> FormatConnectionSuccess(
            ConnectionTestTarget target,
            ModelConnectionTestResult result)
        {
            var provider = result.Provider == ModelProviderType.OpenAICompatible
                ? target.Profile.Provider
                : result.Provider;
            var modelId = string.IsNullOrWhiteSpace(result.ModelId)
                ? target.Profile.ModelId
                : result.ModelId;
            var liveModelCount = result.LiveModelCount;

            return () => Loc.Format(
                "Settings.Status.TargetVerified",
                GetRoleLabel(target.Role),
                liveModelCount,
                provider,
                modelId);
        }

        private static Func<string> FormatConnectionFailure(
            ConnectionTestTarget target,
            ModelConnectionTestResult result)
        {
            var provider = result.Provider == ModelProviderType.OpenAICompatible
                ? target.Profile.Provider
                : result.Provider;
            var modelId = string.IsNullOrWhiteSpace(result.ModelId)
                ? target.Profile.ModelId
                : result.ModelId;
            var message = DescribeProviderMessage(result.Message, target.Profile);
            var failureCategory = result.FailureCategory;

            if (string.Equals(failureCategory, "Connection", StringComparison.OrdinalIgnoreCase)
                && result.Provider == ModelProviderType.OpenAICompatible
                && string.IsNullOrWhiteSpace(result.ModelId))
            {
                return () => Loc.Format("Settings.Status.ConnectionFailed", GetRoleLabel(target.Role), message());
            }

            return () => Loc.Format(
                "Settings.Status.ConnectionFailedDetail",
                GetRoleLabel(target.Role),
                failureCategory,
                provider,
                modelId,
                message());
        }

        /// <summary>
        /// Removes the profile's API key and endpoint from a provider message; an empty message becomes a generic failure.
        /// </summary>
        private static Func<string> DescribeProviderMessage(string message, ModelProviderProfile profile)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return static () => Loc.Get("Settings.Status.ProviderFailed");
            }

            var sanitized = ReplaceIfPresent(message, profile.ApiKey);
            sanitized = ReplaceIfPresent(sanitized, profile.Endpoint);
            return () => sanitized;
        }

        private static string ReplaceIfPresent(string value, string secret)
        {
            return string.IsNullOrWhiteSpace(secret)
                ? value
                : value.Replace(secret, "[redacted]", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDefaultEndpoint(ModelProviderType provider)
        {
            return provider switch
            {
                ModelProviderType.OpenAI => "https://api.openai.com/v1",
                ModelProviderType.Anthropic => "https://api.anthropic.com",
                ModelProviderType.OpenRouter => "https://openrouter.ai/api/v1",
                ModelProviderType.Ollama => "http://localhost:11434/v1",
                _ => string.Empty
            };
        }

        private static string NormalizeModelForProvider(ModelProviderType provider, string modelId)
        {
            if (provider == ModelProviderType.OpenAI)
            {
                if (modelId.StartsWith("openai/", StringComparison.OrdinalIgnoreCase))
                {
                    return modelId["openai/".Length..];
                }

                return string.IsNullOrWhiteSpace(modelId) || modelId.Contains('/', StringComparison.Ordinal)
                    ? "gpt-5.4-mini"
                    : modelId;
            }

            if (provider == ModelProviderType.Anthropic)
            {
                if (modelId.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase))
                {
                    return modelId["anthropic/".Length..];
                }

                return string.IsNullOrWhiteSpace(modelId) || modelId.Contains('/', StringComparison.Ordinal)
                    ? "claude-sonnet-5-5"
                    : modelId;
            }

            if (provider == ModelProviderType.Ollama)
            {
                return string.IsNullOrWhiteSpace(modelId) || modelId.Contains('/', StringComparison.Ordinal)
                    ? "qwen3"
                    : modelId;
            }

            return string.IsNullOrWhiteSpace(modelId)
                ? "openai/gpt-5.4-mini"
                : modelId;
        }

        private static IReadOnlyList<string> CreateDefaultModelOptions(string provider, string currentModel)
        {
            return ModelCatalogDefaults.GetModelIds(ParseProvider(provider), currentModel);
        }

        private static IReadOnlyList<ModelCatalogEntry> CreateDefaultModelCatalogEntries(string provider, string currentModel)
        {
            var providerType = ParseProvider(provider);
            return CreateModelCatalogEntries(providerType, CreateDefaultModelOptions(provider, currentModel), "default");
        }

        private static IReadOnlyList<ModelCatalogEntry> CreateModelCatalogEntries(
            ModelProviderType provider,
            IEnumerable<string> modelIds,
            string source)
        {
            return modelIds
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(modelId => new ModelCatalogEntry
                {
                    Provider = provider,
                    ProviderId = GetCatalogProviderId(provider, modelId),
                    ModelId = modelId,
                    DisplayName = modelId,
                    Source = source,
                    Capabilities = ["text-input", "text-output"],
                    IsAvailable = source.StartsWith("provider-live", StringComparison.OrdinalIgnoreCase)
                })
                .ToArray();
        }

        private static IReadOnlyList<ModelCatalogEntry> MergeCatalogEntries(
            IReadOnlyList<ModelCatalogEntry> models,
            IReadOnlyList<string> modelIds,
            string provider)
        {
            var modelById = models
                .Where(model => !string.IsNullOrWhiteSpace(model.ModelId))
                .GroupBy(model => model.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var providerType = ParseProvider(provider);

            return modelIds
                .Select(modelId => modelById.TryGetValue(modelId, out var model)
                    ? model
                    : new ModelCatalogEntry
                    {
                        Provider = providerType,
                        ProviderId = GetCatalogProviderId(providerType, modelId),
                        ModelId = modelId,
                        DisplayName = modelId,
                        Source = "current-selection",
                        Capabilities = ["text-input", "text-output"],
                        IsAvailable = false
                    })
                .ToArray();
        }

        private static string GetCatalogProviderId(ModelProviderType provider, string modelId)
        {
            if (provider == ModelProviderType.OpenRouter
                && modelId.Contains('/', StringComparison.Ordinal))
            {
                return modelId.Split('/')[0];
            }

            return provider switch
            {
                ModelProviderType.OpenAI => "openai",
                ModelProviderType.Anthropic => "anthropic",
                ModelProviderType.OpenRouter => "openrouter",
                ModelProviderType.Ollama => "ollama",
                _ => "openai-compatible"
            };
        }

        private static IReadOnlyList<string> MergeModelOptions(IEnumerable<string> modelIds, string currentModel)
        {
            var options = modelIds
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (string.IsNullOrWhiteSpace(currentModel)
                || options.Contains(currentModel, StringComparer.OrdinalIgnoreCase))
            {
                return options;
            }

            return options.Prepend(currentModel).ToArray();
        }

        #endregion

        #region Voice Settings

        private const string LocalSpeechEngineValue = "Local";
        private const string ApiSpeechEngineValue = "OpenAI";
        private const string WakeWordModelName = SmartVoiceAgent.Infrastructure.Services.SpeechModelCatalog.WakeWordModel;

        /// <summary>
        /// Gets the talk shortcuts offered in Settings, as Avalonia key gesture strings.
        /// </summary>
        public static IReadOnlyList<string> TalkShortcutPresets { get; } =
        [
            "Ctrl+Alt+Space",
            "Ctrl+Shift+Space",
            "Ctrl+Alt+K",
            "Pause"
        ];

        /// <summary>
        /// Gets the command that downloads the selected local Whisper model.
        /// </summary>
        public ReactiveCommand<Unit, Unit> DownloadSpeechModelCommand { get; }

        /// <summary>
        /// Gets the command that reads a sample sentence with the chosen voice and rate.
        /// </summary>
        public ReactiveCommand<Unit, Unit> PreviewSpeechCommand { get; }

        /// <summary>
        /// Gets the command that stops the sample sentence.
        /// </summary>
        public ReactiveCommand<Unit, Unit> StopSpeechPreviewCommand { get; }

        private ISpeechModelStore? _speechModelStore;
        private ITextToSpeechService? _textToSpeech;
        private bool _isTextToSpeechAvailable;
        private Func<string>? _audioErrorText;

        /// <summary>
        /// A choice in one of the voice lists, such as a spoken language or a talk shortcut. Its label follows
        /// the interface language when <see cref="RefreshLocalizedText"/> runs.
        /// </summary>
        public sealed class VoiceOption : ReactiveObject
        {
            private readonly Func<string> _label;

            /// <summary>
            /// Creates a choice.
            /// </summary>
            /// <param name="value">The value saved in settings.</param>
            /// <param name="label">Builds the label in the current language.</param>
            public VoiceOption(string value, Func<string> label)
            {
                Value = value;
                _label = label;
            }

            /// <summary>
            /// Gets the value saved in settings.
            /// </summary>
            public string Value { get; }

            /// <summary>
            /// Gets the label in the interface language.
            /// </summary>
            public string Label => _label();

            /// <summary>
            /// Re-reads the label after the interface language changes.
            /// </summary>
            public void RefreshLocalizedText() => this.RaisePropertyChanged(nameof(Label));

            /// <inheritdoc />
            public override string ToString() => Label;
        }

        /// <summary>
        /// A local Whisper model as the model list shows it: its name and download size, such as
        /// "base · 148 MB", and a one-line hint.
        /// </summary>
        public sealed class SpeechModelOption : ReactiveObject
        {
            private readonly Func<string> _hint;

            /// <summary>
            /// Creates the option for a model.
            /// </summary>
            /// <param name="name">The model name settings store, such as <c>base</c>.</param>
            /// <param name="approximateBytes">The download size.</param>
            /// <param name="hint">Builds the hint in the current language.</param>
            public SpeechModelOption(string name, long approximateBytes, Func<string> hint)
            {
                Name = name;
                ApproximateBytes = approximateBytes;
                _hint = hint;
            }

            /// <summary>
            /// Gets the model name settings store.
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// Gets the download size in bytes.
            /// </summary>
            public long ApproximateBytes { get; }

            /// <summary>
            /// Gets the name and size, such as "base · 148 MB".
            /// </summary>
            public string Label => $"{Name} · {FormatModelSize(ApproximateBytes)}";

            /// <summary>
            /// Gets what the model is good for, such as "Fast".
            /// </summary>
            public string Hint => _hint();

            /// <summary>
            /// Re-reads the size and hint after the interface language changes.
            /// </summary>
            public void RefreshLocalizedText()
            {
                this.RaisePropertyChanged(nameof(Label));
                this.RaisePropertyChanged(nameof(Hint));
            }

            /// <inheritdoc />
            public override string ToString() => Label;
        }

        /// <summary>
        /// Formats a download size in megabytes, such as "148 MB".
        /// </summary>
        /// <param name="bytes">The size in bytes.</param>
        public static string FormatModelSize(long bytes)
        {
            return Loc.Format("Settings.Voice.Model.Size", Math.Round(bytes / 1_000_000d));
        }

        /// <summary>
        /// Returns the sentence Preview reads: Turkish when the spoken language is Turkish, otherwise English.
        /// </summary>
        /// <param name="spokenLanguage">The two-letter spoken language, such as <c>tr</c>.</param>
        public static string GetSpeechPreviewSample(string spokenLanguage)
        {
            var languageCode = string.Equals(spokenLanguage, "tr", StringComparison.OrdinalIgnoreCase)
                ? "tr-TR"
                : LocalizationService.DefaultLanguage;
            return LocalizationService.LoadDictionary(languageCode).TryGetValue("Settings.Voice.Preview.Sample", out var sample)
                ? sample
                : "Hi, I'm Kam.";
        }

        /// <summary>
        /// Uses these speech services instead of the ones from the application's services. Tests pass fakes;
        /// either may be null, and the section then shows what it cannot do.
        /// </summary>
        /// <param name="speechModelStore">Downloads and finds the local Whisper models.</param>
        /// <param name="textToSpeech">Reads replies aloud.</param>
        public void UseSpeechServices(ISpeechModelStore? speechModelStore, ITextToSpeechService? textToSpeech)
        {
            _speechModelStore = speechModelStore;
            _textToSpeech = textToSpeech;
            LoadSpeechVoices();
            RefreshSpeechModelState();
            this.RaisePropertyChanged(nameof(HasSpeechModelStore));
        }

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

        /// <summary>
        /// Gets the audio device problem shown above the voice cards, in the interface language, or null.
        /// </summary>
        public string? AudioErrorMessage
        {
            get => _audioErrorMessage;
            private set => this.RaiseAndSetIfChanged(ref _audioErrorMessage, value);
        }

        /// <summary>
        /// Shows an audio device problem and keeps how it was built, so it can be shown again in another language.
        /// </summary>
        /// <param name="text">Builds the message in the current language, or null to clear it.</param>
        private void SetAudioError(Func<string>? text)
        {
            _audioErrorText = text;
            AudioErrorMessage = text?.Invoke();
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

        #region Speech recognition

        private IReadOnlyList<VoiceOption> _voiceLanguageOptions = [];
        private VoiceOption? _selectedVoiceLanguage;
        private VoiceOption? _selectedSpeechEngine;
        private IReadOnlyList<SpeechModelOption> _localSpeechModelOptions = [];
        private SpeechModelOption? _selectedLocalSpeechModel;
        private bool _isSpeechModelDownloaded;
        private bool _isDownloadingSpeechModel;
        private double _speechModelDownloadProgress;
        private Func<string>? _speechModelDownloadError;
        private string _speechApiEndpoint = string.Empty;
        private string _speechApiModel = string.Empty;
        private string _speechApiKey = string.Empty;

        /// <summary>
        /// Gets the spoken languages: the interface language, automatic detection, Turkish and English.
        /// </summary>
        public IReadOnlyList<VoiceOption> VoiceLanguageOptions
        {
            get => _voiceLanguageOptions;
            private set => this.RaiseAndSetIfChanged(ref _voiceLanguageOptions, value);
        }

        /// <summary>
        /// Gets or sets the spoken language. Changing it saves <see cref="ISettingsService.VoiceLanguage"/>.
        /// </summary>
        public VoiceOption? SelectedVoiceLanguage
        {
            get => _selectedVoiceLanguage;
            set
            {
                if (value is null || ReferenceEquals(_selectedVoiceLanguage, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedVoiceLanguage, value);
                _settingsService.VoiceLanguage = value.Value;
            }
        }

        /// <summary>
        /// Gets where speech is turned into text: on this computer or through an OpenAI-compatible API.
        /// </summary>
        public IReadOnlyList<VoiceOption> SpeechEngineOptions { get; } =
        [
            new(LocalSpeechEngineValue, static () => Loc.Get("Settings.Voice.Engine.Local")),
            new(ApiSpeechEngineValue, static () => Loc.Get("Settings.Voice.Engine.Api"))
        ];

        /// <summary>
        /// Gets or sets the speech engine. Changing it saves <see cref="ISettingsService.SpeechEngine"/>.
        /// </summary>
        public VoiceOption? SelectedSpeechEngine
        {
            get => _selectedSpeechEngine;
            set
            {
                if (value is null || ReferenceEquals(_selectedSpeechEngine, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedSpeechEngine, value);
                _settingsService.SpeechEngine = value.Value;
                this.RaisePropertyChanged(nameof(IsLocalSpeechEngine));
                this.RaisePropertyChanged(nameof(IsApiSpeechEngine));
            }
        }

        /// <summary>
        /// Gets whether Whisper runs on this computer.
        /// </summary>
        public bool IsLocalSpeechEngine => !IsApiSpeechEngine;

        /// <summary>
        /// Gets whether recordings go to the OpenAI-compatible API.
        /// </summary>
        public bool IsApiSpeechEngine => _selectedSpeechEngine?.Value == ApiSpeechEngineValue;

        /// <summary>
        /// Gets the local models that transcribe commands. The tiny model is kept for the wake phrase.
        /// </summary>
        public IReadOnlyList<SpeechModelOption> LocalSpeechModelOptions
        {
            get => _localSpeechModelOptions;
            private set => this.RaiseAndSetIfChanged(ref _localSpeechModelOptions, value);
        }

        /// <summary>
        /// Gets or sets the local model. Changing it saves <see cref="ISettingsService.LocalSpeechModel"/>.
        /// </summary>
        public SpeechModelOption? SelectedLocalSpeechModel
        {
            get => _selectedLocalSpeechModel;
            set
            {
                if (value is null || ReferenceEquals(_selectedLocalSpeechModel, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedLocalSpeechModel, value);
                _settingsService.LocalSpeechModel = value.Name;
                SetSpeechModelDownloadError(null);
                RefreshSpeechModelState();
            }
        }

        /// <summary>
        /// Gets whether models can be downloaded from here.
        /// </summary>
        public bool HasSpeechModelStore => _speechModelStore is not null;

        /// <summary>
        /// Gets whether the selected local model is on this computer.
        /// </summary>
        public bool IsSpeechModelDownloaded
        {
            get => _isSpeechModelDownloaded;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isSpeechModelDownloaded, value);
                RaiseSpeechModelStateChanged();
            }
        }

        /// <summary>
        /// Gets whether a model download started here is running.
        /// </summary>
        public bool IsDownloadingSpeechModel
        {
            get => _isDownloadingSpeechModel;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isDownloadingSpeechModel, value);
                RaiseSpeechModelStateChanged();
            }
        }

        /// <summary>
        /// Gets the download progress from 0 to 1.
        /// </summary>
        public double SpeechModelDownloadProgress
        {
            get => _speechModelDownloadProgress;
            private set
            {
                this.RaiseAndSetIfChanged(ref _speechModelDownloadProgress, value);
                this.RaisePropertyChanged(nameof(SpeechModelDownloadPercent));
            }
        }

        /// <summary>
        /// Gets the download progress as a percentage, such as "42%".
        /// </summary>
        public string SpeechModelDownloadPercent => Loc.Format("Settings.Voice.Model.Percent", _speechModelDownloadProgress);

        /// <summary>
        /// Gets whether the Download button can start a download.
        /// </summary>
        public bool CanDownloadSpeechModel => HasSpeechModelStore
            && !_isDownloadingSpeechModel
            && !_isSpeechModelDownloaded
            && _selectedLocalSpeechModel is not null;

        /// <summary>
        /// Gets whether the selected model is on this computer, being downloaded or downloads on first use.
        /// </summary>
        public string SpeechModelStatus => _isDownloadingSpeechModel
            ? Loc.Get("Settings.Voice.Model.Downloading")
            : _isSpeechModelDownloaded
                ? Loc.Get("Settings.Voice.Model.Downloaded")
                : Loc.Get("Settings.Voice.Model.NotDownloaded");

        /// <summary>
        /// Gets why the last download failed, or null.
        /// </summary>
        public string? SpeechModelDownloadError => _speechModelDownloadError?.Invoke();

        /// <summary>
        /// Gets whether the last download failed.
        /// </summary>
        public bool HasSpeechModelDownloadError => _speechModelDownloadError is not null;

        /// <summary>
        /// Gets or sets the base URL of the OpenAI-compatible transcription API.
        /// </summary>
        public string SpeechApiEndpoint
        {
            get => _speechApiEndpoint;
            set
            {
                if (_speechApiEndpoint != value)
                {
                    this.RaiseAndSetIfChanged(ref _speechApiEndpoint, value);
                    _settingsService.SpeechApiEndpoint = value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Gets or sets the transcription model of the OpenAI-compatible API.
        /// </summary>
        public string SpeechApiModel
        {
            get => _speechApiModel;
            set
            {
                if (_speechApiModel != value)
                {
                    this.RaiseAndSetIfChanged(ref _speechApiModel, value);
                    _settingsService.SpeechApiModel = value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Gets or sets the API key of the transcription API. It is saved in the secret store.
        /// </summary>
        public string SpeechApiKey
        {
            get => _speechApiKey;
            set
            {
                if (_speechApiKey != value)
                {
                    this.RaiseAndSetIfChanged(ref _speechApiKey, value);
                    this.RaisePropertyChanged(nameof(MaskedSpeechApiKey));
                    _settingsService.SpeechApiKey = value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Gets the transcription API key with most characters hidden.
        /// </summary>
        public string MaskedSpeechApiKey => new ModelProviderProfile { ApiKey = _speechApiKey }.MaskedApiKey;

        /// <summary>
        /// Downloads the selected local model. Does nothing when it is already on this computer or a download runs.
        /// </summary>
        public async Task DownloadSpeechModelAsync()
        {
            var store = _speechModelStore;
            var model = _selectedLocalSpeechModel;
            if (store is null || model is null || _isDownloadingSpeechModel)
            {
                return;
            }

            SetSpeechModelDownloadError(null);
            if (store.IsDownloaded(model.Name))
            {
                RefreshSpeechModelState();
                return;
            }

            SpeechModelDownloadProgress = 0;
            IsDownloadingSpeechModel = true;
            var progress = new Progress<double>(value =>
            {
                if (_isDownloadingSpeechModel)
                {
                    SpeechModelDownloadProgress = Math.Clamp(value, 0, 1);
                }
            });

            try
            {
                await store.EnsureModelAsync(model.Name, progress).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                SetSpeechModelDownloadError(() => Loc.Format("Settings.Voice.Model.DownloadFailed", message));
            }
            finally
            {
                IsDownloadingSpeechModel = false;
                RefreshSpeechModelState();
            }
        }

        private void SetSpeechModelDownloadError(Func<string>? text)
        {
            _speechModelDownloadError = text;
            this.RaisePropertyChanged(nameof(SpeechModelDownloadError));
            this.RaisePropertyChanged(nameof(HasSpeechModelDownloadError));
        }

        private void RefreshSpeechModelState()
        {
            var model = _selectedLocalSpeechModel?.Name;
            bool downloaded;
            try
            {
                downloaded = model is not null && _speechModelStore?.IsDownloaded(model) == true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Speech model state could not be read: {ex.Message}");
                downloaded = false;
            }

            IsSpeechModelDownloaded = downloaded;
        }

        private void RaiseSpeechModelStateChanged()
        {
            this.RaisePropertyChanged(nameof(SpeechModelStatus));
            this.RaisePropertyChanged(nameof(CanDownloadSpeechModel));
        }

        private static IReadOnlyList<VoiceOption> CreateVoiceLanguageOptions(string savedLanguage)
        {
            var options = new List<VoiceOption>
            {
                new(string.Empty, static () => Loc.Get("Settings.Voice.Language.Interface")),
                new(AiRuntimeConfigurationMapper.AutoSpokenLanguage, static () => Loc.Get("Settings.Voice.Language.Auto")),
                new("tr", static () => "Türkçe"),
                new("en", static () => "English")
            };

            if (!options.Any(option => option.Value.Equals(savedLanguage, StringComparison.OrdinalIgnoreCase)))
            {
                options.Add(new VoiceOption(savedLanguage, () => savedLanguage));
            }

            return options;
        }

        private static IReadOnlyList<SpeechModelOption> CreateLocalSpeechModelOptions(string savedModel)
        {
            return SmartVoiceAgent.Infrastructure.Services.SpeechModelCatalog.All
                .Where(model => model.Name != WakeWordModelName || model.Name == savedModel)
                .Select(model => new SpeechModelOption(model.Name, model.ApproximateBytes, GetSpeechModelHint(model.Name)))
                .ToArray();
        }

        private static Func<string> GetSpeechModelHint(string model)
        {
            return model switch
            {
                WakeWordModelName => static () => Loc.Get("Settings.Voice.Model.Hint.Tiny"),
                "small" => static () => Loc.Get("Settings.Voice.Model.Hint.Small"),
                "large-v3-turbo" => static () => Loc.Get("Settings.Voice.Model.Hint.LargeV3Turbo"),
                _ => static () => Loc.Get("Settings.Voice.Model.Hint.Base")
            };
        }

        private void InitializeSpeechRecognitionSettings()
        {
            var savedLanguage = _settingsService.VoiceLanguage?.Trim() ?? string.Empty;
            _voiceLanguageOptions = CreateVoiceLanguageOptions(savedLanguage);
            _selectedVoiceLanguage = _voiceLanguageOptions.First(option =>
                option.Value.Equals(savedLanguage, StringComparison.OrdinalIgnoreCase));

            _selectedSpeechEngine = SpeechEngineOptions.FirstOrDefault(option =>
                    option.Value.Equals(_settingsService.SpeechEngine?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? SpeechEngineOptions[0];

            var savedModel = SmartVoiceAgent.Infrastructure.Services.SpeechModelCatalog.Normalize(_settingsService.LocalSpeechModel);
            _localSpeechModelOptions = CreateLocalSpeechModelOptions(savedModel);
            _selectedLocalSpeechModel = _localSpeechModelOptions.First(option => option.Name == savedModel);

            _speechApiEndpoint = _settingsService.SpeechApiEndpoint ?? string.Empty;
            _speechApiModel = _settingsService.SpeechApiModel ?? string.Empty;
            _speechApiKey = _settingsService.SpeechApiKey ?? string.Empty;
        }

        #endregion

        #region Hands-free

        private bool _wakeWordEnabled;
        private string _wakeWord = string.Empty;
        private IReadOnlyList<VoiceOption> _talkShortcutOptions = [];
        private VoiceOption? _selectedTalkShortcut;

        /// <summary>
        /// Gets or sets whether Kam listens for the wake phrase. Changing it saves <see cref="ISettingsService.WakeWordEnabled"/>.
        /// </summary>
        public bool WakeWordEnabled
        {
            get => _wakeWordEnabled;
            set
            {
                if (_wakeWordEnabled != value)
                {
                    this.RaiseAndSetIfChanged(ref _wakeWordEnabled, value);
                    _settingsService.WakeWordEnabled = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the wake phrase, such as "Hey Kam".
        /// </summary>
        public string WakeWord
        {
            get => _wakeWord;
            set
            {
                if (_wakeWord != value)
                {
                    this.RaiseAndSetIfChanged(ref _wakeWord, value);
                    _settingsService.WakeWord = value ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Gets what the wake phrase listener does with audio, including the size of the model it downloads.
        /// </summary>
        public string WakeWordNote => Loc.Format(
            "Settings.Voice.WakeWord.Note",
            FormatModelSize(SmartVoiceAgent.Infrastructure.Services.SpeechModelCatalog.Get(WakeWordModelName).ApproximateBytes));

        /// <summary>
        /// Gets the talk shortcuts: the presets, a saved shortcut that is not one of them, and Off.
        /// </summary>
        public IReadOnlyList<VoiceOption> TalkShortcutOptions
        {
            get => _talkShortcutOptions;
            private set => this.RaiseAndSetIfChanged(ref _talkShortcutOptions, value);
        }

        /// <summary>
        /// Gets or sets the talk shortcut. Changing it saves <see cref="ISettingsService.TalkShortcut"/>; Off saves an empty value.
        /// </summary>
        public VoiceOption? SelectedTalkShortcut
        {
            get => _selectedTalkShortcut;
            set
            {
                if (value is null || ReferenceEquals(_selectedTalkShortcut, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedTalkShortcut, value);
                _settingsService.TalkShortcut = value.Value;
            }
        }

        private static IReadOnlyList<VoiceOption> CreateTalkShortcutOptions(string savedShortcut)
        {
            var options = TalkShortcutPresets
                .Select(shortcut => new VoiceOption(shortcut, () => shortcut))
                .ToList();

            if (!string.IsNullOrWhiteSpace(savedShortcut) && FindShortcut(options, savedShortcut) is null)
            {
                options.Add(new VoiceOption(savedShortcut, () => savedShortcut));
            }

            options.Add(new VoiceOption(string.Empty, static () => Loc.Get("Settings.Voice.Shortcut.Off")));
            return options;
        }

        private static VoiceOption? FindShortcut(IEnumerable<VoiceOption> options, string shortcut)
        {
            var wanted = shortcut.Replace(" ", string.Empty, StringComparison.Ordinal);
            return options.FirstOrDefault(option =>
                option.Value.Replace(" ", string.Empty, StringComparison.Ordinal).Equals(wanted, StringComparison.OrdinalIgnoreCase));
        }

        private void InitializeHandsFreeSettings()
        {
            _wakeWordEnabled = _settingsService.WakeWordEnabled;
            _wakeWord = _settingsService.WakeWord ?? string.Empty;

            var savedShortcut = _settingsService.TalkShortcut?.Trim() ?? string.Empty;
            _talkShortcutOptions = CreateTalkShortcutOptions(savedShortcut);
            _selectedTalkShortcut = FindShortcut(_talkShortcutOptions, savedShortcut) ?? _talkShortcutOptions[^1];
        }

        #endregion

        #region Spoken replies

        private VoiceOption? _selectedSpokenReplies;
        private IReadOnlyList<VoiceOption> _speechVoiceOptions = [];
        private VoiceOption? _selectedSpeechVoice;
        private int _installedSpeechVoiceCount;
        private int _speechRate;
        private bool _isPreviewingSpeech;

        /// <summary>
        /// Gets which replies are read aloud: none, replies to voice commands, or all replies.
        /// </summary>
        public IReadOnlyList<VoiceOption> SpokenRepliesOptions { get; } =
        [
            new("Off", static () => Loc.Get("Settings.Voice.SpokenReplies.Off")),
            new("Voice", static () => Loc.Get("Settings.Voice.SpokenReplies.Voice")),
            new("All", static () => Loc.Get("Settings.Voice.SpokenReplies.All"))
        ];

        /// <summary>
        /// Gets or sets which replies are read aloud. Changing it saves <see cref="ISettingsService.SpokenReplies"/>.
        /// </summary>
        public VoiceOption? SelectedSpokenReplies
        {
            get => _selectedSpokenReplies;
            set
            {
                if (value is null || ReferenceEquals(_selectedSpokenReplies, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedSpokenReplies, value);
                _settingsService.SpokenReplies = value.Value;
            }
        }

        /// <summary>
        /// Gets the voices replies can be read in: Automatic, then the installed voices.
        /// </summary>
        public IReadOnlyList<VoiceOption> SpeechVoiceOptions
        {
            get => _speechVoiceOptions;
            private set => this.RaiseAndSetIfChanged(ref _speechVoiceOptions, value);
        }

        /// <summary>
        /// Gets or sets the voice. Changing it saves <see cref="ISettingsService.SpeechVoice"/>; Automatic saves an empty value.
        /// </summary>
        public VoiceOption? SelectedSpeechVoice
        {
            get => _selectedSpeechVoice;
            set
            {
                if (value is null || ReferenceEquals(_selectedSpeechVoice, value))
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _selectedSpeechVoice, value);
                _settingsService.SpeechVoice = value.Value;
            }
        }

        /// <summary>
        /// Gets or sets the speaking rate from -5 (slow) to 5 (fast). Changing it saves <see cref="ISettingsService.SpeechRate"/>.
        /// </summary>
        public double SpeechRate
        {
            get => _speechRate;
            set
            {
                var rate = (int)Math.Round(Math.Clamp(double.IsNaN(value) ? 0 : value, -5, 5), MidpointRounding.AwayFromZero);
                if (_speechRate != rate)
                {
                    _speechRate = rate;
                    this.RaisePropertyChanged();
                    this.RaisePropertyChanged(nameof(SpeechRateText));
                    _settingsService.SpeechRate = rate;
                }
            }
        }

        /// <summary>
        /// Gets the speaking rate as shown next to the slider: "Normal" or a signed step such as "+2".
        /// </summary>
        public string SpeechRateText => _speechRate == 0
            ? Loc.Get("Settings.Voice.Rate.Normal")
            : _speechRate.ToString("+0;-0", LocalizationService.Instance.Culture);

        /// <summary>
        /// Gets whether this computer has a speech engine.
        /// </summary>
        public bool IsTextToSpeechAvailable => _isTextToSpeechAvailable;

        /// <summary>
        /// Gets whether replies can be read aloud: a speech engine with at least one voice.
        /// </summary>
        public bool CanUseSpeechOutput => _isTextToSpeechAvailable && _installedSpeechVoiceCount > 0;

        /// <summary>
        /// Gets why replies cannot be read aloud, or null when they can.
        /// </summary>
        public string? SpeechOutputProblem => !_isTextToSpeechAvailable
            ? Loc.Get("Settings.Voice.Tts.Unavailable")
            : _installedSpeechVoiceCount == 0
                ? Loc.Get("Settings.Voice.Tts.NoVoices")
                : null;

        /// <summary>
        /// Gets whether <see cref="SpeechOutputProblem"/> has a message.
        /// </summary>
        public bool HasSpeechOutputProblem => !CanUseSpeechOutput;

        /// <summary>
        /// Gets whether the preview sentence is being read.
        /// </summary>
        public bool IsPreviewingSpeech
        {
            get => _isPreviewingSpeech;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isPreviewingSpeech, value);
                this.RaisePropertyChanged(nameof(CanPreviewSpeech));
            }
        }

        /// <summary>
        /// Gets whether Preview can start.
        /// </summary>
        public bool CanPreviewSpeech => CanUseSpeechOutput && !_isPreviewingSpeech;

        /// <summary>
        /// Gets the two-letter language replies are spoken in: the chosen spoken language, or the interface
        /// language when it follows the interface or is detected automatically.
        /// </summary>
        public string EffectiveSpokenLanguage
        {
            get
            {
                var language = AiRuntimeConfigurationMapper.ResolveSpokenLanguage(_settingsService);
                return language == AiRuntimeConfigurationMapper.AutoSpokenLanguage
                    ? LocalizationService.Instance.CurrentLanguage.Split('-')[0]
                    : language;
            }
        }

        /// <summary>
        /// Reads a short sample sentence in the spoken language with the chosen voice and rate.
        /// </summary>
        public async Task PreviewSpeechAsync()
        {
            var speech = _textToSpeech;
            if (speech is null || !CanUseSpeechOutput)
            {
                return;
            }

            IsPreviewingSpeech = true;
            try
            {
                await speech.SpeakAsync(GetSpeechPreviewSample(EffectiveSpokenLanguage)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Speech preview failed: {ex.Message}");
            }
            finally
            {
                IsPreviewingSpeech = false;
            }
        }

        /// <summary>
        /// Stops the preview sentence.
        /// </summary>
        public void StopSpeechPreview()
        {
            _textToSpeech?.Stop();
        }

        private void LoadSpeechVoices()
        {
            IReadOnlyList<SpeechVoiceInfo> voices = [];
            try
            {
                _isTextToSpeechAvailable = _textToSpeech?.IsAvailable == true;
                if (_isTextToSpeechAvailable)
                {
                    voices = _textToSpeech!.GetVoices();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Speech voices could not be listed: {ex.Message}");
                _isTextToSpeechAvailable = false;
            }

            _installedSpeechVoiceCount = voices.Count;
            var savedVoice = _settingsService.SpeechVoice?.Trim() ?? string.Empty;
            var options = new List<VoiceOption>
            {
                new(string.Empty, static () => Loc.Get("Settings.Voice.Voice.Automatic"))
            };

            foreach (var voice in voices.Where(voice => !string.IsNullOrWhiteSpace(voice.Id)))
            {
                var label = string.IsNullOrWhiteSpace(voice.Language)
                    ? voice.Name
                    : $"{voice.Name} ({voice.Language})";
                options.Add(new VoiceOption(voice.Id, () => label));
            }

            if (savedVoice.Length > 0 && options.All(option => option.Value != savedVoice))
            {
                var name = savedVoice.Split('\\', '/').Last();
                options.Add(new VoiceOption(savedVoice, () => Loc.Format("Settings.Voice.Voice.Missing", name)));
            }

            _speechVoiceOptions = options;
            _selectedSpeechVoice = options.First(option => option.Value == savedVoice);
            this.RaisePropertyChanged(nameof(SpeechVoiceOptions));
            this.RaisePropertyChanged(nameof(SelectedSpeechVoice));
            this.RaisePropertyChanged(nameof(IsTextToSpeechAvailable));
            this.RaisePropertyChanged(nameof(CanUseSpeechOutput));
            this.RaisePropertyChanged(nameof(SpeechOutputProblem));
            this.RaisePropertyChanged(nameof(HasSpeechOutputProblem));
            this.RaisePropertyChanged(nameof(CanPreviewSpeech));
        }

        private void InitializeSpokenRepliesSettings()
        {
            _selectedSpokenReplies = SpokenRepliesOptions.FirstOrDefault(option =>
                    option.Value.Equals(_settingsService.SpokenReplies?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? SpokenRepliesOptions[1];
            _speechRate = Math.Clamp(_settingsService.SpeechRate, -5, 5);
            LoadSpeechVoices();
        }

        #endregion

        private void InitializeVoiceSettings()
        {
            _speechModelStore = App.Services?.GetService(typeof(ISpeechModelStore)) as ISpeechModelStore;
            _textToSpeech = App.Services?.GetService(typeof(ITextToSpeechService)) as ITextToSpeechService;

            InitializeSpeechRecognitionSettings();
            InitializeHandsFreeSettings();
            InitializeSpokenRepliesSettings();
            RefreshSpeechModelState();

            // Check audio availability
            IsAudioAvailable = _audioDeviceService.IsAvailable;
            if (!IsAudioAvailable)
            {
                SetAudioError(static () => Loc.Get("Settings.Voice.AudioUnavailable"));
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
                _settingsService.SelectedInputDeviceId = string.Empty;
            }

            if (!string.IsNullOrEmpty(savedOutputId) && _audioDeviceService.IsDeviceAvailable(savedOutputId))
            {
                SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == savedOutputId);
            }
            else if (!string.IsNullOrEmpty(savedOutputId))
            {
                // Saved device no longer available, clear it
                _settingsService.SelectedOutputDeviceId = string.Empty;
            }

            _settingsService.SettingChanged += OnVoiceSettingChanged;
            LocalizationService.Instance.LanguageChanged += OnVoiceLanguageChanged;

            // The input meter runs only while the Settings page is open; see OnNavigatedTo.
        }

        /// <summary>
        /// Keeps the toggles in step when the tray or the chat changes a voice setting while the page is open.
        /// </summary>
        private void OnVoiceSettingChanged(object? sender, SettingChangedEventArgs e)
        {
            if (e.SettingName != nameof(ISettingsService.WakeWordEnabled)
                && e.SettingName != nameof(ISettingsService.SpokenReplies))
            {
                return;
            }

            if (_wakeWordEnabled == _settingsService.WakeWordEnabled
                && string.Equals(_selectedSpokenReplies?.Value, _settingsService.SpokenReplies, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnVoiceSettingChanged(sender, e));
                return;
            }

            if (_wakeWordEnabled != _settingsService.WakeWordEnabled)
            {
                _wakeWordEnabled = _settingsService.WakeWordEnabled;
                this.RaisePropertyChanged(nameof(WakeWordEnabled));
            }

            var spokenReplies = SpokenRepliesOptions.FirstOrDefault(option =>
                option.Value.Equals(_settingsService.SpokenReplies, StringComparison.OrdinalIgnoreCase));
            if (spokenReplies is not null && !ReferenceEquals(spokenReplies, _selectedSpokenReplies))
            {
                _selectedSpokenReplies = spokenReplies;
                this.RaisePropertyChanged(nameof(SelectedSpokenReplies));
            }
        }

        /// <summary>
        /// Rebuilds the voice text built in code when the interface language changes.
        /// </summary>
        private void OnVoiceLanguageChanged(object? sender, EventArgs e)
        {
            foreach (var option in _voiceLanguageOptions
                         .Concat(SpeechEngineOptions)
                         .Concat(_talkShortcutOptions)
                         .Concat(SpokenRepliesOptions)
                         .Concat(_speechVoiceOptions))
            {
                option.RefreshLocalizedText();
            }

            foreach (var model in _localSpeechModelOptions)
            {
                model.RefreshLocalizedText();
            }

            AudioErrorMessage = _audioErrorText?.Invoke();
            this.RaisePropertyChanged(nameof(SpeechModelStatus));
            this.RaisePropertyChanged(nameof(SpeechModelDownloadPercent));
            this.RaisePropertyChanged(nameof(SpeechModelDownloadError));
            this.RaisePropertyChanged(nameof(WakeWordNote));
            this.RaisePropertyChanged(nameof(SpeechRateText));
            this.RaisePropertyChanged(nameof(SpeechOutputProblem));
        }

        /// <summary>
        /// Stops listening for setting and language changes and stops a running preview.
        /// </summary>
        private void DisposeVoiceSettings()
        {
            _settingsService.SettingChanged -= OnVoiceSettingChanged;
            LocalizationService.Instance.LanguageChanged -= OnVoiceLanguageChanged;
            if (_isPreviewingSpeech)
            {
                _textToSpeech?.Stop();
            }
        }

        /// <summary>
        /// Starts the microphone level meter while the page is shown and re-reads whether the model is downloaded.
        /// </summary>
        public override void OnNavigatedTo()
        {
            base.OnNavigatedTo();
            RefreshSpeechModelState();
            StartInputLevelMonitoring();
        }

        /// <summary>
        /// Stops the microphone level meter, which otherwise polls the device ten times a second.
        /// </summary>
        public override void OnNavigatedFrom()
        {
            StopInputLevelMonitoring();
            base.OnNavigatedFrom();
        }

        /// <summary>
        /// Gets whether the input level meter is polling the microphone.
        /// </summary>
        public bool IsInputLevelMonitoring => _inputLevelCts is { IsCancellationRequested: false };

        private void OnDevicesChanged(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Check if selected devices are still available
                if (SelectedInputDevice != null && !_audioDeviceService.IsDeviceAvailable(SelectedInputDevice.Id))
                {
                    // Device disconnected, select default
                    RefreshAudioDevices();
                    SetAudioError(static () => Loc.Get("Settings.Voice.MicrophoneDisconnected"));
                }
                else if (SelectedOutputDevice != null && !_audioDeviceService.IsDeviceAvailable(SelectedOutputDevice.Id))
                {
                    RefreshAudioDevices();
                    SetAudioError(static () => Loc.Get("Settings.Voice.OutputDisconnected"));
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
                SetAudioError(null);
            }
        }

        private void StartInputLevelMonitoring()
        {
            _inputLevelCts?.Cancel();
            _inputLevelCts = new CancellationTokenSource();
            var token = _inputLevelCts.Token;

            Task.Run(async () =>
            {
                // Performance: Throttle to 10 FPS (100ms) instead of 20 FPS to reduce UI thread load
                // This is still smooth enough for VU meter visualization
                const int updateIntervalMs = 100;
                float lastLevel = 0;

                while (!token.IsCancellationRequested)
                {
                    if (SelectedInputDevice != null && !IsRecordingTest)
                    {
                        var level = _audioDeviceService.GetInputLevel(SelectedInputDevice.Id);

                        // Only update UI if level changed significantly (> 0.05) or on every 5th update
                        // This reduces unnecessary UI refreshes
                        if (Math.Abs(level - lastLevel) > 0.05f || Environment.TickCount % 5 == 0)
                        {
                            lastLevel = level;
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (!token.IsCancellationRequested)
                                {
                                    InputLevel = level;
                                }
                            });
                        }
                    }
                    await Task.Delay(updateIntervalMs, token);
                }
            }, token);
        }

        private void StopInputLevelMonitoring()
        {
            _inputLevelCts?.Cancel();
            _inputLevelCts = null;
            InputLevel = 0;
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

        /// <summary>
        /// Gets the languages the interface can be shown in.
        /// </summary>
        public IReadOnlyList<LanguageOption> Languages => LocalizationService.SupportedLanguages;

        /// <summary>
        /// Gets or sets the interface language. Changing it saves the choice and switches the text at once.
        /// </summary>
        public LanguageOption SelectedLanguage
        {
            get
            {
                var code = LocalizationService.Instance.CurrentLanguage;
                return Languages.FirstOrDefault(language => language.Code == code) ?? Languages[0];
            }
            set
            {
                if (value is null || value.Code == LocalizationService.Instance.CurrentLanguage)
                {
                    return;
                }

                _settingsService.Language = value.Code;
                LocalizationService.Instance.SetLanguage(value.Code);
                this.RaisePropertyChanged();
            }
        }

        /// <summary>
        /// Rebuilds the text this view model writes in code when the interface language changes.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = Loc.Get("Settings.Title");
            AiProfileStatus = _aiProfileStatusText();
            this.RaisePropertyChanged(nameof(AiConnectionTestButtonText));
            this.RaisePropertyChanged(nameof(SelectedLanguage));
        }

        #endregion

        #region Startup Behavior

        /// <summary>
        /// Controls whether the application starts automatically with Windows
        /// </summary>
        public bool AutoStart
        {
            get => _autoStartRegistrationService.IsEnabled(_settingsService.AutoStart);
            set
            {
                var current = _autoStartRegistrationService.IsEnabled(_settingsService.AutoStart);
                if (current != value)
                {
                    _settingsService.AutoStart = value;
                    this.RaisePropertyChanged();
                    _autoStartRegistrationService.SetEnabled(value, GetExecutablePath());
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
            DisposeVoiceSettings();
            if (_ownsModelCatalogService && _modelCatalogService is IDisposable disposableModelCatalogService)
            {
                disposableModelCatalogService.Dispose();
            }

            if (_ownsModelConnectionTestService && _modelConnectionTestService is IDisposable disposableConnectionTestService)
            {
                disposableConnectionTestService.Dispose();
            }

            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }
    }
}

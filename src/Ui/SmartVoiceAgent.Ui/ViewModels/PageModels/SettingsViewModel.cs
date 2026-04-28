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
        private readonly bool _ownsModelCatalogService;
        private readonly bool _ownsModelConnectionTestService;
        private readonly AudioDeviceService _audioDeviceService;
        private readonly VoiceTestService? _voiceTestService;
        private int _selectedLanguageIndex;
        private CancellationTokenSource? _inputLevelCts;

        public ReactiveCommand<Unit, Unit> StartMicTestCommand { get; }
        public ReactiveCommand<Unit, Unit> StopMicTestCommand { get; }
        public ReactiveCommand<Unit, Unit> PlayTestRecordingCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshDevicesCommand { get; }
        public ReactiveCommand<Unit, Unit> TestAiConnectionCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshAiModelsCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshChatModelsCommand { get; }

        public SettingsViewModel() : this(new JsonSettingsService(), null, null, null)
        {
        }

        public SettingsViewModel(ISettingsService settingsService) : this(settingsService, null, null, null)
        {
        }

        public SettingsViewModel(ISettingsService settingsService, IModelCatalogService modelCatalogService)
            : this(settingsService, null, modelCatalogService, null)
        {
        }

        public SettingsViewModel(
            ISettingsService settingsService,
            IModelCatalogService modelCatalogService,
            IModelConnectionTestService modelConnectionTestService)
            : this(settingsService, null, modelCatalogService, modelConnectionTestService)
        {
        }

        public SettingsViewModel(MainWindowViewModel mainViewModel) : this(new JsonSettingsService(), mainViewModel, null, null)
        {
        }

        private SettingsViewModel(
            ISettingsService settingsService,
            MainWindowViewModel? mainViewModel,
            IModelCatalogService? modelCatalogService,
            IModelConnectionTestService? modelConnectionTestService)
        {
            _mainViewModel = mainViewModel;
            Title = "SETTINGS";
            _selectedLanguageIndex = mainViewModel?.SelectedLanguageIndex ?? 0;
            _settingsService = settingsService;
            _modelCatalogService = modelCatalogService ?? CompositeModelCatalogService.CreateDefault();
            _modelConnectionTestService = modelConnectionTestService ?? new ModelConnectionTestService();
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
        private string _chatProvider = "OpenRouter";
        private string _chatEndpoint = "https://openrouter.ai/api/v1";
        private string _chatModelId = "openai/gpt-4.1-mini";
        private string _chatApiKey = string.Empty;
        private string _activeChatProfileId = "openrouter-chat";
        private string _aiProfileStatus = "Profile not validated.";
        private bool _isAiProfileValid;
        private IReadOnlyList<string> _aiModelOptions = CreateDefaultModelOptions("OpenRouter", "openai/gpt-4.1-mini");
        private IReadOnlyList<string> _chatModelOptions = CreateDefaultModelOptions("OpenRouter", "openai/gpt-4.1-mini");
        private IReadOnlyList<ModelCatalogEntry> _aiModelCatalogEntries = CreateDefaultModelCatalogEntries("OpenRouter", "openai/gpt-4.1-mini");
        private IReadOnlyList<ModelCatalogEntry> _chatModelCatalogEntries = CreateDefaultModelCatalogEntries("OpenRouter", "openai/gpt-4.1-mini");
        private bool _isRefreshingAiModels;
        private bool _isRefreshingChatModels;
        private bool _isTestingAiConnection;
        private bool _isPlannerModelCatalogBacked = true;
        private bool _isChatModelCatalogBacked = true;

        public IReadOnlyList<string> AiProviders { get; } =
        [
            "OpenAI",
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
            ? "Testing..."
            : "Test Connection";

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
                .SelectMany(target => ValidateProfileForConnectionTest(target.Label, target.Profile, target.Required))
                .ToArray();

            if (validationErrors.Length > 0)
            {
                IsAiProfileValid = false;
                AiProfileStatus = string.Join(" ", validationErrors);
                SaveAiProfileSettings();
                return;
            }

            try
            {
                IsTestingAiConnection = true;
                AiProfileStatus = "Testing provider connection...";
                var results = new List<string>();

                foreach (var target in targets.Where(target => target.Required || ShouldTestOptionalProfile(target.Profile)))
                {
                    var result = await _modelConnectionTestService.TestAsync(target.Profile).ConfigureAwait(true);
                    if (!result.Success)
                    {
                        IsAiProfileValid = false;
                        AiProfileStatus = $"{target.Label} connection failed: {result.Message}";
                        SaveAiProfileSettings();
                        return;
                    }

                    results.Add($"{target.Label} returned {result.LiveModelCount} live models");
                }

                IsAiProfileValid = true;
                AiProfileStatus = $"Connection verified: {string.Join("; ", results)}. Restart Kam to apply runtime changes.";
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
                AiProfileStatus = "Custom OpenAI-compatible providers keep manual model entry enabled.";
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
                AiProfileStatus = hasLiveAvailability
                    ? $"{(isPlanner ? "Planner" : "Chat")} model list loaded from {profile.Provider}."
                    : $"{(isPlanner ? "Planner" : "Chat")} model registry loaded. Add an API key to verify live availability.";
                SaveAiProfileSettings();
            }
            catch (Exception ex)
            {
                SetModelOptions(role, CreateDefaultModelCatalogEntries(profile.Provider.ToString(), profile.ModelId));
                AiProfileStatus = $"Model list could not be loaded: {ex.Message}";
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
                ModelId = "openai/gpt-4.1-mini",
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
                ModelId = "openai/gpt-4.1-mini",
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
                or ModelProviderType.OpenRouter
                or ModelProviderType.Ollama;
        }

        private static IReadOnlyList<ConnectionTestTarget> CreateConnectionTestTargets(
            ModelProviderProfile plannerProfile,
            ModelProviderProfile chatProfile)
        {
            return
            [
                new ConnectionTestTarget("Planner", plannerProfile, Required: true),
                new ConnectionTestTarget("Chat", chatProfile, Required: false)
            ];
        }

        private static IEnumerable<string> ValidateProfileForConnectionTest(
            string label,
            ModelProviderProfile profile,
            bool required)
        {
            if (!required && !ShouldTestOptionalProfile(profile))
            {
                yield break;
            }

            foreach (var error in profile.Validate().Errors)
            {
                yield return $"{label}: {error}";
            }

            if (profile.Provider != ModelProviderType.Ollama && string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                yield return $"{label}: API key is required to test this provider.";
            }
        }

        private static bool ShouldTestOptionalProfile(ModelProviderProfile profile)
        {
            return profile.Provider == ModelProviderType.Ollama
                || !string.IsNullOrWhiteSpace(profile.ApiKey);
        }

        private sealed record ConnectionTestTarget(
            string Label,
            ModelProviderProfile Profile,
            bool Required);

        private static string GetDefaultEndpoint(ModelProviderType provider)
        {
            return provider switch
            {
                ModelProviderType.OpenAI => "https://api.openai.com/v1",
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
                    ? "gpt-4.1-mini"
                    : modelId;
            }

            if (provider == ModelProviderType.Ollama)
            {
                return string.IsNullOrWhiteSpace(modelId) || modelId.Contains('/', StringComparison.Ordinal)
                    ? "llama3.1"
                    : modelId;
            }

            return string.IsNullOrWhiteSpace(modelId)
                ? "openai/gpt-4.1-mini"
                : modelId;
        }

        private static IReadOnlyList<string> CreateDefaultModelOptions(string provider, string currentModel)
        {
            IEnumerable<string> defaults = ParseProvider(provider) switch
            {
                ModelProviderType.OpenAI =>
                [
                    "gpt-5.2",
                    "gpt-5.1",
                    "gpt-5",
                    "gpt-5-mini",
                    "gpt-5-nano",
                    "gpt-4.1",
                    "gpt-4.1-mini",
                    "gpt-4o",
                    "gpt-4o-mini"
                ],
                ModelProviderType.Ollama =>
                [
                    "llama3.1",
                    "llama3.2",
                    "mistral",
                    "qwen2.5-coder"
                ],
                _ =>
                [
                    "openai/gpt-4.1-mini",
                    "openai/gpt-4o-mini",
                    "anthropic/claude-3.5-sonnet",
                    "google/gemini-2.0-flash-001"
                ]
            };

            return MergeModelOptions(defaults, currentModel);
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
            if (_ownsModelCatalogService && _modelCatalogService is IDisposable disposableModelCatalogService)
            {
                disposableModelCatalogService.Dispose();
            }

            if (_ownsModelConnectionTestService && _modelConnectionTestService is IDisposable disposableConnectionTestService)
            {
                disposableConnectionTestService.Dispose();
            }
        }
    }
}

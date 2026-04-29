using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels;

public sealed class RuntimeDiagnosticsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IVoiceAgentHostControl? _hostControl;
    private readonly ISkillHealthService? _skillHealthService;

    private string _coreReadinessStatus = "ACTION_NEEDED";
    private string _hostStatus = "Unknown";
    private string _skillStatus = "Unavailable";
    private string _lastRefreshText = "Not refreshed";
    private bool _isRefreshing;
    private bool _isCoreReady;

    public RuntimeDiagnosticsViewModel()
        : this(new JsonSettingsService())
    {
    }

    public RuntimeDiagnosticsViewModel(
        ISettingsService settingsService,
        IVoiceAgentHostControl? hostControl = null,
        ISkillHealthService? skillHealthService = null)
    {
        _settingsService = settingsService;
        _hostControl = hostControl;
        _skillHealthService = skillHealthService;

        Title = "Runtime Diagnostics";
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);

        if (_hostControl is not null)
        {
            _hostControl.StateChanged += OnHostStateChanged;
        }

        RefreshLocalState();
    }

    public ICommand RefreshCommand { get; }

    public ObservableCollection<RuntimeDiagnosticItemViewModel> SummaryCards { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> AiRuntimeItems { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> IntegrationItems { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> RuntimeItems { get; } = [];

    public ObservableCollection<string> BlockingItems { get; } = [];

    public string CoreReadinessStatus
    {
        get => _coreReadinessStatus;
        private set => this.RaiseAndSetIfChanged(ref _coreReadinessStatus, value);
    }

    public bool IsCoreReady
    {
        get => _isCoreReady;
        private set => this.RaiseAndSetIfChanged(ref _isCoreReady, value);
    }

    public string HostStatus
    {
        get => _hostStatus;
        private set => this.RaiseAndSetIfChanged(ref _hostStatus, value);
    }

    public string SkillStatus
    {
        get => _skillStatus;
        private set => this.RaiseAndSetIfChanged(ref _skillStatus, value);
    }

    public string LastRefreshText
    {
        get => _lastRefreshText;
        private set => this.RaiseAndSetIfChanged(ref _lastRefreshText, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => this.RaiseAndSetIfChanged(ref _isRefreshing, value);
    }

    public bool HasBlockingItems => BlockingItems.Count > 0;

    public override void OnNavigatedTo()
    {
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            RefreshLocalState();

            if (_skillHealthService is not null)
            {
                var reports = await _skillHealthService.GetHealthAsync();
                ApplySkillHealth(reports);
            }
            else
            {
                SkillStatus = "Unavailable";
                ReplaceRuntimeItem(
                    "Skill Health",
                    "Unavailable",
                    "Skill health service is not registered in this runtime.",
                    RuntimeDiagnosticSeverity.Warning);
            }

            RebuildSummary();
            LastRefreshText = $"Updated {DateTimeOffset.Now:HH:mm:ss}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void RefreshLocalState()
    {
        AiRuntimeItems.Clear();
        IntegrationItems.Clear();
        RuntimeItems.Clear();
        BlockingItems.Clear();

        var profiles = _settingsService.ModelProviderProfiles.ToArray();
        var plannerProfile = FindProfile(
            profiles,
            _settingsService.ActivePlannerProfileId,
            ModelProviderRole.Planner);
        var chatProfile = FindProfile(
            profiles,
            _settingsService.ActiveChatProfileId,
            ModelProviderRole.Chat);

        ApplyPlannerDiagnostics(plannerProfile);
        ApplyChatDiagnostics(chatProfile, plannerProfile);
        ApplyIntegrationDiagnostics();
        ApplyHostDiagnostics();
        RebuildSummary();
    }

    private void ApplyPlannerDiagnostics(ModelProviderProfile? plannerProfile)
    {
        if (plannerProfile is null)
        {
            IsCoreReady = false;
            CoreReadinessStatus = "ACTION_NEEDED";
            AddBlockingItem("Planner model profile is missing.");
            AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
                "Planner Model",
                "Missing",
                "Configure a planner profile in Settings > AI Runtime.",
                RuntimeDiagnosticSeverity.Blocked));
            return;
        }

        var validation = plannerProfile.Validate();
        var providerModel = $"{plannerProfile.Provider} / {ValueOrMissing(plannerProfile.ModelId)}";
        var modelSeverity = validation.IsValid ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked;

        AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
            "Planner Model",
            providerModel,
            plannerProfile.Enabled ? "Active planner profile is enabled." : "Planner profile exists but is disabled.",
            plannerProfile.Enabled ? modelSeverity : RuntimeDiagnosticSeverity.Blocked));

        AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
            "Planner API Key",
            RequiresApiKey(plannerProfile.Provider)
                ? (HasValue(plannerProfile.ApiKey) ? "Present" : "Missing")
                : "Not required",
            RequiresApiKey(plannerProfile.Provider)
                ? "Secret value is stored and never displayed here."
                : "Local providers can run without a cloud API key.",
            RequiresApiKey(plannerProfile.Provider) && !HasValue(plannerProfile.ApiKey)
                ? RuntimeDiagnosticSeverity.Blocked
                : RuntimeDiagnosticSeverity.Ready));

        AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
            "Planner Endpoint",
            HasValue(plannerProfile.Endpoint) ? "Configured" : "Missing",
            "Endpoint value is hidden from this screen.",
            HasValue(plannerProfile.Endpoint) ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked));

        if (!plannerProfile.Enabled)
        {
            AddBlockingItem("Planner profile is disabled.");
        }

        if (RequiresApiKey(plannerProfile.Provider) && !HasValue(plannerProfile.ApiKey))
        {
            AddBlockingItem("Planner API key is missing.");
        }

        foreach (var error in validation.Errors)
        {
            AddBlockingItem($"Planner: {error}");
        }

        IsCoreReady = validation.IsValid && plannerProfile.Enabled;
        CoreReadinessStatus = IsCoreReady ? "READY" : "ACTION_NEEDED";
    }

    private void ApplyChatDiagnostics(
        ModelProviderProfile? chatProfile,
        ModelProviderProfile? plannerProfile)
    {
        if (chatProfile is null)
        {
            AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
                "Chat / Skill Model",
                plannerProfile is null ? "Missing" : "Planner fallback",
                plannerProfile is null
                    ? "Configure a chat profile or a usable planner profile."
                    : "No dedicated chat profile is active; runtime can fall back to the planner profile.",
                plannerProfile is null ? RuntimeDiagnosticSeverity.Blocked : RuntimeDiagnosticSeverity.Warning));
            return;
        }

        var validation = chatProfile.Validate();
        AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
            "Chat / Skill Model",
            $"{chatProfile.Provider} / {ValueOrMissing(chatProfile.ModelId)}",
            chatProfile.Enabled ? "Dedicated chat and skill execution profile is enabled." : "Profile exists but is disabled.",
            validation.IsValid && chatProfile.Enabled
                ? RuntimeDiagnosticSeverity.Ready
                : RuntimeDiagnosticSeverity.Warning));
    }

    private void ApplyIntegrationDiagnostics()
    {
        IntegrationItems.Add(CreateOptionalSecretItem(
            "Todoist",
            HasValue(_settingsService.TodoistApiKey),
            "Task MCP integration token."));

        var isSmtpConfigured =
            HasValue(_settingsService.SmtpHost)
            && _settingsService.SmtpPort > 0
            && HasValue(_settingsService.SmtpUsername)
            && HasValue(_settingsService.SmtpPassword)
            && HasValue(_settingsService.SenderEmail);
        IntegrationItems.Add(CreateOptionalSecretItem(
            "Email SMTP",
            isSmtpConfigured,
            "Email sending remains optional for core agent readiness."));

        var isSmsConfigured =
            _settingsService.SmsEnabled
            && HasValue(_settingsService.TwilioAccountSid)
            && HasValue(_settingsService.TwilioAuthToken)
            && HasValue(_settingsService.TwilioPhoneNumber);
        IntegrationItems.Add(CreateOptionalSecretItem(
            "Twilio SMS",
            isSmsConfigured,
            "SMS is optional and does not block core runtime readiness."));
    }

    private void ApplyHostDiagnostics()
    {
        HostStatus = _hostControl is null
            ? "Unavailable"
            : (_hostControl.IsRunning ? "Online" : "Offline");

        RuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
            "Agent Host",
            HostStatus,
            _hostControl is null
                ? "Host control service is not attached to this window yet."
                : "Reflects the local hosted agent service state.",
            _hostControl is null
                ? RuntimeDiagnosticSeverity.Warning
                : (_hostControl.IsRunning ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked)));
    }

    private void ApplySkillHealth(IReadOnlyCollection<SkillHealthReport> reports)
    {
        var total = reports.Count;
        var healthy = reports.Count(report => report.Status == SkillHealthStatus.Healthy);
        var needsReview = reports.Count(report => report.Status == SkillHealthStatus.ReviewRequired);
        var blocked = reports.Count(report =>
            report.Status is SkillHealthStatus.MissingExecutor or SkillHealthStatus.PermissionDenied);

        SkillStatus = total == 0 ? "No skills" : $"{healthy}/{total} healthy";
        ReplaceRuntimeItem(
            "Skill Health",
            SkillStatus,
            total == 0
                ? "No skills are registered."
                : $"{needsReview} need review, {blocked} blocked by executor or permissions.",
            blocked > 0
                ? RuntimeDiagnosticSeverity.Blocked
                : (needsReview > 0 ? RuntimeDiagnosticSeverity.Warning : RuntimeDiagnosticSeverity.Ready));
    }

    private void RebuildSummary()
    {
        SummaryCards.Clear();
        SummaryCards.Add(new RuntimeDiagnosticItemViewModel(
            "Core AI",
            IsCoreReady ? "Ready" : "Action needed",
            IsCoreReady
                ? "Planner profile can generate structured plans."
                : "Fix blocking model settings before live commands.",
            IsCoreReady ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked));
        SummaryCards.Add(new RuntimeDiagnosticItemViewModel(
            "Skills",
            SkillStatus,
            "Health service summarizes installed skill readiness.",
            SkillStatus.Contains("healthy", StringComparison.OrdinalIgnoreCase)
                ? RuntimeDiagnosticSeverity.Ready
                : RuntimeDiagnosticSeverity.Warning));
        SummaryCards.Add(new RuntimeDiagnosticItemViewModel(
            "Integrations",
            $"{IntegrationItems.Count(item => item.IsReady)}/{IntegrationItems.Count} configured",
            "Optional connectors do not block the core agent.",
            IntegrationItems.Any(item => item.IsReady)
                ? RuntimeDiagnosticSeverity.Ready
                : RuntimeDiagnosticSeverity.Warning));
        SummaryCards.Add(new RuntimeDiagnosticItemViewModel(
            "Host",
            HostStatus,
            "Local hosted agent service state.",
            HostStatus == "Online"
                ? RuntimeDiagnosticSeverity.Ready
                : RuntimeDiagnosticSeverity.Warning));

        this.RaisePropertyChanged(nameof(HasBlockingItems));
    }

    private static ModelProviderProfile? FindProfile(
        IReadOnlyCollection<ModelProviderProfile> profiles,
        string activeProfileId,
        ModelProviderRole role)
    {
        var activeProfile = profiles.FirstOrDefault(profile =>
            profile.Id.Equals(activeProfileId, StringComparison.OrdinalIgnoreCase)
            && profile.Roles.Contains(role));

        return activeProfile
            ?? profiles.FirstOrDefault(profile => profile.Enabled && profile.Roles.Contains(role))
            ?? profiles.FirstOrDefault(profile => profile.Roles.Contains(role));
    }

    private static RuntimeDiagnosticItemViewModel CreateOptionalSecretItem(
        string name,
        bool isConfigured,
        string detail)
    {
        return new RuntimeDiagnosticItemViewModel(
            name,
            isConfigured ? "Configured" : "Not configured",
            isConfigured ? $"{detail} Secret value is hidden." : detail,
            isConfigured ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Warning);
    }

    private void ReplaceRuntimeItem(
        string name,
        string value,
        string detail,
        RuntimeDiagnosticSeverity severity)
    {
        var existing = RuntimeItems.FirstOrDefault(item =>
            item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            RuntimeItems.Remove(existing);
        }

        RuntimeItems.Add(new RuntimeDiagnosticItemViewModel(name, value, detail, severity));
    }

    private void AddBlockingItem(string item)
    {
        if (!BlockingItems.Contains(item))
        {
            BlockingItems.Add(item);
        }
    }

    private void OnHostStateChanged(object? sender, bool isRunning)
    {
        HostStatus = isRunning ? "Online" : "Offline";
        ReplaceRuntimeItem(
            "Agent Host",
            HostStatus,
            "Reflects the local hosted agent service state.",
            isRunning ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked);
        RebuildSummary();
    }

    private static bool RequiresApiKey(ModelProviderType provider) => provider != ModelProviderType.Ollama;

    private static bool HasValue(string value) => !string.IsNullOrWhiteSpace(value);

    private static string ValueOrMissing(string value) => HasValue(value) ? value : "Missing";
}

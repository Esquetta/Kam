using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.AI;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Core.Models.Updates;
using SmartVoiceAgent.Core.Security;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels;

public sealed class RuntimeDiagnosticsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly IVoiceAgentHostControl? _hostControl;
    private readonly ISkillHealthService? _skillHealthService;
    private readonly IModelConnectionTestService? _modelConnectionTestService;
    private readonly ISkillEvalHarness? _skillEvalHarness;
    private readonly ISkillEvalCaseCatalog? _skillEvalCaseCatalog;
    private readonly ISkillExecutionHistoryService? _skillExecutionHistoryService;
    private readonly ISkillPlannerTraceStore? _skillPlannerTraceStore;
    private readonly IApplicationUpdateService? _applicationUpdateService;
    private readonly IApplicationRestartPlanner? _applicationRestartPlanner;
    private readonly IApplicationVersionProvider? _applicationVersionProvider;
    private readonly IGitHubAppClient? _githubAppClient;
    private readonly Action<string, string>? _copyReport;

    private ApplicationUpdateCheckResult? _lastApplicationUpdateCheck;
    private ApplicationUpdateDownloadResult? _lastApplicationUpdateDownload;
    private ApplicationRestartPlan? _lastApplicationRestartPlan;
    private string _coreReadinessStatus = "ACTION_NEEDED";
    private string _hostStatus = "Unknown";
    private string _skillStatus = "Unavailable";
    private string _skillSmokeStatus = "Not run";
    private string _skillSmokeSummaryValue = string.Empty;
    private string _applicationVersionText = "Unknown";
    private string _applicationUpdateStatus = "Not checked";
    private string _applicationUpdateActionStatus = "Updates not checked.";
    private string _downloadedUpdatePackagePath = string.Empty;
    private string _liveTestStatus = "NEEDS_ACTION";
    private string _liveTestNextAction = "Fix blocking model settings before live commands.";
    private string _readinessReportCopyStatus = "Report not copied.";
    private string _lastRefreshText = "Not refreshed";
    private bool _isRefreshing;
    private bool _isRunningSkillSmoke;
    private bool _isCoreReady;
    private bool _isLiveTestReady;

    public RuntimeDiagnosticsViewModel()
        : this(new JsonSettingsService())
    {
    }

    public RuntimeDiagnosticsViewModel(
        ISettingsService settingsService,
        IVoiceAgentHostControl? hostControl = null,
        ISkillHealthService? skillHealthService = null,
        IModelConnectionTestService? modelConnectionTestService = null,
        ISkillEvalHarness? skillEvalHarness = null,
        ISkillEvalCaseCatalog? skillEvalCaseCatalog = null,
        ISkillExecutionHistoryService? skillExecutionHistoryService = null,
        ISkillPlannerTraceStore? skillPlannerTraceStore = null,
        IApplicationUpdateService? applicationUpdateService = null,
        IApplicationRestartPlanner? applicationRestartPlanner = null,
        IApplicationVersionProvider? applicationVersionProvider = null,
        IGitHubAppClient? githubAppClient = null,
        Action<string, string>? copyReport = null)
    {
        _settingsService = settingsService;
        _hostControl = hostControl;
        _skillHealthService = skillHealthService;
        _modelConnectionTestService = modelConnectionTestService;
        _skillEvalHarness = skillEvalHarness;
        _skillEvalCaseCatalog = skillEvalCaseCatalog;
        _skillExecutionHistoryService = skillExecutionHistoryService;
        _skillPlannerTraceStore = skillPlannerTraceStore;
        _applicationUpdateService = applicationUpdateService;
        _applicationRestartPlanner = applicationRestartPlanner;
        _applicationVersionProvider = applicationVersionProvider;
        _githubAppClient = githubAppClient;
        _copyReport = copyReport;

        Title = "Runtime Diagnostics";
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        RunSkillSmokeCommand = ReactiveCommand.CreateFromTask(RunSkillSmokeAsync);
        CheckApplicationUpdateCommand = ReactiveCommand.CreateFromTask(CheckApplicationUpdateAsync);
        DownloadApplicationUpdateCommand = ReactiveCommand.CreateFromTask(DownloadApplicationUpdateAsync);
        PlanApplicationRestartCommand = ReactiveCommand.Create(PlanApplicationRestart);
        CopyReadinessReportCommand = ReactiveCommand.Create(CopyReadinessReport);

        if (_hostControl is not null)
        {
            _hostControl.StateChanged += OnHostStateChanged;
        }

        if (_skillExecutionHistoryService is not null)
        {
            _skillExecutionHistoryService.Changed += OnCommandLoopEvidenceChanged;
        }

        if (_skillPlannerTraceStore is not null)
        {
            _skillPlannerTraceStore.Changed += OnCommandLoopEvidenceChanged;
        }

        RefreshLocalState();
    }

    public ICommand RefreshCommand { get; }

    public ICommand RunSkillSmokeCommand { get; }

    public ICommand CheckApplicationUpdateCommand { get; }

    public ICommand DownloadApplicationUpdateCommand { get; }

    public ICommand PlanApplicationRestartCommand { get; }

    public ICommand CopyReadinessReportCommand { get; }

    public ObservableCollection<RuntimeDiagnosticItemViewModel> SummaryCards { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> AiRuntimeItems { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> IntegrationItems { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> RuntimeItems { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> ApplicationUpdateItems { get; } = [];

    public ObservableCollection<RuntimeDiagnosticItemViewModel> LiveTestSteps { get; } = [];

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

    public string SkillSmokeStatus
    {
        get => _skillSmokeStatus;
        private set => this.RaiseAndSetIfChanged(ref _skillSmokeStatus, value);
    }

    public string ApplicationVersionText
    {
        get => _applicationVersionText;
        private set => this.RaiseAndSetIfChanged(ref _applicationVersionText, value);
    }

    public string ApplicationUpdateStatus
    {
        get => _applicationUpdateStatus;
        private set => this.RaiseAndSetIfChanged(ref _applicationUpdateStatus, value);
    }

    public string ApplicationUpdateActionStatus
    {
        get => _applicationUpdateActionStatus;
        private set => this.RaiseAndSetIfChanged(ref _applicationUpdateActionStatus, value);
    }

    public string DownloadedUpdatePackagePath
    {
        get => _downloadedUpdatePackagePath;
        private set => this.RaiseAndSetIfChanged(ref _downloadedUpdatePackagePath, value);
    }

    public string LiveTestStatus
    {
        get => _liveTestStatus;
        private set => this.RaiseAndSetIfChanged(ref _liveTestStatus, value);
    }

    public string LiveTestNextAction
    {
        get => _liveTestNextAction;
        private set => this.RaiseAndSetIfChanged(ref _liveTestNextAction, value);
    }

    public bool IsLiveTestReady
    {
        get => _isLiveTestReady;
        private set => this.RaiseAndSetIfChanged(ref _isLiveTestReady, value);
    }

    public string ReadinessReportCopyStatus
    {
        get => _readinessReportCopyStatus;
        private set => this.RaiseAndSetIfChanged(ref _readinessReportCopyStatus, value);
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

    public bool IsRunningSkillSmoke
    {
        get => _isRunningSkillSmoke;
        private set => this.RaiseAndSetIfChanged(ref _isRunningSkillSmoke, value);
    }

    public bool HasBlockingItems => BlockingItems.Count > 0;

    public override void OnNavigatedTo()
    {
        _ = RefreshAsync();
    }

    public override void OnNavigatedFrom()
    {
        if (_hostControl is not null)
        {
            _hostControl.StateChanged -= OnHostStateChanged;
        }

        if (_skillExecutionHistoryService is not null)
        {
            _skillExecutionHistoryService.Changed -= OnCommandLoopEvidenceChanged;
        }

        if (_skillPlannerTraceStore is not null)
        {
            _skillPlannerTraceStore.Changed -= OnCommandLoopEvidenceChanged;
        }
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
            var plannerProfile = RefreshLocalState();

            if (_githubAppClient is not null)
            {
                await ApplyGitHubAppDiagnosticsAsync();
            }

            if (plannerProfile is not null
                && _modelConnectionTestService is not null
                && IsReadyForLiveConnectionTest(plannerProfile))
            {
                await ApplyPlannerLiveConnectionAsync(plannerProfile);
            }

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

    public async Task RunSkillSmokeAsync()
    {
        if (IsRunningSkillSmoke)
        {
            return;
        }

        if (_skillEvalHarness is null || _skillEvalCaseCatalog is null)
        {
            SkillSmokeStatus = "Smoke eval services unavailable";
            _skillSmokeSummaryValue = "Unavailable";
            ReplaceRuntimeItem(
                "Skill Smoke",
                "Unavailable",
                "Skill eval harness is not registered in this runtime.",
                RuntimeDiagnosticSeverity.Warning);
            RebuildSummary();
            return;
        }

        IsRunningSkillSmoke = true;
        try
        {
            var summary = await _skillEvalHarness.RunAsync(
                _skillEvalCaseCatalog.CreateSmokeCases()).ConfigureAwait(true);
            ApplySkillSmokeSummary(summary);
            LastRefreshText = $"Skill smoke {DateTimeOffset.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            SkillSmokeStatus = "Smoke evals failed to run";
            _skillSmokeSummaryValue = "Failed";
            ReplaceRuntimeItem(
                "Skill Smoke",
                "Failed",
                ex.Message,
                RuntimeDiagnosticSeverity.Blocked);
            AddBlockingItem($"Skill smoke failed: {ex.Message}");
            RebuildSummary();
        }
        finally
        {
            IsRunningSkillSmoke = false;
        }
    }

    public async Task CheckApplicationUpdateAsync()
    {
        if (_applicationUpdateService is null)
        {
            _lastApplicationUpdateCheck = ApplicationUpdateCheckResult.Failed(
                CurrentApplicationVersion(),
                "Application update service is unavailable.");
            ApplicationUpdateActionStatus = _lastApplicationUpdateCheck.Message;
            ApplyApplicationUpdateDiagnostics();
            RebuildSummary();
            return;
        }

        ApplicationUpdateActionStatus = "Checking GitHub Releases for Kam updates.";
        try
        {
            _lastApplicationUpdateCheck = await _applicationUpdateService
                .CheckForUpdatesAsync()
                .ConfigureAwait(true);
            ApplicationUpdateActionStatus = _lastApplicationUpdateCheck.Message;
        }
        catch (Exception ex)
        {
            _lastApplicationUpdateCheck = ApplicationUpdateCheckResult.Failed(
                CurrentApplicationVersion(),
                $"Update check failed: {ex.Message}");
            ApplicationUpdateActionStatus = _lastApplicationUpdateCheck.Message;
        }

        ApplyApplicationUpdateDiagnostics();
        RebuildSummary();
        LastRefreshText = $"Updates {DateTimeOffset.Now:HH:mm:ss}";
    }

    public async Task DownloadApplicationUpdateAsync()
    {
        if (_applicationUpdateService is null)
        {
            _lastApplicationUpdateDownload = ApplicationUpdateDownloadResult.Failed(
                "Application update service is unavailable.");
            ApplicationUpdateActionStatus = _lastApplicationUpdateDownload.Message;
            ApplyApplicationUpdateDiagnostics();
            RebuildSummary();
            return;
        }

        ApplicationUpdateActionStatus = "Downloading latest Kam release package.";
        try
        {
            _lastApplicationUpdateDownload = await _applicationUpdateService
                .DownloadLatestAsync()
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _lastApplicationUpdateDownload = ApplicationUpdateDownloadResult.Failed(
                $"Update package download failed: {ex.Message}");
        }

        if (_lastApplicationUpdateDownload.Success)
        {
            DownloadedUpdatePackagePath = _lastApplicationUpdateDownload.FilePath ?? string.Empty;
            ApplicationUpdateActionStatus = "Downloaded Kam update package.";
            if (_lastApplicationUpdateDownload.IsVerified)
            {
                PlanApplicationRestart();
                ApplicationUpdateActionStatus = "Downloaded Kam update package.";
                return;
            }

            _lastApplicationRestartPlan = null;
            ApplyApplicationUpdateDiagnostics();
            RebuildSummary();
            ApplicationUpdateActionStatus = "Downloaded Kam update package.";
            return;
        }

        ApplicationUpdateActionStatus = _lastApplicationUpdateDownload.Message;
        ApplyApplicationUpdateDiagnostics();
        RebuildSummary();
    }

    public void PlanApplicationRestart()
    {
        if (_lastApplicationUpdateDownload?.Success == true
            && !_lastApplicationUpdateDownload.IsVerified)
        {
            _lastApplicationRestartPlan = null;
            ApplicationUpdateActionStatus = "Verify the downloaded package before restart handoff.";
            ApplyApplicationUpdateDiagnostics();
            RebuildSummary();
            return;
        }

        if (_applicationRestartPlanner is null)
        {
            _lastApplicationRestartPlan = new ApplicationRestartPlan(
                false,
                "Application restart planner is unavailable.",
                null,
                DownloadedUpdatePackagePath,
                ["Restart planner service is not registered."]);
        }
        else
        {
            _lastApplicationRestartPlan = _applicationRestartPlanner.CreateRestartPlan(
                string.IsNullOrWhiteSpace(DownloadedUpdatePackagePath)
                    ? null
                    : DownloadedUpdatePackagePath);
        }

        ApplicationUpdateActionStatus = _lastApplicationRestartPlan.Message;
        ApplyApplicationUpdateDiagnostics();
        RebuildSummary();
    }

    public string BuildReadinessReport()
    {
        var report = new StringBuilder();
        report.AppendLine("Kam Runtime Readiness Report");
        report.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        report.AppendLine($"Core Status: {CoreReadinessStatus}");
        report.AppendLine($"Live Test: {LiveTestStatus}");
        report.AppendLine($"Next Action: {LiveTestNextAction}");
        report.AppendLine();

        AppendReportSection(report, "Live Production Test", LiveTestSteps);
        AppendReportSection(report, "Summary", SummaryCards);
        AppendReportSection(report, "AI Runtime", AiRuntimeItems);
        AppendReportSection(report, "Application Updates", ApplicationUpdateItems);
        AppendReportSection(report, "Local Runtime", RuntimeItems);
        AppendReportSection(report, "Integrations", IntegrationItems);

        report.AppendLine("Blocking Items");
        if (BlockingItems.Count == 0)
        {
            report.AppendLine("- None");
        }
        else
        {
            foreach (var item in BlockingItems)
            {
                report.AppendLine($"- {item}");
            }
        }

        return SecretRedactor.Redact(report.ToString().TrimEnd());
    }

    private void CopyReadinessReport()
    {
        var report = BuildReadinessReport();
        if (_copyReport is null)
        {
            ReadinessReportCopyStatus = "Clipboard unavailable.";
            return;
        }

        _copyReport("readiness_report", report);
        ReadinessReportCopyStatus = "Readiness report copied.";
    }

    private ModelProviderProfile? RefreshLocalState()
    {
        AiRuntimeItems.Clear();
        IntegrationItems.Clear();
        RuntimeItems.Clear();
        ApplicationUpdateItems.Clear();
        BlockingItems.Clear();
        SkillSmokeStatus = "Not run";
        _skillSmokeSummaryValue = string.Empty;

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
        ApplyCommandLoopDiagnostics();
        ApplyApplicationUpdateDiagnostics();
        RebuildSummary();

        return plannerProfile;
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

    private async Task ApplyGitHubAppDiagnosticsAsync()
    {
        try
        {
            var status = await _githubAppClient!.GetStatusAsync();
            var repositories = status.IsConnected
                ? await _githubAppClient.ListRepositoriesAsync()
                : null;
            ReplaceIntegrationItem(BuildGitHubAppIntegrationItem(status, repositories));
        }
        catch (Exception ex)
        {
            ReplaceIntegrationItem(new RuntimeDiagnosticItemViewModel(
                "GitHub App",
                "Unavailable",
                SecretRedactor.Redact($"GitHub App status check failed: {ex.Message}"),
                RuntimeDiagnosticSeverity.Warning));
        }
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

    private void ApplyApplicationUpdateDiagnostics()
    {
        ApplicationUpdateItems.Clear();
        ApplicationVersionText = CurrentApplicationVersion();

        ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
            "Current Version",
            ApplicationVersionText,
            "Assembly version used for release comparison.",
            RuntimeDiagnosticSeverity.Ready));

        if (_applicationUpdateService is null)
        {
            ApplicationUpdateStatus = "Unavailable";
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Release Feed",
                "Unavailable",
                "Application update service is not registered in this runtime.",
                RuntimeDiagnosticSeverity.Warning));
            AddPackageVerificationDiagnostic();
            AddDownloadedPackageDiagnostic();
            AddRestartPlanDiagnostic();
            return;
        }

        if (_lastApplicationUpdateCheck is null)
        {
            ApplicationUpdateStatus = "Not checked";
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Release Feed",
                "Not checked",
                "Use Check Updates to query the configured GitHub Releases feed.",
                RuntimeDiagnosticSeverity.Warning));
            AddPackageVerificationDiagnostic();
            AddDownloadedPackageDiagnostic();
            AddRestartPlanDiagnostic();
            return;
        }

        ApplicationUpdateStatus = FormatApplicationUpdateStatus(_lastApplicationUpdateCheck);
        ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
            "Release Feed",
            ApplicationUpdateStatus,
            BuildApplicationUpdateFeedDetail(_lastApplicationUpdateCheck),
            GetApplicationUpdateSeverity(_lastApplicationUpdateCheck)));

        if (_lastApplicationUpdateCheck.Asset is not null)
        {
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Release Asset",
                _lastApplicationUpdateCheck.Asset.Name,
                $"{FormatBytes(_lastApplicationUpdateCheck.Asset.SizeBytes)} package selected; raw download URL hidden.",
                RuntimeDiagnosticSeverity.Ready));
        }
        else
        {
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Release Asset",
                _lastApplicationUpdateCheck.IsUpdateAvailable ? "Missing" : "Not needed",
                _lastApplicationUpdateCheck.IsUpdateAvailable
                    ? "Latest release has no installer asset selected for this app."
                    : "No package is needed when the app is already current.",
                _lastApplicationUpdateCheck.IsUpdateAvailable
                    ? RuntimeDiagnosticSeverity.Warning
                    : RuntimeDiagnosticSeverity.Ready));
        }

        AddPackageVerificationDiagnostic();
        AddDownloadedPackageDiagnostic();
        AddRestartPlanDiagnostic();
    }

    private void ApplyCommandLoopDiagnostics()
    {
        ApplyPlannerTraceDiagnostic();
        ApplySkillResultDiagnostic();
    }

    private void ApplyPlannerTraceDiagnostic()
    {
        var trace = _skillPlannerTraceStore?.GetRecent(1).FirstOrDefault();
        if (trace is null)
        {
            ReplaceRuntimeItem(
                "Planner Trace",
                "No trace",
                _skillPlannerTraceStore is null
                    ? "Planner trace store is not attached to this diagnostics view."
                    : "Submit a command to produce planner JSON evidence.",
                RuntimeDiagnosticSeverity.Warning);
            return;
        }

        if (trace.IsValid)
        {
            ReplaceRuntimeItem(
                "Planner Trace",
                "Valid",
                $"{ValueOrMissing(trace.SkillId)}, confidence {trace.Confidence:0.00}, {FormatDuration(trace.DurationMilliseconds)}.",
                RuntimeDiagnosticSeverity.Ready);
            return;
        }

        ReplaceRuntimeItem(
            "Planner Trace",
            "Invalid",
            string.IsNullOrWhiteSpace(trace.ErrorMessage)
                ? "Planner produced an invalid trace."
                : SecretRedactor.Redact(trace.ErrorMessage),
            RuntimeDiagnosticSeverity.Blocked);
        AddBlockingItem("Planner trace invalid: model did not produce a usable skill plan.");
    }

    private void ApplySkillResultDiagnostic()
    {
        var entry = _skillExecutionHistoryService?.GetRecent(1).FirstOrDefault();
        if (entry is null)
        {
            ReplaceRuntimeItem(
                "Skill Result",
                "No result",
                _skillExecutionHistoryService is null
                    ? "Skill execution history service is not attached to this diagnostics view."
                    : "Run a command or smoke eval to produce normalized skill result evidence.",
                RuntimeDiagnosticSeverity.Warning);
            return;
        }

        var statusText = FormatSkillExecutionStatus(entry.Status);
        var detail = BuildSkillResultDetail(entry);
        if (entry.Success)
        {
            ReplaceRuntimeItem(
                "Skill Result",
                statusText,
                detail,
                RuntimeDiagnosticSeverity.Ready);
            return;
        }

        ReplaceRuntimeItem(
            "Skill Result",
            statusText,
            detail,
            RuntimeDiagnosticSeverity.Blocked);
        AddBlockingItem($"Last skill execution failed: {entry.SkillId} ({statusText}).");
    }

    private async Task ApplyPlannerLiveConnectionAsync(ModelProviderProfile plannerProfile)
    {
        var result = await _modelConnectionTestService!.TestAsync(plannerProfile).ConfigureAwait(true);
        if (result.Success)
        {
            AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
                "Planner Live Connection",
                "Verified",
                $"{result.LiveModelCount} live models returned by provider.",
                RuntimeDiagnosticSeverity.Ready));
            return;
        }

        var message = SanitizeDiagnosticMessage(result.Message, plannerProfile.ApiKey);
        IsCoreReady = false;
        CoreReadinessStatus = "ACTION_NEEDED";
        AddBlockingItem($"Planner live connection failed: {message}");
        AiRuntimeItems.Add(new RuntimeDiagnosticItemViewModel(
            "Planner Live Connection",
            "Failed",
            message,
            RuntimeDiagnosticSeverity.Blocked));
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

    private void ApplySkillSmokeSummary(SkillEvalSummary summary)
    {
        if (summary.Total <= 0)
        {
            SkillSmokeStatus = "No smoke evals configured";
            _skillSmokeSummaryValue = "No smoke";
            ReplaceRuntimeItem(
                "Skill Smoke",
                "No cases",
                "Smoke eval catalog returned no cases.",
                RuntimeDiagnosticSeverity.Warning);
            RebuildSummary();
            return;
        }

        SkillSmokeStatus = $"{summary.Passed}/{summary.Total} smoke evals passing";
        _skillSmokeSummaryValue = $"{summary.Passed}/{summary.Total} smoke";

        var isPassing = summary.Failed == 0;
        var detail = isPassing
            ? $"Smoke eval harness executed {summary.Total} cases."
            : BuildSkillSmokeFailureDetail(summary);

        ReplaceRuntimeItem(
            "Skill Smoke",
            $"{summary.Passed}/{summary.Total} passing",
            detail,
            isPassing ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked);

        if (!isPassing)
        {
            AddBlockingItem($"Skill smoke failed: {summary.Passed}/{summary.Total} passing.");
        }

        RebuildSummary();
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
            string.IsNullOrWhiteSpace(_skillSmokeSummaryValue) ? SkillStatus : _skillSmokeSummaryValue,
            string.IsNullOrWhiteSpace(_skillSmokeSummaryValue)
                ? "Health service summarizes installed skill readiness."
                : "Smoke eval harness verifies executable skill behavior.",
            GetSkillSummarySeverity()));
        var commandLoopSummary = GetCommandLoopSummary();
        SummaryCards.Add(new RuntimeDiagnosticItemViewModel(
            "Command Loop",
            commandLoopSummary.Value,
            commandLoopSummary.Detail,
            commandLoopSummary.Severity));
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
        SummaryCards.Add(new RuntimeDiagnosticItemViewModel(
            "Updates",
            ApplicationUpdateStatus,
            $"Current version {ApplicationVersionText}.",
            GetApplicationUpdateSummarySeverity()));

        RebuildLiveTestSession();
        this.RaisePropertyChanged(nameof(HasBlockingItems));
    }

    private void RebuildLiveTestSession()
    {
        LiveTestSteps.Clear();

        LiveTestSteps.Add(new RuntimeDiagnosticItemViewModel(
            "Core AI",
            IsCoreReady ? "Ready" : "Action needed",
            IsCoreReady
                ? "Planner profile is configured for structured commands."
                : "Planner profile or credentials need attention.",
            IsCoreReady ? RuntimeDiagnosticSeverity.Ready : RuntimeDiagnosticSeverity.Blocked));

        LiveTestSteps.Add(BuildPlannerLiveConnectionLiveTestStep());
        LiveTestSteps.Add(new RuntimeDiagnosticItemViewModel(
            "Agent Host",
            HostStatus,
            HostStatus == "Online"
                ? "Hosted command runtime is active."
                : "Hosted command runtime must be online for a live session.",
            HostStatus == "Online"
                ? RuntimeDiagnosticSeverity.Ready
                : (HostStatus == "Offline" ? RuntimeDiagnosticSeverity.Blocked : RuntimeDiagnosticSeverity.Warning)));
        LiveTestSteps.Add(BuildSkillSmokeLiveTestStep());
        LiveTestSteps.Add(BuildRuntimeEvidenceLiveTestStep("Planner Trace"));
        LiveTestSteps.Add(BuildRuntimeEvidenceLiveTestStep("Skill Result"));

        var commandLoopSummary = GetCommandLoopSummary();
        LiveTestSteps.Add(new RuntimeDiagnosticItemViewModel(
            "Command Loop",
            commandLoopSummary.Value,
            commandLoopSummary.Detail,
            commandLoopSummary.Severity));

        IsLiveTestReady = LiveTestSteps.All(step => step.IsReady);
        LiveTestStatus = IsLiveTestReady ? "READY_FOR_LIVE_TEST" : "NEEDS_ACTION";
        LiveTestNextAction = IsLiveTestReady
            ? "Run a production command loop smoke."
            : GetLiveTestNextAction(LiveTestSteps.First(step => !step.IsReady));
    }

    private RuntimeDiagnosticItemViewModel BuildPlannerLiveConnectionLiveTestStep()
    {
        var liveConnection = AiRuntimeItems.FirstOrDefault(item =>
            item.Name.Equals("Planner Live Connection", StringComparison.OrdinalIgnoreCase));
        if (liveConnection is not null)
        {
            return new RuntimeDiagnosticItemViewModel(
                "Planner Live Connection",
                liveConnection.Value,
                liveConnection.Detail,
                liveConnection.Severity);
        }

        if (!IsCoreReady)
        {
            return new RuntimeDiagnosticItemViewModel(
                "Planner Live Connection",
                "Blocked",
                "Fix core AI settings before testing the provider connection.",
                RuntimeDiagnosticSeverity.Blocked);
        }

        return new RuntimeDiagnosticItemViewModel(
            "Planner Live Connection",
            "Needs refresh",
            _modelConnectionTestService is null
                ? "Live model connection test service is unavailable."
                : "Click Refresh to verify the planner provider with the saved key.",
            RuntimeDiagnosticSeverity.Warning);
    }

    private RuntimeDiagnosticItemViewModel BuildSkillSmokeLiveTestStep()
    {
        var skillSmoke = RuntimeItems.FirstOrDefault(item =>
            item.Name.Equals("Skill Smoke", StringComparison.OrdinalIgnoreCase));
        if (skillSmoke is not null)
        {
            return new RuntimeDiagnosticItemViewModel(
                "Skill Smoke",
                skillSmoke.Value,
                skillSmoke.Detail,
                skillSmoke.Severity);
        }

        return new RuntimeDiagnosticItemViewModel(
            "Skill Smoke",
            "Not run",
            "Run Skill Smoke to verify executable built-in skill behavior.",
            RuntimeDiagnosticSeverity.Warning);
    }

    private RuntimeDiagnosticItemViewModel BuildRuntimeEvidenceLiveTestStep(string name)
    {
        var item = RuntimeItems.FirstOrDefault(runtimeItem =>
            runtimeItem.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            return new RuntimeDiagnosticItemViewModel(
                item.Name,
                item.Value,
                item.Detail,
                item.Severity);
        }

        return new RuntimeDiagnosticItemViewModel(
            name,
            "Unavailable",
            $"{name} evidence is not attached to this diagnostics view.",
            RuntimeDiagnosticSeverity.Warning);
    }

    private static string GetLiveTestNextAction(RuntimeDiagnosticItemViewModel step)
    {
        return step.Name switch
        {
            "Core AI" => "Fix model settings in AI Runtime.",
            "Planner Live Connection" when step.IsBlocked => "Update model credentials, then refresh diagnostics.",
            "Planner Live Connection" => "Click Refresh to verify the planner model connection.",
            "Agent Host" => "Start the local agent host before live testing.",
            "Skill Smoke" when step.IsBlocked => "Fix failing skill smoke evals, then rerun smoke.",
            "Skill Smoke" => "Run Skill Smoke.",
            "Planner Trace" when step.IsBlocked => "Fix the latest planner trace blocker.",
            "Planner Trace" => "Submit a real command to produce planner JSON evidence.",
            "Skill Result" when step.IsBlocked => "Fix the latest skill execution blocker.",
            "Skill Result" => "Run a command or smoke eval to produce normalized skill result evidence.",
            "Command Loop" when step.IsBlocked => "Fix the latest planner or skill execution blocker.",
            "Command Loop" => "Run a production command loop smoke.",
            _ => "Resolve the first non-ready live test step."
        };
    }

    private static void AppendReportSection(
        StringBuilder report,
        string title,
        IEnumerable<RuntimeDiagnosticItemViewModel> items)
    {
        report.AppendLine(title);

        var hasItems = false;
        foreach (var item in items)
        {
            hasItems = true;
            report.AppendLine($"- {item.Name}: {item.Value}");
            if (!string.IsNullOrWhiteSpace(item.Detail))
            {
                report.AppendLine($"  {item.Detail}");
            }
        }

        if (!hasItems)
        {
            report.AppendLine("- None");
        }

        report.AppendLine();
    }

    private void AddDownloadedPackageDiagnostic()
    {
        if (_lastApplicationUpdateDownload?.Success == true
            && !string.IsNullOrWhiteSpace(_lastApplicationUpdateDownload.FilePath))
        {
            var verificationDetail = _lastApplicationUpdateDownload.IsVerified
                ? "SHA256 verified."
                : $"Verification: {_lastApplicationUpdateDownload.VerificationStatus}.";
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Downloaded Package",
                _lastApplicationUpdateDownload.IsVerified ? "Ready" : "Needs verification",
                $"{Path.GetFileName(_lastApplicationUpdateDownload.FilePath)} downloaded for version {_lastApplicationUpdateDownload.Version ?? "(unknown)"}. {verificationDetail}",
                _lastApplicationUpdateDownload.IsVerified
                    ? RuntimeDiagnosticSeverity.Ready
                    : RuntimeDiagnosticSeverity.Warning));
            return;
        }

        if (_lastApplicationUpdateDownload is not null)
        {
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Downloaded Package",
                "Failed",
                SecretRedactor.Redact(_lastApplicationUpdateDownload.Message),
                RuntimeDiagnosticSeverity.Warning));
            return;
        }

        ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
            "Downloaded Package",
            "Not downloaded",
            "Download is explicit so packages are not pulled during diagnostics refresh.",
            RuntimeDiagnosticSeverity.Warning));
    }

    private void AddPackageVerificationDiagnostic()
    {
        if (_lastApplicationUpdateDownload is not null)
        {
            if (_lastApplicationUpdateDownload.Success && _lastApplicationUpdateDownload.IsVerified)
            {
                ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                    "Package Verification",
                    "Verified",
                    BuildVerificationDetail(_lastApplicationUpdateDownload),
                    RuntimeDiagnosticSeverity.Ready));
                return;
            }

            if (_lastApplicationUpdateDownload.VerificationStatus.Contains(
                    "mismatch",
                    StringComparison.OrdinalIgnoreCase))
            {
                ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                    "Package Verification",
                    "Mismatch",
                    BuildVerificationDetail(_lastApplicationUpdateDownload),
                    RuntimeDiagnosticSeverity.Blocked));
                return;
            }

            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Package Verification",
                "Not verified",
                BuildVerificationDetail(_lastApplicationUpdateDownload),
                RuntimeDiagnosticSeverity.Warning));
            return;
        }

        var asset = _lastApplicationUpdateCheck?.Asset;
        if (asset is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(asset.ChecksumDownloadUrl))
        {
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Package Verification",
                "Not verified",
                "No checksum asset was found for the selected release package.",
                RuntimeDiagnosticSeverity.Warning));
            return;
        }

        ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
            "Package Verification",
            "Pending",
            $"{asset.ChecksumName ?? "Checksum"} will be verified after download.",
            RuntimeDiagnosticSeverity.Warning));
    }

    private void AddRestartPlanDiagnostic()
    {
        if (_lastApplicationUpdateDownload?.Success == true
            && !_lastApplicationUpdateDownload.IsVerified)
        {
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Restart Plan",
                "Blocked",
                "Verify the downloaded package before restart handoff.",
                RuntimeDiagnosticSeverity.Blocked));
            return;
        }

        if (_lastApplicationRestartPlan is null)
        {
            ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
                "Restart Plan",
                "Not planned",
                "Download a package or request a restart plan before closing the app.",
                RuntimeDiagnosticSeverity.Warning));
            return;
        }

        var detail = _lastApplicationRestartPlan.CanRestart
            ? $"{_lastApplicationRestartPlan.Message} Verify the release package before running the installer."
            : _lastApplicationRestartPlan.Message;
        ApplicationUpdateItems.Add(new RuntimeDiagnosticItemViewModel(
            "Restart Plan",
            _lastApplicationRestartPlan.CanRestart ? "Ready" : "Manual",
            SecretRedactor.Redact(detail),
            _lastApplicationRestartPlan.CanRestart
                ? RuntimeDiagnosticSeverity.Ready
                : RuntimeDiagnosticSeverity.Warning));
    }

    private static string BuildVerificationDetail(ApplicationUpdateDownloadResult download)
    {
        var detail = string.IsNullOrWhiteSpace(download.VerificationStatus)
            ? "Package verification did not produce a status."
            : download.VerificationStatus;

        if (!string.IsNullOrWhiteSpace(download.ExpectedSha256)
            && !string.IsNullOrWhiteSpace(download.ActualSha256))
        {
            detail += $" Expected {download.ExpectedSha256}; actual {download.ActualSha256}.";
        }

        return SecretRedactor.Redact(detail);
    }

    private string CurrentApplicationVersion()
    {
        return _applicationVersionProvider?.CurrentVersion
            ?? _applicationUpdateService?.CurrentVersion
            ?? "Unknown";
    }

    private static string FormatApplicationUpdateStatus(ApplicationUpdateCheckResult update)
    {
        if (!update.Success)
        {
            return "Check failed";
        }

        return update.IsUpdateAvailable ? "Update available" : "Up to date";
    }

    private static string BuildApplicationUpdateFeedDetail(ApplicationUpdateCheckResult update)
    {
        if (!update.Success)
        {
            return SecretRedactor.Redact(update.Message);
        }

        var latestVersion = string.IsNullOrWhiteSpace(update.LatestVersion)
            ? "(unknown)"
            : update.LatestVersion;
        var releaseName = string.IsNullOrWhiteSpace(update.ReleaseName)
            ? "latest release"
            : update.ReleaseName;

        return $"{releaseName}, latest {latestVersion}.";
    }

    private static RuntimeDiagnosticSeverity GetApplicationUpdateSeverity(ApplicationUpdateCheckResult update)
    {
        if (!update.Success)
        {
            return RuntimeDiagnosticSeverity.Warning;
        }

        return update.IsUpdateAvailable
            ? RuntimeDiagnosticSeverity.Warning
            : RuntimeDiagnosticSeverity.Ready;
    }

    private RuntimeDiagnosticSeverity GetApplicationUpdateSummarySeverity()
    {
        if (_applicationUpdateService is null || _lastApplicationUpdateCheck is null)
        {
            return RuntimeDiagnosticSeverity.Warning;
        }

        return GetApplicationUpdateSeverity(_lastApplicationUpdateCheck);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:0.#} KB";
        }

        return $"{bytes / 1024d / 1024d:0.#} MB";
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

    private static RuntimeDiagnosticItemViewModel BuildGitHubAppIntegrationItem(
        GitHubAppConnectionStatus status,
        GitHubRepositoryListResult? repositories = null)
    {
        if (!status.IsConfigured)
        {
            var missing = status.MissingSettings is { Count: > 0 }
                ? $" Missing: {string.Join(", ", status.MissingSettings)}."
                : string.Empty;
            return new RuntimeDiagnosticItemViewModel(
                "GitHub App",
                "Not configured",
                SecretRedactor.Redact($"{status.Message}{missing}"),
                RuntimeDiagnosticSeverity.Warning);
        }

        if (!status.IsConnected)
        {
            return new RuntimeDiagnosticItemViewModel(
                "GitHub App",
                "Needs action",
                SecretRedactor.Redact(status.Message),
                RuntimeDiagnosticSeverity.Warning);
        }

        if (repositories is { Success: false })
        {
            return new RuntimeDiagnosticItemViewModel(
                "GitHub App",
                "Needs action",
                SecretRedactor.Redact($"GitHub App connected, but repository list validation failed: {repositories.Message}"),
                RuntimeDiagnosticSeverity.Warning);
        }

        var repositoryCount = status.RepositoryCount ?? repositories?.Repositories.Count ?? 0;
        var value = repositoryCount == 1 ? "1 repo" : $"{repositoryCount} repos";
        var appName = string.IsNullOrWhiteSpace(status.AppName)
            ? "Configured GitHub App"
            : status.AppName.Trim();
        var repositoryDetail = FormatGitHubRepositoryPreview(
            repositories?.Repositories ?? [],
            repositoryCount);

        return new RuntimeDiagnosticItemViewModel(
            "GitHub App",
            value,
            SecretRedactor.Redact($"{appName} repo list access verified.{repositoryDetail}"),
            RuntimeDiagnosticSeverity.Ready);
    }

    private static string FormatGitHubRepositoryPreview(
        IReadOnlyList<GitHubRepositorySummary> repositories,
        int expectedRepositoryCount)
    {
        if (repositories.Count == 0)
        {
            return string.Empty;
        }

        const int maxVisibleRepositories = 3;
        var visible = repositories
            .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
            .Take(maxVisibleRepositories)
            .Select(repository =>
                $"{repository.FullName} ({(repository.IsPrivate ? "private" : "public")}, {repository.DefaultBranch})")
            .ToArray();
        var detail = $" Repositories: {string.Join("; ", visible)}";
        var hiddenCount = Math.Max(expectedRepositoryCount, repositories.Count) - visible.Length;
        if (hiddenCount > 0)
        {
            detail += $"; and {hiddenCount} more";
        }

        return detail + ".";
    }

    private void ReplaceIntegrationItem(RuntimeDiagnosticItemViewModel item)
    {
        var existing = IntegrationItems.FirstOrDefault(existingItem =>
            existingItem.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            IntegrationItems.Remove(existing);
        }

        IntegrationItems.Add(item);
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

    private void OnCommandLoopEvidenceChanged(object? sender, EventArgs e)
    {
        ApplyCommandLoopDiagnostics();
        RebuildSummary();
    }

    private RuntimeDiagnosticSeverity GetSkillSummarySeverity()
    {
        var skillSmokeItem = RuntimeItems.FirstOrDefault(item =>
            item.Name.Equals("Skill Smoke", StringComparison.OrdinalIgnoreCase));
        if (skillSmokeItem is not null)
        {
            return skillSmokeItem.Severity;
        }

        return SkillStatus.Contains("healthy", StringComparison.OrdinalIgnoreCase)
            ? RuntimeDiagnosticSeverity.Ready
            : RuntimeDiagnosticSeverity.Warning;
    }

    private CommandLoopSummary GetCommandLoopSummary()
    {
        var plannerTrace = RuntimeItems.FirstOrDefault(item =>
            item.Name.Equals("Planner Trace", StringComparison.OrdinalIgnoreCase));
        var skillResult = RuntimeItems.FirstOrDefault(item =>
            item.Name.Equals("Skill Result", StringComparison.OrdinalIgnoreCase));

        if (plannerTrace?.IsBlocked == true || skillResult?.IsBlocked == true)
        {
            return new CommandLoopSummary(
                "Action needed",
                "Latest planner trace or skill result is blocked.",
                RuntimeDiagnosticSeverity.Blocked);
        }

        if (plannerTrace?.IsReady == true && skillResult?.IsReady == true)
        {
            return new CommandLoopSummary(
                "Ready",
                "Latest command produced a valid plan and normalized result.",
                RuntimeDiagnosticSeverity.Ready);
        }

        return new CommandLoopSummary(
            "Needs command",
            "Submit a command to verify planner and skill execution together.",
            RuntimeDiagnosticSeverity.Warning);
    }

    private static string BuildSkillSmokeFailureDetail(SkillEvalSummary summary)
    {
        var failedResult = summary.Results.FirstOrDefault(result => !result.Passed);
        if (failedResult is null)
        {
            return $"{summary.Failed} smoke evals failed.";
        }

        var skillId = string.IsNullOrWhiteSpace(failedResult.SkillId)
            ? failedResult.Name
            : failedResult.SkillId;
        return $"{skillId}: {failedResult.Message}";
    }

    private static string BuildSkillResultDetail(SkillExecutionHistoryEntry entry)
    {
        var parts = new List<string>
        {
            $"{ValueOrMissing(entry.SkillId)}, {FormatDuration(entry.DurationMilliseconds)}"
        };

        if (!string.IsNullOrWhiteSpace(entry.ResultSummary))
        {
            parts.Add(SecretRedactor.Redact(entry.ResultSummary));
        }

        if (!string.IsNullOrWhiteSpace(entry.ErrorCode))
        {
            parts.Add($"error {SecretRedactor.Redact(entry.ErrorCode)}");
        }

        return string.Join(". ", parts) + ".";
    }

    private static string FormatSkillExecutionStatus(SkillExecutionStatus status)
    {
        return status switch
        {
            SkillExecutionStatus.Cancelled => "Cancelled",
            SkillExecutionStatus.Disabled => "Disabled",
            SkillExecutionStatus.ExecutorNotFound => "Executor Missing",
            SkillExecutionStatus.Failed => "Failed",
            SkillExecutionStatus.PermissionDenied => "Permission Denied",
            SkillExecutionStatus.ReviewRequired => "Review Required",
            SkillExecutionStatus.SkillNotFound => "Skill Missing",
            SkillExecutionStatus.Succeeded => "Succeeded",
            SkillExecutionStatus.TimedOut => "Timed Out",
            SkillExecutionStatus.ValidationFailed => "Validation Failed",
            _ => status.ToString()
        };
    }

    private static string FormatDuration(long durationMilliseconds)
    {
        return durationMilliseconds <= 0 ? "<1 ms" : $"{durationMilliseconds} ms";
    }

    private sealed record CommandLoopSummary(
        string Value,
        string Detail,
        RuntimeDiagnosticSeverity Severity);

    private static bool RequiresApiKey(ModelProviderType provider) => provider != ModelProviderType.Ollama;

    private static bool HasValue(string value) => !string.IsNullOrWhiteSpace(value);

    private static string ValueOrMissing(string value) => HasValue(value) ? value : "Missing";

    private static bool IsReadyForLiveConnectionTest(ModelProviderProfile profile)
    {
        return profile.Enabled && profile.Validate().IsValid;
    }

    private static string SanitizeDiagnosticMessage(string message, string secret)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(secret))
        {
            return message;
        }

        return message.Replace(secret, "[redacted]", StringComparison.Ordinal);
    }
}

using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class PluginItem : ReactiveObject
    {
        private bool _hasEvalResult;
        private string _lastEvalStatus = string.Empty;
        private string _lastEvalDetail = string.Empty;
        private string _requiredPermissionsText = "Requires: none";
        private string _grantedPermissionsText = "Granted: none";
        private string _missingPermissionsText = string.Empty;

        public string SkillId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string HealthDetail { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool CanApproveReview { get; set; }
        public bool CanEnable { get; set; }
        public bool CanDisable { get; set; }
        public bool CanGrantPermissions { get; set; }
        public bool CanRevokePermissions { get; set; }
        public ICommand? ApproveReviewCommand { get; set; }
        public ICommand? EnableCommand { get; set; }
        public ICommand? DisableCommand { get; set; }
        public ICommand? GrantPermissionsCommand { get; set; }
        public ICommand? RevokePermissionsCommand { get; set; }

        public bool HasEvalResult
        {
            get => _hasEvalResult;
            set => this.RaiseAndSetIfChanged(ref _hasEvalResult, value);
        }

        public string LastEvalStatus
        {
            get => _lastEvalStatus;
            set => this.RaiseAndSetIfChanged(ref _lastEvalStatus, value);
        }

        public string LastEvalDetail
        {
            get => _lastEvalDetail;
            set => this.RaiseAndSetIfChanged(ref _lastEvalDetail, value);
        }

        public string RequiredPermissionsText
        {
            get => _requiredPermissionsText;
            set => this.RaiseAndSetIfChanged(ref _requiredPermissionsText, value);
        }

        public string GrantedPermissionsText
        {
            get => _grantedPermissionsText;
            set => this.RaiseAndSetIfChanged(ref _grantedPermissionsText, value);
        }

        public string MissingPermissionsText
        {
            get => _missingPermissionsText;
            set => this.RaiseAndSetIfChanged(ref _missingPermissionsText, value);
        }

        public bool HasMissingPermissions => !string.IsNullOrWhiteSpace(MissingPermissionsText);

        // Color properties for the new design - use theme-aware colors
        public IBrush IconColor { get; set; } = Brush.Parse("#06B6D4");
        public IBrush GlowColor { get; set; } = Brush.Parse("#1606B6D4");
        // TextColor is now dynamic - returns TextPrimaryBrush for active plugins
        public IBrush TextColor => IsActive ? GetThemeTextBrush() : Brush.Parse("#71717A");
        public IBrush StatusColor { get; set; } = Brush.Parse("#10B981");
        
        private IBrush GetThemeTextBrush()
        {
            var app = global::Avalonia.Application.Current;
            if (app?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark)
            {
                return Brush.Parse("#FAFAFA"); // White for dark mode
            }
            return Brush.Parse("#18181B"); // Dark for light mode
        }
    }

    public class PluginsViewModel : ViewModelBase
    {
        private ObservableCollection<PluginItem> _plugins = new();
        private string _skillEvalStatus = "Smoke evals not run";
        private string _skillEvalDetail = "Open this screen with runtime services available to run smoke evals.";
        private bool _isSkillEvalHealthy;
        private string _importLocation = string.Empty;
        private int _selectedImportSourceIndex;
        private string _importStatus = "Import local or skills.sh folders containing SKILL.md.";
        private ISkillHealthService? _skillHealthService;
        private ISkillImportService? _skillImportService;
        private ISkillPolicyManager? _skillPolicyManager;
        private ISkillEvalHarness? _skillEvalHarness;
        private ISkillEvalCaseCatalog? _skillEvalCaseCatalog;
        private SkillEvalSummary? _lastEvalSummary;

        public ObservableCollection<PluginItem> Plugins
        {
            get => _plugins;
            set => this.RaiseAndSetIfChanged(ref _plugins, value);
        }

        public string SkillEvalStatus
        {
            get => _skillEvalStatus;
            private set => this.RaiseAndSetIfChanged(ref _skillEvalStatus, value);
        }

        public string SkillEvalDetail
        {
            get => _skillEvalDetail;
            private set => this.RaiseAndSetIfChanged(ref _skillEvalDetail, value);
        }

        public bool IsSkillEvalHealthy
        {
            get => _isSkillEvalHealthy;
            private set => this.RaiseAndSetIfChanged(ref _isSkillEvalHealthy, value);
        }

        public ObservableCollection<string> ImportSources { get; } =
            new(["Local folder", "skills.sh folder"]);

        public string ImportLocation
        {
            get => _importLocation;
            set => this.RaiseAndSetIfChanged(ref _importLocation, value);
        }

        public int SelectedImportSourceIndex
        {
            get => _selectedImportSourceIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedImportSourceIndex, value);
        }

        public string ImportStatus
        {
            get => _importStatus;
            private set => this.RaiseAndSetIfChanged(ref _importStatus, value);
        }

        public ICommand ImportSkillCommand { get; }
        public ICommand RunSkillEvalCommand { get; }

        public PluginsViewModel()
        {
            Title = "SKILLS";
            ImportSkillCommand = ReactiveCommand.CreateFromTask(ImportSkillAsync);
            RunSkillEvalCommand = ReactiveCommand.CreateFromTask(RunSkillEvalAsync);
            LoadPlugins(CreateBuiltInHealthSnapshot());
        }

        public PluginsViewModel(IEnumerable<SkillHealthReport> skillHealthReports)
            : this()
        {
            Title = "SKILLS";
            LoadPlugins(skillHealthReports);
        }

        public PluginsViewModel(
            IEnumerable<SkillHealthReport> skillHealthReports,
            SkillEvalSummary? evalSummary)
            : this(skillHealthReports)
        {
            ApplyEvalSummary(evalSummary);
        }

        public PluginsViewModel(ISkillHealthService skillHealthService)
            : this()
        {
            _skillHealthService = skillHealthService;
            _ = RefreshHealthAsync(skillHealthService);
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillImportService skillImportService)
            : this(skillHealthService)
        {
            _skillImportService = skillImportService;
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillPolicyManager skillPolicyManager)
            : this()
        {
            _skillHealthService = skillHealthService;
            _skillPolicyManager = skillPolicyManager;
            _ = RefreshHealthAsync(skillHealthService);
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog)
            : this()
        {
            _skillHealthService = skillHealthService;
            _skillEvalHarness = skillEvalHarness;
            _skillEvalCaseCatalog = skillEvalCaseCatalog;
            _ = RefreshRuntimeStateAsync(skillHealthService, skillEvalHarness, skillEvalCaseCatalog);
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog,
            ISkillImportService skillImportService)
            : this(skillHealthService, skillEvalHarness, skillEvalCaseCatalog)
        {
            _skillImportService = skillImportService;
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog,
            ISkillPolicyManager skillPolicyManager)
            : this()
        {
            _skillHealthService = skillHealthService;
            _skillEvalHarness = skillEvalHarness;
            _skillEvalCaseCatalog = skillEvalCaseCatalog;
            _skillPolicyManager = skillPolicyManager;
            _ = RefreshRuntimeStateAsync(skillHealthService, skillEvalHarness, skillEvalCaseCatalog);
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog,
            ISkillPolicyManager skillPolicyManager,
            ISkillImportService skillImportService)
            : this(skillHealthService, skillEvalHarness, skillEvalCaseCatalog, skillPolicyManager)
        {
            _skillImportService = skillImportService;
        }

        private static IReadOnlyCollection<SkillHealthReport> CreateBuiltInHealthSnapshot()
        {
            return BuiltInSkillManifestCatalog
                .CreateAll()
                .OrderBy(manifest => manifest.Id, StringComparer.OrdinalIgnoreCase)
                .Select(manifest =>
                {
                    var status = manifest.Enabled
                        ? SkillHealthStatus.Healthy
                        : SkillHealthStatus.Disabled;

                    return new SkillHealthReport
                    {
                        SkillId = manifest.Id,
                        DisplayName = manifest.DisplayName,
                        Description = manifest.Description,
                        Source = manifest.Source,
                        ExecutorType = manifest.ExecutorType,
                        RiskLevel = manifest.RiskLevel,
                        Status = status,
                        Details = status == SkillHealthStatus.Healthy
                            ? "Built-in skill configured."
                            : "Skill is disabled."
                    };
                })
                .ToArray();
        }

        private void LoadPlugins(IEnumerable<SkillHealthReport> skillHealthReports)
        {
            Plugins = new ObservableCollection<PluginItem>(
                skillHealthReports.Select(CreatePluginItem));
            ApplyPluginEvalResults(_lastEvalSummary);
        }

        private async Task RefreshHealthAsync(ISkillHealthService skillHealthService)
        {
            try
            {
                var reports = await skillHealthService.GetHealthAsync();
                await RunOnUiThreadAsync(() => LoadPlugins(reports));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to refresh skill health: {ex.Message}");
            }
        }

        private async Task RefreshRuntimeStateAsync(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog)
        {
            try
            {
                var reportsTask = skillHealthService.GetHealthAsync();
                var evalTask = skillEvalHarness.RunAsync(skillEvalCaseCatalog.CreateSmokeCases());

                var reports = await reportsTask;
                var evalSummary = await evalTask;

                await RunOnUiThreadAsync(() =>
                {
                    LoadPlugins(reports);
                    ApplyEvalSummary(evalSummary);
                });
            }
            catch (Exception ex)
            {
                await RunOnUiThreadAsync(() =>
                {
                    SkillEvalStatus = "Smoke evals failed to run";
                    SkillEvalDetail = ex.Message;
                    IsSkillEvalHealthy = false;
                });
            }
        }

        public async Task ImportSkillAsync()
        {
            if (_skillImportService is null)
            {
                ImportStatus = "Skill import service is not available.";
                return;
            }

            var location = ImportLocation.Trim();
            if (string.IsNullOrWhiteSpace(location))
            {
                ImportStatus = "Enter a folder path containing SKILL.md.";
                return;
            }

            var sourceKind = SelectedImportSourceIndex == 1
                ? SkillSourceKind.SkillsSh
                : SkillSourceKind.LocalDirectory;

            try
            {
                var result = await _skillImportService.ImportAsync(new SkillSourceDefinition
                {
                    Id = sourceKind == SkillSourceKind.SkillsSh ? "skills-sh-ui" : "local-ui",
                    Kind = sourceKind,
                    Location = location
                });

                ImportStatus = result.ImportedCount == 1
                    ? "Imported 1 skill. Review required before use."
                    : $"Imported {result.ImportedCount} skills. Review required before use.";

                if (result.ImportedCount == 0)
                {
                    ImportStatus = "No skills found. Select a folder containing SKILL.md.";
                }

                if (_skillHealthService is not null)
                {
                    await RefreshHealthAsync(_skillHealthService);
                }
            }
            catch (Exception ex)
            {
                ImportStatus = $"Import failed: {ex.Message}";
            }
        }

        public async Task GrantPermissionsAsync(string skillId)
        {
            if (_skillPolicyManager is null)
            {
                return;
            }

            await ApplyPolicyActionAsync(skillId, _skillPolicyManager.GrantPermissionsAsync);
        }

        public async Task RunSkillEvalAsync()
        {
            if (_skillEvalHarness is null || _skillEvalCaseCatalog is null)
            {
                SkillEvalStatus = "Smoke eval services unavailable";
                SkillEvalDetail = "Runtime eval services are not registered for this screen.";
                IsSkillEvalHealthy = false;
                return;
            }

            try
            {
                SkillEvalStatus = "Running smoke evals";
                SkillEvalDetail = "Executing skill smoke cases.";
                IsSkillEvalHealthy = false;

                var summary = await _skillEvalHarness.RunAsync(
                    _skillEvalCaseCatalog.CreateSmokeCases());

                await RunOnUiThreadAsync(() => ApplyEvalSummary(summary));
            }
            catch (Exception ex)
            {
                await RunOnUiThreadAsync(() =>
                {
                    SkillEvalStatus = "Smoke evals failed to run";
                    SkillEvalDetail = ex.Message;
                    IsSkillEvalHealthy = false;
                });
            }
        }

        private void ApplyEvalSummary(SkillEvalSummary? summary)
        {
            _lastEvalSummary = summary;

            if (summary is null || summary.Total <= 0)
            {
                SkillEvalStatus = "Smoke evals not run";
                SkillEvalDetail = "No smoke eval results are available.";
                IsSkillEvalHealthy = false;
                ApplyPluginEvalResults(null);
                return;
            }

            SkillEvalStatus = $"{summary.Passed}/{summary.Total} smoke evals passing";
            IsSkillEvalHealthy = summary.Failed == 0;

            var failingResult = summary.Results.FirstOrDefault(result => !result.Passed);
            SkillEvalDetail = failingResult is null
                ? "All smoke eval cases matched their expected status."
                : $"{failingResult.SkillId}: {failingResult.Message}";
            ApplyPluginEvalResults(summary);
        }

        private void ApplyPluginEvalResults(SkillEvalSummary? summary)
        {
            foreach (var plugin in Plugins)
            {
                var result = summary?.Results
                    .Where(candidate => candidate.SkillId.Equals(
                        plugin.SkillId,
                        StringComparison.OrdinalIgnoreCase))
                    .OrderBy(candidate => candidate.Passed)
                    .FirstOrDefault();

                if (result is null)
                {
                    plugin.HasEvalResult = false;
                    plugin.LastEvalStatus = string.Empty;
                    plugin.LastEvalDetail = string.Empty;
                    continue;
                }

                plugin.HasEvalResult = true;
                plugin.LastEvalStatus = result.Passed ? "Eval Pass" : "Eval Fail";
                plugin.LastEvalDetail = result.Message;
            }
        }

        private PluginItem CreatePluginItem(SkillHealthReport report)
        {
            var palette = GetStatusPalette(report.Status);

            var item = new PluginItem
            {
                SkillId = report.SkillId,
                Name = FormatName(report.DisplayName, report.SkillId),
                Description = string.IsNullOrWhiteSpace(report.Description)
                    ? report.Details
                    : report.Description,
                Status = FormatStatus(report.Status),
                Source = report.Source,
                HealthDetail = report.Details,
                IconColor = Brush.Parse(palette.IconColor),
                GlowColor = Brush.Parse(palette.GlowColor),
                StatusColor = Brush.Parse(palette.StatusColor),
                IconPath = GetIconPath(report),
                IsActive = report.Status == SkillHealthStatus.Healthy,
                CanApproveReview = report.Status == SkillHealthStatus.ReviewRequired,
                CanEnable = report.Status == SkillHealthStatus.Disabled,
                CanDisable = report.Status is SkillHealthStatus.Healthy
                    or SkillHealthStatus.MissingExecutor
                    or SkillHealthStatus.PermissionDenied,
                CanGrantPermissions = report.MissingPermissions.Count > 0
                    && report.Status == SkillHealthStatus.PermissionDenied,
                CanRevokePermissions = report.Status is SkillHealthStatus.Healthy
                    or SkillHealthStatus.MissingExecutor,
                RequiredPermissionsText = FormatPermissions("Requires", report.RequiredPermissions),
                GrantedPermissionsText = FormatPermissions("Granted", report.GrantedPermissions),
                MissingPermissionsText = report.MissingPermissions.Count == 0
                    ? string.Empty
                    : FormatPermissions("Missing", report.MissingPermissions)
            };

            AttachPolicyCommands(item);
            return item;
        }

        private void AttachPolicyCommands(PluginItem item)
        {
            if (_skillPolicyManager is null)
            {
                return;
            }

            item.ApproveReviewCommand = ReactiveCommand.CreateFromTask(
                () => ApplyPolicyActionAsync(item.SkillId, _skillPolicyManager.ApproveReviewAsync));
            item.EnableCommand = ReactiveCommand.CreateFromTask(
                () => ApplyPolicyActionAsync(item.SkillId, _skillPolicyManager.EnableAsync));
            item.DisableCommand = ReactiveCommand.CreateFromTask(
                () => ApplyPolicyActionAsync(item.SkillId, _skillPolicyManager.DisableAsync));
            item.GrantPermissionsCommand = ReactiveCommand.CreateFromTask(
                () => GrantPermissionsAsync(item.SkillId));
            item.RevokePermissionsCommand = ReactiveCommand.CreateFromTask(
                () => ApplyPolicyActionAsync(item.SkillId, _skillPolicyManager.RevokePermissionsAsync));
        }

        private async Task ApplyPolicyActionAsync(
            string skillId,
            Func<string, CancellationToken, Task<bool>> action)
        {
            var changed = await action(skillId, CancellationToken.None);
            if (!changed || _skillHealthService is null)
            {
                return;
            }

            var reports = await _skillHealthService.GetHealthAsync();
            await RunOnUiThreadAsync(() => LoadPlugins(reports));
        }

        private static string FormatName(string displayName, string skillId)
        {
            var name = string.IsNullOrWhiteSpace(displayName)
                ? skillId
                : displayName;

            return name.ToUpperInvariant();
        }

        private static string FormatStatus(SkillHealthStatus status)
        {
            return status switch
            {
                SkillHealthStatus.Healthy => "Healthy",
                SkillHealthStatus.Disabled => "Disabled",
                SkillHealthStatus.MissingExecutor => "Missing Executor",
                SkillHealthStatus.ReviewRequired => "Review Required",
                SkillHealthStatus.PermissionDenied => "Permission Denied",
                _ => "Unknown"
            };
        }

        private static string FormatPermissions(
            string label,
            IReadOnlyCollection<SkillPermission> permissions)
        {
            var values = permissions
                .Where(permission => permission != SkillPermission.None)
                .Distinct()
                .Select(permission => permission.ToString())
                .ToArray();

            return values.Length == 0
                ? $"{label}: none"
                : $"{label}: {string.Join(", ", values)}";
        }

        private static (string IconColor, string GlowColor, string StatusColor) GetStatusPalette(
            SkillHealthStatus status)
        {
            return status switch
            {
                SkillHealthStatus.Healthy => ("#22D3EE", "#2006B6D4", "#10B981"),
                SkillHealthStatus.MissingExecutor => ("#F59E0B", "#20F59E0B", "#F59E0B"),
                SkillHealthStatus.ReviewRequired => ("#F59E0B", "#20F59E0B", "#F59E0B"),
                SkillHealthStatus.PermissionDenied => ("#EF4444", "#20EF4444", "#EF4444"),
                SkillHealthStatus.Disabled => ("#71717A", "#1827272A", "#71717A"),
                _ => ("#71717A", "#1827272A", "#71717A")
            };
        }

        private static string GetIconPath(SkillHealthReport report)
        {
            if (report.SkillId.StartsWith("apps.", StringComparison.OrdinalIgnoreCase))
            {
                return "M13,3H5A2,2 0 0,0 3,5V13H5V5H13V3M19,7H9A2,2 0 0,0 7,9V19A2,2 0 0,0 9,21H19A2,2 0 0,0 21,19V9A2,2 0 0,0 19,7Z";
            }

            if (report.SkillId.StartsWith("files.", StringComparison.OrdinalIgnoreCase))
            {
                return "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M13,9V3.5L18.5,9H13Z";
            }

            if (report.SkillId.StartsWith("web.", StringComparison.OrdinalIgnoreCase))
            {
                return "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M18.9,8H15.97C15.65,6.75 15.15,5.56 14.5,4.48A8.03,8.03 0 0,1 18.9,8Z";
            }

            return "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,6V13H13V6H11M11,15V17H13V15H11Z";
        }

        private static async Task RunOnUiThreadAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess() || global::Avalonia.Application.Current is null)
            {
                action();
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(action);
        }

        public override void OnNavigatedTo()
        {
        }

        public override void OnNavigatedFrom()
        {
        }
    }
}

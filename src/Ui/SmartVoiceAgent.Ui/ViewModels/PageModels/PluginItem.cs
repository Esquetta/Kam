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
using System.Threading.Tasks;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class PluginItem : ReactiveObject
    {
        public string SkillId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string HealthDetail { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public bool IsActive { get; set; }

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

        public PluginsViewModel()
        {
            Title = "SKILLS";
            LoadPlugins(CreateBuiltInHealthSnapshot());
        }

        public PluginsViewModel(IEnumerable<SkillHealthReport> skillHealthReports)
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
            _ = RefreshHealthAsync(skillHealthService);
        }

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog)
            : this()
        {
            _ = RefreshRuntimeStateAsync(skillHealthService, skillEvalHarness, skillEvalCaseCatalog);
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
        }

        private async Task RefreshHealthAsync(ISkillHealthService skillHealthService)
        {
            try
            {
                var reports = await skillHealthService.GetHealthAsync();
                await Dispatcher.UIThread.InvokeAsync(() => LoadPlugins(reports));
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

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    LoadPlugins(reports);
                    ApplyEvalSummary(evalSummary);
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SkillEvalStatus = "Smoke evals failed to run";
                    SkillEvalDetail = ex.Message;
                    IsSkillEvalHealthy = false;
                });
            }
        }

        private void ApplyEvalSummary(SkillEvalSummary? summary)
        {
            if (summary is null || summary.Total <= 0)
            {
                SkillEvalStatus = "Smoke evals not run";
                SkillEvalDetail = "No smoke eval results are available.";
                IsSkillEvalHealthy = false;
                return;
            }

            SkillEvalStatus = $"{summary.Passed}/{summary.Total} smoke evals passing";
            IsSkillEvalHealthy = summary.Failed == 0;

            var failingResult = summary.Results.FirstOrDefault(result => !result.Passed);
            SkillEvalDetail = failingResult is null
                ? "All smoke eval cases matched their expected status."
                : $"{failingResult.SkillId}: {failingResult.Message}";
        }

        private static PluginItem CreatePluginItem(SkillHealthReport report)
        {
            var palette = GetStatusPalette(report.Status);

            return new PluginItem
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
                IsActive = report.Status == SkillHealthStatus.Healthy
            };
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

        public override void OnNavigatedTo()
        {
        }

        public override void OnNavigatedFrom()
        {
        }
    }
}

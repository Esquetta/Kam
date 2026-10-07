using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Skills;
using SmartVoiceAgent.Infrastructure.Skills.BuiltIn;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using SmartVoiceAgent.Infrastructure.Skills.Adapters;
using SmartVoiceAgent.Infrastructure.Skills.Importing;
using SmartVoiceAgent.Infrastructure.Skills.Policy;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class PluginItem : ReactiveObject
    {
        private bool _hasEvalResult;
        private string _lastEvalStatus = string.Empty;
        private string _lastEvalDetail = string.Empty;
        private bool _hasLastRun;
        private string _lastRunStatus = string.Empty;
        private string _lastRunDetail = string.Empty;
        private string _healthMetricsText = string.Empty;
        private string _failureMetricsText = string.Empty;
        private string _requiredPermissionsText =
            Loc.Format("Skills.Permissions.Requires", Loc.Get("Skills.Permissions.None"));
        private string _grantedPermissionsText =
            Loc.Format("Skills.Permissions.Granted", Loc.Get("Skills.Permissions.None"));
        private string _missingPermissionsText = string.Empty;
        private string _policyGuardrailText = string.Empty;
        private bool _isOn;
        private bool _isSelected;
        private bool _isVisible = true;

        public string SkillId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string ExecutorType { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;

        /// <summary>Gets or sets the second line of the row: what needs fixing, the description, or the permissions it uses.</summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>Gets whether the details repeat the description because the row shows what needs fixing instead.</summary>
        public bool ShowsDescriptionInDetails => NeedsAttention && !string.IsNullOrWhiteSpace(Description) && Description != Summary;
        public string ExecutorText { get; set; } = string.Empty;
        public string RiskLevelText { get; set; } = string.Empty;
        public string ChecksumText { get; set; } = string.Empty;
        public string InstalledFromText { get; set; } = string.Empty;
        public string HealthDetail { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool CanTestSkill { get; set; }
        public bool CanApproveReview { get; set; }
        public bool CanEnable { get; set; }
        public bool CanDisable { get; set; }
        public bool CanGrantPermissions { get; set; }
        public bool CanRevokePermissions { get; set; }
        public ICommand? SelectCommand { get; set; }
        public ICommand? TestSkillCommand { get; set; }
        public ICommand? ApproveReviewCommand { get; set; }
        public ICommand? EnableCommand { get; set; }
        public ICommand? DisableCommand { get; set; }
        public ICommand? GrantPermissionsCommand { get; set; }
        public ICommand? RevokePermissionsCommand { get; set; }
        public IBrush LastRunColor { get; set; } = Brush.Parse("#71717A");
        public Dictionary<string, string> RuntimeOptions { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);

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

        public bool HasLastRun
        {
            get => _hasLastRun;
            set => this.RaiseAndSetIfChanged(ref _hasLastRun, value);
        }

        public string LastRunStatus
        {
            get => _lastRunStatus;
            set => this.RaiseAndSetIfChanged(ref _lastRunStatus, value);
        }

        public string LastRunDetail
        {
            get => _lastRunDetail;
            set => this.RaiseAndSetIfChanged(ref _lastRunDetail, value);
        }

        public string HealthMetricsText
        {
            get => _healthMetricsText;
            set => this.RaiseAndSetIfChanged(ref _healthMetricsText, value);
        }

        public bool HasHealthMetrics => !string.IsNullOrWhiteSpace(HealthMetricsText);

        public string FailureMetricsText
        {
            get => _failureMetricsText;
            set => this.RaiseAndSetIfChanged(ref _failureMetricsText, value);
        }

        public bool HasFailureMetrics => !string.IsNullOrWhiteSpace(FailureMetricsText);

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

        public string PolicyGuardrailText
        {
            get => _policyGuardrailText;
            set => this.RaiseAndSetIfChanged(ref _policyGuardrailText, value);
        }

        public ObservableCollection<SkillExecutionHistoryItem> ExecutionHistory { get; set; } = new();

        public bool HasExecutionHistory => ExecutionHistory.Count > 0;

        /// <summary>Gets or sets the group the skill is listed under, such as Files or Web.</summary>
        public SkillCategory Category { get; set; } = SkillCategory.Other;

        /// <summary>Gets or sets the icon resource shown on the row.</summary>
        public string IconKey { get; set; } = "IconSparkles";

        /// <summary>Gets whether the skill is turned on but cannot run until someone acts on it.</summary>
        public bool NeedsAttention { get; set; }

        /// <summary>Gets whether the skill is turned off.</summary>
        public bool IsOff { get; set; }

        /// <summary>Gets whether the details end with actions: test the skill or revoke its permissions.</summary>
        public bool HasDetailActions => CanTestSkill || CanRevokePermissions;

        /// <summary>Gets whether the row shows a one-click fix: approve the review or grant permissions.</summary>
        public bool HasQuickFix => CanApproveReview || CanGrantPermissions;

        /// <summary>Gets whether the row shows an on/off switch.</summary>
        public bool CanToggle => SetEnabled is not null && (CanEnable || CanDisable);

        /// <summary>Gets or sets what turns the skill on or off; set by the page.</summary>
        public Func<bool, Task>? SetEnabled { get; set; }

        /// <summary>
        /// Gets or sets whether the skill is on. Changing it turns the skill on or off.
        /// </summary>
        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value)
                {
                    return;
                }

                this.RaiseAndSetIfChanged(ref _isOn, value);
                _ = SetEnabled?.Invoke(value);
            }
        }

        /// <summary>Gets or sets whether the row is expanded to show its details.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        /// <summary>Gets or sets whether the row matches the page search and filter.</summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => this.RaiseAndSetIfChanged(ref _isVisible, value);
        }

        /// <summary>
        /// Sets the switch without turning the skill on or off, for example after the change failed.
        /// </summary>
        /// <param name="isOn">Whether the switch shows on.</param>
        public void ShowIsOn(bool isOn)
        {
            _isOn = isOn;
            this.RaisePropertyChanged(nameof(IsOn));
        }
    }

    /// <summary>
    /// The groups the Skills page lists skills under, in display order.
    /// </summary>
    public enum SkillCategory
    {
        Apps,
        Files,
        Code,
        Web,
        Communication,
        System,
        Automation,
        Other
    }

    /// <summary>
    /// Which skills the Skills page lists.
    /// </summary>
    public enum SkillsFilter
    {
        All,
        On,
        Attention,
        Off
    }

    /// <summary>
    /// A titled group of skills on the Skills page.
    /// </summary>
    public sealed class SkillGroupViewModel : ReactiveObject
    {
        private int _visibleCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillGroupViewModel"/> class.
        /// </summary>
        /// <param name="category">The group.</param>
        /// <param name="title">The display title.</param>
        /// <param name="items">The skills in the group.</param>
        public SkillGroupViewModel(SkillCategory category, string title, IReadOnlyList<PluginItem> items)
        {
            Category = category;
            Title = title;
            Items = items;
            _visibleCount = items.Count;
        }

        /// <summary>Gets the group.</summary>
        public SkillCategory Category { get; }

        /// <summary>Gets the display title.</summary>
        public string Title { get; }

        /// <summary>Gets the skills in the group.</summary>
        public IReadOnlyList<PluginItem> Items { get; }

        /// <summary>Gets how many skills match the search and filter.</summary>
        public int VisibleCount
        {
            get => _visibleCount;
            private set
            {
                this.RaiseAndSetIfChanged(ref _visibleCount, value);
                this.RaisePropertyChanged(nameof(IsVisible));
            }
        }

        /// <summary>Gets whether any skill in the group matches.</summary>
        public bool IsVisible => VisibleCount > 0;

        /// <summary>Recounts the skills that match.</summary>
        public void Refresh()
        {
            VisibleCount = Items.Count(item => item.IsVisible);
        }
    }

    public sealed class SkillExecutionHistoryItem
    {
        public string TimestampText { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public string DetailText { get; set; } = string.Empty;

        public IBrush StatusColor { get; set; } = Brush.Parse("#71717A");
    }

    public sealed class SkillEvalResultItem
    {
        public string Name { get; set; } = string.Empty;

        public string SkillId { get; set; } = string.Empty;

        public string StatusText { get; set; } = string.Empty;

        public string ExpectedActualText { get; set; } = string.Empty;

        public string DetailText { get; set; } = string.Empty;

        public IBrush StatusColor { get; set; } = Brush.Parse("#71717A");
    }

    public class PluginsViewModel : ViewModelBase
    {
        private ObservableCollection<PluginItem> _plugins = new();
        private ObservableCollection<SkillEvalResultItem> _skillEvalResults = new();
        private Func<string> _skillEvalStatusSource = () => Loc.Get("Skills.Eval.NotRun");
        private Func<string> _skillEvalDetailSource = () => Loc.Get("Skills.Eval.NeedsRuntime");
        private string _skillEvalStatus = Loc.Get("Skills.Eval.NotRun");
        private string _skillEvalDetail = Loc.Get("Skills.Eval.NeedsRuntime");
        private bool _isSkillEvalHealthy;
        private bool _hasSkillEvalResults;
        private string _importLocation = string.Empty;
        private int _selectedImportSourceIndex;
        private Func<string> _importStatusSource = () => Loc.Get("Skills.Import.Hint");
        private string _importStatus = Loc.Get("Skills.Import.Hint");
        private PluginItem? _selectedPlugin;
        private bool _hasSelectedPlugin;
        private string _selectedSkillTitle = Loc.Get("Skills.Detail.NoSelection");
        private string _selectedSkillId = string.Empty;
        private string _selectedSkillSource = string.Empty;
        private string _selectedSkillExecutor = string.Empty;
        private string _selectedSkillRisk = string.Empty;
        private string _selectedSkillChecksum = string.Empty;
        private string _selectedSkillPermissions = string.Empty;
        private string _selectedSkillLastRun = string.Empty;
        private string _selectedSkillHealthMetrics = string.Empty;
        private string _selectedSkillPolicyGuardrail = string.Empty;
        private ObservableCollection<SkillExecutionHistoryItem> _selectedSkillExecutionHistory = new();
        private bool _hasSelectedSkillExecutionHistory;
        private bool _canEditSelectedSkillPolicy;
        private string _policyOptionKeyInput = string.Empty;
        private string _policyOptionValueInput = string.Empty;
        private ISkillHealthService? _skillHealthService;
        private ISkillImportService? _skillImportService;
        private ISkillPolicyManager? _skillPolicyManager;
        private ISkillEvalHarness? _skillEvalHarness;
        private ISkillEvalCaseCatalog? _skillEvalCaseCatalog;
        private ISkillTestService? _skillTestService;
        private SkillEvalSummary? _lastEvalSummary;
        private IReadOnlyCollection<SkillHealthReport> _lastReports = [];
        private bool _showsBuiltInSnapshot;
        private ObservableCollection<SkillGroupViewModel> _groups = new();
        private string _searchText = string.Empty;
        private SkillsFilter _filter = SkillsFilter.All;
        private int _onCount;
        private int _attentionCount;
        private int _offCount;
        private bool _hasNoMatches;
        private string _noMatchesText = string.Empty;
        private bool _showEvalResults;

        public ObservableCollection<PluginItem> Plugins
        {
            get => _plugins;
            set => this.RaiseAndSetIfChanged(ref _plugins, value);
        }

        /// <summary>Gets the skills grouped for the list, in display order.</summary>
        public ObservableCollection<SkillGroupViewModel> Groups
        {
            get => _groups;
            private set => this.RaiseAndSetIfChanged(ref _groups, value);
        }

        /// <summary>Gets or sets the text the list is filtered by.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                this.RaiseAndSetIfChanged(ref _searchText, value ?? string.Empty);
                ApplyFilter();
            }
        }

        /// <summary>Gets which skills the list shows.</summary>
        public SkillsFilter Filter
        {
            get => _filter;
            private set
            {
                this.RaiseAndSetIfChanged(ref _filter, value);
                this.RaisePropertyChanged(nameof(IsFilterAll));
                this.RaisePropertyChanged(nameof(IsFilterOn));
                this.RaisePropertyChanged(nameof(IsFilterAttention));
                this.RaisePropertyChanged(nameof(IsFilterOff));
            }
        }

        public bool IsFilterAll => Filter == SkillsFilter.All;
        public bool IsFilterOn => Filter == SkillsFilter.On;
        public bool IsFilterAttention => Filter == SkillsFilter.Attention;
        public bool IsFilterOff => Filter == SkillsFilter.Off;

        /// <summary>Gets how many skills are installed.</summary>
        public int SkillCount => Plugins.Count;

        /// <summary>Gets how many skills are on and ready.</summary>
        public int OnCount
        {
            get => _onCount;
            private set => this.RaiseAndSetIfChanged(ref _onCount, value);
        }

        /// <summary>Gets how many skills are on but cannot run yet.</summary>
        public int AttentionCount
        {
            get => _attentionCount;
            private set
            {
                this.RaiseAndSetIfChanged(ref _attentionCount, value);
                this.RaisePropertyChanged(nameof(HasAttention));
            }
        }

        /// <summary>Gets whether any skill needs attention.</summary>
        public bool HasAttention => AttentionCount > 0;

        /// <summary>Gets how many skills are off.</summary>
        public int OffCount
        {
            get => _offCount;
            private set => this.RaiseAndSetIfChanged(ref _offCount, value);
        }

        /// <summary>Gets whether the search and filter hide every skill.</summary>
        public bool HasNoMatches
        {
            get => _hasNoMatches;
            private set => this.RaiseAndSetIfChanged(ref _hasNoMatches, value);
        }

        /// <summary>Gets what the empty list says.</summary>
        public string NoMatchesText
        {
            get => _noMatchesText;
            private set => this.RaiseAndSetIfChanged(ref _noMatchesText, value);
        }

        /// <summary>Gets or sets whether the eval results list is open.</summary>
        public bool ShowEvalResults
        {
            get => _showEvalResults;
            set => this.RaiseAndSetIfChanged(ref _showEvalResults, value);
        }

        /// <summary>Gets or sets the folder picker the view provides; it returns null when cancelled.</summary>
        public Func<string, Task<string?>>? PickFolderAsync { get; set; }

        public ObservableCollection<SkillEvalResultItem> SkillEvalResults
        {
            get => _skillEvalResults;
            private set => this.RaiseAndSetIfChanged(ref _skillEvalResults, value);
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

        public bool HasSkillEvalResults
        {
            get => _hasSkillEvalResults;
            private set => this.RaiseAndSetIfChanged(ref _hasSkillEvalResults, value);
        }

        public ObservableCollection<string> ImportSources { get; } =
            new([Loc.Get("Skills.Import.LocalFolder"), Loc.Get("Skills.Import.SkillsShFolder")]);

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

        public PluginItem? SelectedPlugin
        {
            get => _selectedPlugin;
            private set => this.RaiseAndSetIfChanged(ref _selectedPlugin, value);
        }

        public bool HasSelectedPlugin
        {
            get => _hasSelectedPlugin;
            private set => this.RaiseAndSetIfChanged(ref _hasSelectedPlugin, value);
        }

        public string SelectedSkillTitle
        {
            get => _selectedSkillTitle;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillTitle, value);
        }

        public string SelectedSkillId
        {
            get => _selectedSkillId;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillId, value);
        }

        public string SelectedSkillSource
        {
            get => _selectedSkillSource;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillSource, value);
        }

        public string SelectedSkillExecutor
        {
            get => _selectedSkillExecutor;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillExecutor, value);
        }

        public string SelectedSkillRisk
        {
            get => _selectedSkillRisk;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillRisk, value);
        }

        public string SelectedSkillChecksum
        {
            get => _selectedSkillChecksum;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillChecksum, value);
        }

        public string SelectedSkillPermissions
        {
            get => _selectedSkillPermissions;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillPermissions, value);
        }

        public string SelectedSkillLastRun
        {
            get => _selectedSkillLastRun;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillLastRun, value);
        }

        public string SelectedSkillHealthMetrics
        {
            get => _selectedSkillHealthMetrics;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillHealthMetrics, value);
        }

        public string SelectedSkillPolicyGuardrail
        {
            get => _selectedSkillPolicyGuardrail;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillPolicyGuardrail, value);
        }

        public ObservableCollection<SkillExecutionHistoryItem> SelectedSkillExecutionHistory
        {
            get => _selectedSkillExecutionHistory;
            private set => this.RaiseAndSetIfChanged(ref _selectedSkillExecutionHistory, value);
        }

        public bool HasSelectedSkillExecutionHistory
        {
            get => _hasSelectedSkillExecutionHistory;
            private set => this.RaiseAndSetIfChanged(ref _hasSelectedSkillExecutionHistory, value);
        }

        public bool CanEditSelectedSkillPolicy
        {
            get => _canEditSelectedSkillPolicy;
            private set => this.RaiseAndSetIfChanged(ref _canEditSelectedSkillPolicy, value);
        }

        public string PolicyOptionKeyInput
        {
            get => _policyOptionKeyInput;
            set => this.RaiseAndSetIfChanged(ref _policyOptionKeyInput, value);
        }

        public string PolicyOptionValueInput
        {
            get => _policyOptionValueInput;
            set => this.RaiseAndSetIfChanged(ref _policyOptionValueInput, value);
        }

        public ICommand ImportSkillCommand { get; }
        public ICommand RunSkillEvalCommand { get; }
        public ICommand SaveRuntimePolicyOptionCommand { get; }
        public ReactiveCommand<string, Unit> SetFilterCommand { get; }
        public ICommand ToggleEvalResultsCommand { get; }
        public ICommand BrowseImportFolderCommand { get; }

        public PluginsViewModel()
        {
            Title = Loc.Get("Skills.Title");
            ImportSkillCommand = ReactiveCommand.CreateFromTask(ImportSkillAsync);
            RunSkillEvalCommand = ReactiveCommand.CreateFromTask(RunSkillEvalAsync);
            SaveRuntimePolicyOptionCommand = ReactiveCommand.CreateFromTask(SaveRuntimePolicyOptionAsync);
            SetFilterCommand = ReactiveCommand.Create<string>(SetFilter);
            ToggleEvalResultsCommand = ReactiveCommand.Create(() => ShowEvalResults = !ShowEvalResults);
            BrowseImportFolderCommand = ReactiveCommand.CreateFromTask(BrowseImportFolderAsync);
            LoadBuiltInSnapshot();
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        public PluginsViewModel(IEnumerable<SkillHealthReport> skillHealthReports)
            : this()
        {
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
            ISkillTestService skillTestService)
            : this()
        {
            _skillHealthService = skillHealthService;
            _skillTestService = skillTestService;
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

        public PluginsViewModel(
            ISkillHealthService skillHealthService,
            ISkillEvalHarness skillEvalHarness,
            ISkillEvalCaseCatalog skillEvalCaseCatalog,
            ISkillPolicyManager skillPolicyManager,
            ISkillImportService skillImportService,
            ISkillTestService skillTestService)
            : this(skillHealthService, skillEvalHarness, skillEvalCaseCatalog, skillPolicyManager, skillImportService)
        {
            _skillTestService = skillTestService;
            _ = RefreshHealthAsync(skillHealthService);
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
                            ? Loc.Get("Skills.BuiltIn.Configured")
                            : Loc.Get("Skills.BuiltIn.Disabled")
                    };
                })
                .ToArray();
        }

        private void LoadBuiltInSnapshot()
        {
            LoadPlugins(CreateBuiltInHealthSnapshot());
            _showsBuiltInSnapshot = true;
        }

        private void LoadPlugins(IEnumerable<SkillHealthReport> skillHealthReports)
        {
            var reports = skillHealthReports.ToArray();
            _lastReports = reports;
            _showsBuiltInSnapshot = false;
            var selectedSkillId = SelectedPlugin?.SkillId;
            Plugins = new ObservableCollection<PluginItem>(
                reports.Select(CreatePluginItem));
            this.RaisePropertyChanged(nameof(SkillCount));
            Groups = new ObservableCollection<SkillGroupViewModel>(Plugins
                .GroupBy(item => item.Category)
                .OrderBy(group => group.Key)
                .Select(group => new SkillGroupViewModel(
                    group.Key,
                    FormatCategory(group.Key),
                    group.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray())));
            OnCount = Plugins.Count(item => item.IsActive);
            AttentionCount = Plugins.Count(item => item.NeedsAttention);
            OffCount = Plugins.Count(item => item.IsOff);
            ApplyPluginEvalResults(_lastEvalSummary);
            ApplyFilter();
            SelectPlugin(selectedSkillId ?? string.Empty);
        }

        private void SetFilter(string filter)
        {
            Filter = Enum.TryParse<SkillsFilter>(filter, ignoreCase: true, out var value) ? value : SkillsFilter.All;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var search = SearchText.Trim();
            foreach (var item in Plugins)
            {
                item.IsVisible = MatchesFilter(item) && MatchesSearch(item, search);
            }

            foreach (var group in Groups)
            {
                group.Refresh();
            }

            HasNoMatches = Plugins.Count > 0 && Plugins.All(item => !item.IsVisible);
            NoMatchesText = search.Length > 0
                ? Loc.Format("Skills.NoMatches.Search", search)
                : Loc.Get("Skills.NoMatches.Filter");
        }

        private bool MatchesFilter(PluginItem item)
        {
            return Filter switch
            {
                SkillsFilter.On => item.IsActive,
                SkillsFilter.Attention => item.NeedsAttention,
                SkillsFilter.Off => item.IsOff,
                _ => true
            };
        }

        private static bool MatchesSearch(PluginItem item, string search)
        {
            return search.Length == 0
                || item.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.SkillId.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.Status.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        }

        private async Task BrowseImportFolderAsync()
        {
            if (PickFolderAsync is null)
            {
                return;
            }

            var folder = await PickFolderAsync(Loc.Get("Skills.Import.PickFolder"));
            if (!string.IsNullOrWhiteSpace(folder))
            {
                ImportLocation = folder;
            }
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
                    SetSkillEvalText(() => Loc.Get("Skills.Eval.FailedToRun"), () => ex.Message);
                    IsSkillEvalHealthy = false;
                });
            }
        }

        public async Task ImportSkillAsync()
        {
            if (_skillImportService is null)
            {
                SetImportStatus(() => Loc.Get("Skills.Import.Unavailable"));
                return;
            }

            var location = ImportLocation.Trim();
            if (string.IsNullOrWhiteSpace(location))
            {
                SetImportStatus(() => Loc.Get("Skills.Import.EnterPath"));
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

                var importedCount = result.ImportedCount;
                if (importedCount == 0)
                {
                    SetImportStatus(() => Loc.Get("Skills.Import.NoneFound"));
                }
                else if (importedCount == 1)
                {
                    SetImportStatus(() => Loc.Get("Skills.Import.ImportedOne"));
                }
                else
                {
                    SetImportStatus(() => Loc.Format("Skills.Import.ImportedMany", importedCount));
                }

                if (_skillHealthService is not null)
                {
                    await RefreshHealthAsync(_skillHealthService);
                }
            }
            catch (Exception ex)
            {
                SetImportStatus(() => Loc.Format("Skills.Import.Failed", ex.Message));
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

        public void SelectPlugin(string skillId)
        {
            var plugin = Plugins.FirstOrDefault(candidate => candidate.SkillId.Equals(
                skillId,
                StringComparison.OrdinalIgnoreCase));
            SelectedPlugin = plugin;
            HasSelectedPlugin = plugin is not null;
            foreach (var candidate in Plugins)
            {
                candidate.IsSelected = ReferenceEquals(candidate, plugin);
            }

            if (plugin is null)
            {
                SelectedSkillTitle = Loc.Get("Skills.Detail.NoSelection");
                SelectedSkillId = string.Empty;
                SelectedSkillSource = string.Empty;
                SelectedSkillExecutor = string.Empty;
                SelectedSkillRisk = string.Empty;
                SelectedSkillChecksum = string.Empty;
                SelectedSkillPermissions = string.Empty;
                SelectedSkillLastRun = string.Empty;
                SelectedSkillHealthMetrics = string.Empty;
                SelectedSkillPolicyGuardrail = string.Empty;
                SelectedSkillExecutionHistory = new ObservableCollection<SkillExecutionHistoryItem>();
                HasSelectedSkillExecutionHistory = false;
                CanEditSelectedSkillPolicy = false;
                PolicyOptionKeyInput = string.Empty;
                PolicyOptionValueInput = string.Empty;
                return;
            }

            SelectedSkillTitle = plugin.Name;
            SelectedSkillId = plugin.SkillId;
            SelectedSkillSource = Loc.Format("Skills.Detail.Source", plugin.Source);
            SelectedSkillExecutor = Loc.Format("Skills.Detail.Executor", plugin.ExecutorType);
            SelectedSkillRisk = plugin.RiskLevelText;
            SelectedSkillChecksum = plugin.ChecksumText;
            SelectedSkillPermissions = $"{plugin.RequiredPermissionsText} | {plugin.GrantedPermissionsText}";
            if (plugin.HasMissingPermissions)
            {
                SelectedSkillPermissions = $"{SelectedSkillPermissions} | {plugin.MissingPermissionsText}";
            }

            SelectedSkillLastRun = plugin.HasLastRun
                ? $"{plugin.LastRunStatus} | {plugin.LastRunDetail}"
                : Loc.Get("Skills.Detail.LastRunNone");
            SelectedSkillHealthMetrics = FormatSelectedHealthMetrics(plugin);
            SelectedSkillPolicyGuardrail = plugin.PolicyGuardrailText;
            SelectedSkillExecutionHistory = new ObservableCollection<SkillExecutionHistoryItem>(
                plugin.ExecutionHistory);
            HasSelectedSkillExecutionHistory = SelectedSkillExecutionHistory.Count > 0;
            CanEditSelectedSkillPolicy = _skillPolicyManager is not null
                && SkillRuntimePolicyOptions.IsEditableSkill(plugin.SkillId);
            PolicyOptionKeyInput = SkillRuntimePolicyOptions.GetDefaultOptionKey(plugin.SkillId);
            PolicyOptionValueInput = string.IsNullOrWhiteSpace(PolicyOptionKeyInput)
                ? string.Empty
                : plugin.RuntimeOptions.TryGetValue(PolicyOptionKeyInput, out var optionValue)
                    ? optionValue
                    : string.Empty;
        }

        public async Task SaveRuntimePolicyOptionAsync()
        {
            if (_skillPolicyManager is null || SelectedPlugin is null)
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Policy.Unavailable"),
                    () => Loc.Get("Skills.Policy.ManagerMissing"));
                IsSkillEvalHealthy = false;
                return;
            }

            var key = PolicyOptionKeyInput.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Policy.NotSaved"),
                    () => Loc.Get("Skills.Policy.KeyRequired"));
                IsSkillEvalHealthy = false;
                return;
            }

            var value = PolicyOptionValueInput.Trim();
            var skillId = SelectedPlugin.SkillId;
            var changed = await _skillPolicyManager.SetRuntimeOptionAsync(
                skillId,
                key,
                value,
                CancellationToken.None);
            if (!changed)
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Policy.NotSaved"),
                    () => Loc.Format("Skills.Policy.NotPersisted", skillId));
                IsSkillEvalHealthy = false;
                return;
            }

            if (_skillHealthService is not null)
            {
                await RefreshHealthAsync(_skillHealthService);
            }

            UpdateSelectedRuntimeOption(skillId, key, value);
            if (string.IsNullOrWhiteSpace(value))
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Policy.Saved"),
                    () => Loc.Format("Skills.Policy.OptionRemoved", skillId, key));
            }
            else
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Policy.Saved"),
                    () => Loc.Format("Skills.Policy.OptionSaved", skillId, key));
            }

            IsSkillEvalHealthy = true;
        }

        public async Task TestSkillAsync(string skillId)
        {
            if (_skillTestService is null)
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Test.Unavailable"),
                    () => Loc.Get("Skills.Test.ServiceMissing"));
                IsSkillEvalHealthy = false;
                return;
            }

            SetSkillEvalText(
                () => Loc.Get("Skills.Test.Running"),
                () => Loc.Format("Skills.Test.Executing", skillId));
            IsSkillEvalHealthy = false;

            try
            {
                var result = await _skillTestService.TestAsync(skillId);
                if (result.Success)
                {
                    SetSkillEvalText(() => Loc.Get("Skills.Test.Passed"), () => $"{skillId}: {result.Message}");
                }
                else
                {
                    SetSkillEvalText(() => Loc.Get("Skills.Test.Failed"), () => $"{skillId}: {result.ErrorMessage}");
                }

                IsSkillEvalHealthy = result.Success;

                if (_skillHealthService is not null)
                {
                    await RefreshHealthAsync(_skillHealthService);
                }
            }
            catch (Exception ex)
            {
                SetSkillEvalText(() => Loc.Get("Skills.Test.Failed"), () => ex.Message);
                IsSkillEvalHealthy = false;
            }
        }

        public async Task RunSkillEvalAsync()
        {
            if (_skillEvalHarness is null || _skillEvalCaseCatalog is null)
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Eval.ServicesUnavailable"),
                    () => Loc.Get("Skills.Eval.ServicesMissing"));
                IsSkillEvalHealthy = false;
                return;
            }

            try
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Eval.Running"),
                    () => Loc.Get("Skills.Eval.Executing"));
                IsSkillEvalHealthy = false;

                var summary = await _skillEvalHarness.RunAsync(
                    _skillEvalCaseCatalog.CreateSmokeCases());

                await RunOnUiThreadAsync(() => ApplyEvalSummary(summary));
            }
            catch (Exception ex)
            {
                await RunOnUiThreadAsync(() =>
                {
                    SetSkillEvalText(() => Loc.Get("Skills.Eval.FailedToRun"), () => ex.Message);
                    IsSkillEvalHealthy = false;
                });
            }
        }

        private void ApplyEvalSummary(SkillEvalSummary? summary)
        {
            _lastEvalSummary = summary;

            if (summary is null || summary.Total <= 0)
            {
                SetSkillEvalText(
                    () => Loc.Get("Skills.Eval.NotRun"),
                    () => Loc.Get("Skills.Eval.NoResults"));
                IsSkillEvalHealthy = false;
                RebuildSkillEvalResults();
                ApplyPluginEvalResults(null);
                return;
            }

            var passed = summary.Passed;
            var total = summary.Total;
            var failingResult = summary.Results.FirstOrDefault(result => !result.Passed);
            if (failingResult is null)
            {
                SetSkillEvalText(
                    () => Loc.Format("Skills.Eval.Passing", passed, total),
                    () => Loc.Get("Skills.Eval.AllMatched"));
            }
            else
            {
                SetSkillEvalText(
                    () => Loc.Format("Skills.Eval.Passing", passed, total),
                    () => $"{failingResult.SkillId}: {failingResult.Message}");
            }

            IsSkillEvalHealthy = summary.Failed == 0;
            RebuildSkillEvalResults();
            ApplyPluginEvalResults(summary);
        }

        private void RebuildSkillEvalResults()
        {
            SkillEvalResults = _lastEvalSummary is { Total: > 0 } summary
                ? new ObservableCollection<SkillEvalResultItem>(summary.Results.Select(CreateSkillEvalResultItem))
                : new ObservableCollection<SkillEvalResultItem>();
            HasSkillEvalResults = SkillEvalResults.Count > 0;
        }

        private void SetSkillEvalText(Func<string> status, Func<string> detail)
        {
            _skillEvalStatusSource = status;
            _skillEvalDetailSource = detail;
            SkillEvalStatus = status();
            SkillEvalDetail = detail();
        }

        private void SetImportStatus(Func<string> status)
        {
            _importStatusSource = status;
            ImportStatus = status();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            if (global::Avalonia.Application.Current is not null && !Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(ApplyLanguage);
                return;
            }

            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            Title = Loc.Get("Skills.Title");

            var importSourceIndex = _selectedImportSourceIndex;
            ImportSources[0] = Loc.Get("Skills.Import.LocalFolder");
            ImportSources[1] = Loc.Get("Skills.Import.SkillsShFolder");
            _selectedImportSourceIndex = importSourceIndex;
            this.RaisePropertyChanged(nameof(SelectedImportSourceIndex));

            ImportStatus = _importStatusSource();
            SkillEvalStatus = _skillEvalStatusSource();
            SkillEvalDetail = _skillEvalDetailSource();
            RebuildSkillEvalResults();

            var selectedSkillId = SelectedPlugin?.SkillId;
            var policyKey = PolicyOptionKeyInput;
            var policyValue = PolicyOptionValueInput;
            if (_showsBuiltInSnapshot)
            {
                LoadBuiltInSnapshot();
            }
            else
            {
                LoadPlugins(_lastReports);
            }

            if (selectedSkillId is not null
                && string.Equals(SelectedPlugin?.SkillId, selectedSkillId, StringComparison.OrdinalIgnoreCase))
            {
                PolicyOptionKeyInput = policyKey;
                PolicyOptionValueInput = policyValue;
            }
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
                plugin.LastEvalStatus = result.Passed
                    ? Loc.Get("Skills.Eval.CardPass")
                    : Loc.Get("Skills.Eval.CardFail");
                plugin.LastEvalDetail = result.Message;
            }
        }

        private PluginItem CreatePluginItem(SkillHealthReport report)
        {
            var item = new PluginItem
            {
                SkillId = report.SkillId,
                Name = FormatName(report.DisplayName, report.SkillId),
                Description = string.IsNullOrWhiteSpace(report.Description)
                    ? report.Details
                    : report.Description,
                Status = FormatStatus(report.Status),
                Source = report.Source,
                ExecutorType = string.IsNullOrWhiteSpace(report.ExecutorType)
                    ? Loc.Get("Skills.Detail.ExecutorUnknown")
                    : report.ExecutorType,
                SourceText = Loc.Format("Skills.Detail.Source", report.Source),
                ExecutorText = Loc.Format(
                    "Skills.Detail.Executor",
                    string.IsNullOrWhiteSpace(report.ExecutorType) ? Loc.Get("Skills.Detail.ExecutorUnknown") : report.ExecutorType),
                RiskLevelText = Loc.Format("Skills.Detail.Risk", FormatRiskLevel(report.RiskLevel)),
                ChecksumText = string.IsNullOrWhiteSpace(report.Checksum)
                    ? Loc.Get("Skills.Detail.ChecksumNone")
                    : Loc.Format("Skills.Detail.Checksum", report.Checksum),
                InstalledFromText = string.IsNullOrWhiteSpace(report.InstalledFrom)
                    ? Loc.Get("Skills.Detail.InstalledFromNone")
                    : Loc.Format("Skills.Detail.InstalledFrom", report.InstalledFrom),
                HealthDetail = report.Details,
                Category = GetCategory(report.SkillId),
                IconKey = GetIconKey(report.SkillId),
                IsActive = report.Status == SkillHealthStatus.Healthy,
                IsOff = report.Status == SkillHealthStatus.Disabled,
                NeedsAttention = report.Status is not SkillHealthStatus.Healthy and not SkillHealthStatus.Disabled,
                CanTestSkill = _skillTestService is not null
                    && report.Status == SkillHealthStatus.Healthy,
                CanApproveReview = report.Status == SkillHealthStatus.ReviewRequired,
                CanEnable = report.Status == SkillHealthStatus.Disabled,
                CanDisable = report.Status is SkillHealthStatus.Healthy
                    or SkillHealthStatus.MissingExecutor
                    or SkillHealthStatus.PermissionDenied,
                CanGrantPermissions = report.MissingPermissions.Count > 0
                    && report.Status == SkillHealthStatus.PermissionDenied,
                CanRevokePermissions = report.Status is SkillHealthStatus.Healthy
                    or SkillHealthStatus.MissingExecutor,
                HasLastRun = report.LastRunAt.HasValue,
                LastRunStatus = report.LastRunAt.HasValue
                    ? Loc.Format("Skills.Detail.LastRun", FormatExecutionStatus(report.LastRunStatus))
                    : string.Empty,
                LastRunDetail = FormatLastRunDetail(report),
                HealthMetricsText = FormatHealthMetrics(report),
                FailureMetricsText = FormatFailureMetrics(report),
                LastRunColor = GetExecutionStatusBrush(report.LastRunStatus),
                RequiredPermissionsText = Loc.Format(
                    "Skills.Permissions.Requires",
                    FormatPermissionList(report.RequiredPermissions)),
                GrantedPermissionsText = Loc.Format(
                    "Skills.Permissions.Granted",
                    FormatPermissionList(report.GrantedPermissions)),
                MissingPermissionsText = report.MissingPermissions.Count == 0
                    ? string.Empty
                    : Loc.Format("Skills.Permissions.Missing", FormatPermissionList(report.MissingPermissions)),
                RuntimeOptions = CloneRuntimeOptions(report.RuntimeOptions),
                ExecutionHistory = new ObservableCollection<SkillExecutionHistoryItem>(
                    report.RecentRuns
                        .OrderByDescending(record => record.Timestamp)
                        .Select(CreateExecutionHistoryItem)),
                PolicyGuardrailText = SkillRuntimePolicyOptions.Describe(
                    report.SkillId,
                    report.RuntimeOptions)
            };

            item.Summary = FormatSummary(report, item);
            AttachPolicyCommands(item);
            item.ShowIsOn(item.CanDisable);
            return item;
        }

        private void AttachPolicyCommands(PluginItem item)
        {
            item.SelectCommand = ReactiveCommand.Create(() => SelectPlugin(item.IsSelected ? string.Empty : item.SkillId));
            if (_skillTestService is not null)
            {
                item.TestSkillCommand = ReactiveCommand.CreateFromTask(
                    () => TestSkillAsync(item.SkillId));
            }

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
            item.SetEnabled = isOn => SetSkillEnabledAsync(item, isOn);
            item.GrantPermissionsCommand = ReactiveCommand.CreateFromTask(
                () => GrantPermissionsAsync(item.SkillId));
            item.RevokePermissionsCommand = ReactiveCommand.CreateFromTask(
                () => ApplyPolicyActionAsync(item.SkillId, _skillPolicyManager.RevokePermissionsAsync));
        }

        private async Task<bool> ApplyPolicyActionAsync(
            string skillId,
            Func<string, CancellationToken, Task<bool>> action)
        {
            var changed = await action(skillId, CancellationToken.None);
            if (!changed || _skillHealthService is null)
            {
                return changed;
            }

            var reports = await _skillHealthService.GetHealthAsync();
            await RunOnUiThreadAsync(() => LoadPlugins(reports));
            return true;
        }

        /// <summary>
        /// Turns a skill on or off from its row switch; the switch flips back when the change fails.
        /// </summary>
        /// <param name="item">The skill's row.</param>
        /// <param name="isOn">Whether to turn it on.</param>
        public async Task SetSkillEnabledAsync(PluginItem item, bool isOn)
        {
            var changed = false;
            try
            {
                if (_skillPolicyManager is not null)
                {
                    changed = await ApplyPolicyActionAsync(
                        item.SkillId,
                        isOn ? _skillPolicyManager.EnableAsync : _skillPolicyManager.DisableAsync);
                }
            }
            catch (Exception ex)
            {
                SetSkillEvalText(() => Loc.Format("Skills.Toggle.Failed", item.Name), () => ex.Message);
                IsSkillEvalHealthy = false;
            }

            if (!changed)
            {
                await RunOnUiThreadAsync(() => item.ShowIsOn(!isOn));
            }
        }

        private void UpdateSelectedRuntimeOption(string skillId, string key, string value)
        {
            var plugin = Plugins.FirstOrDefault(candidate => candidate.SkillId.Equals(
                skillId,
                StringComparison.OrdinalIgnoreCase));
            if (plugin is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                plugin.RuntimeOptions.Remove(key);
            }
            else
            {
                plugin.RuntimeOptions[key] = value;
            }

            plugin.PolicyGuardrailText = SkillRuntimePolicyOptions.Describe(
                plugin.SkillId,
                plugin.RuntimeOptions);
            SelectPlugin(plugin.SkillId);
        }

        private static string FormatName(string displayName, string skillId)
        {
            var name = string.IsNullOrWhiteSpace(displayName)
                ? skillId
                : displayName;

            return name.Trim();
        }

        private static string FormatStatus(SkillHealthStatus status)
        {
            return status switch
            {
                SkillHealthStatus.Healthy => Loc.Get("Skills.Status.Healthy"),
                SkillHealthStatus.Disabled => Loc.Get("Skills.Status.Disabled"),
                SkillHealthStatus.MissingExecutor => Loc.Get("Skills.Status.MissingExecutor"),
                SkillHealthStatus.ReviewRequired => Loc.Get("Skills.Status.ReviewRequired"),
                SkillHealthStatus.PermissionDenied => Loc.Get("Skills.Status.PermissionDenied"),
                _ => Loc.Get("Skills.Status.Unknown")
            };
        }

        private static string FormatSummary(SkillHealthReport report, PluginItem item)
        {
            if (item.NeedsAttention && !string.IsNullOrWhiteSpace(report.Details))
            {
                return report.Details;
            }

            if (!string.IsNullOrWhiteSpace(report.Description))
            {
                return report.Description;
            }

            var permissions = report.RequiredPermissions
                .Where(permission => permission != SkillPermission.None)
                .Distinct()
                .Select(permission => permission.ToString())
                .ToArray();
            var uses = permissions.Length == 0
                ? Loc.Get("Skills.Summary.NoPermissions")
                : Loc.Format("Skills.Summary.Uses", string.Join(", ", permissions));
            return $"{uses} · {item.RiskLevelText}";
        }

        private static string FormatCategory(SkillCategory category)
        {
            return category switch
            {
                SkillCategory.Apps => Loc.Get("Skills.Group.Apps"),
                SkillCategory.Files => Loc.Get("Skills.Group.Files"),
                SkillCategory.Code => Loc.Get("Skills.Group.Code"),
                SkillCategory.Web => Loc.Get("Skills.Group.Web"),
                SkillCategory.Communication => Loc.Get("Skills.Group.Communication"),
                SkillCategory.System => Loc.Get("Skills.Group.System"),
                SkillCategory.Automation => Loc.Get("Skills.Group.Automation"),
                _ => Loc.Get("Skills.Group.Other")
            };
        }

        private static string SkillPrefix(string skillId)
        {
            var dot = skillId.IndexOf('.');
            return (dot > 0 ? skillId[..dot] : skillId).ToLowerInvariant();
        }

        internal static SkillCategory GetCategory(string skillId)
        {
            return SkillPrefix(skillId) switch
            {
                "apps" => SkillCategory.Apps,
                "files" or "file" or "directories" => SkillCategory.Files,
                "workspace" or "code" => SkillCategory.Code,
                "web" => SkillCategory.Web,
                "communication" => SkillCategory.Communication,
                "system" or "clipboard" or "media" or "window" or "accessibility" => SkillCategory.System,
                "shell" or "agents" => SkillCategory.Automation,
                _ => SkillCategory.Other
            };
        }

        private static string GetIconKey(string skillId)
        {
            return SkillPrefix(skillId) switch
            {
                "apps" or "window" => "IconAppWindow",
                "files" or "directories" => "IconFolder",
                "file" => "IconFile",
                "workspace" => "IconListTree",
                "code" => "IconCode",
                "web" => "IconGlobe",
                "communication" => "IconMail",
                "system" => "IconCpu",
                "clipboard" => "IconCopy",
                "media" => "IconPlay",
                "accessibility" => "IconMonitor",
                "shell" => "IconTerminal",
                "agents" => "IconBot",
                _ => "IconSparkles"
            };
        }

        private static string FormatRiskLevel(SkillRiskLevel riskLevel)
        {
            return riskLevel switch
            {
                SkillRiskLevel.Low => Loc.Get("Skills.Risk.Low"),
                SkillRiskLevel.Medium => Loc.Get("Skills.Risk.Medium"),
                SkillRiskLevel.High => Loc.Get("Skills.Risk.High"),
                _ => riskLevel.ToString()
            };
        }

        private static string FormatPermissionList(IReadOnlyCollection<SkillPermission> permissions)
        {
            var values = permissions
                .Where(permission => permission != SkillPermission.None)
                .Distinct()
                .Select(permission => permission.ToString())
                .ToArray();

            return values.Length == 0
                ? Loc.Get("Skills.Permissions.None")
                : string.Join(", ", values);
        }

        private static SkillExecutionHistoryItem CreateExecutionHistoryItem(SkillAuditRecord record)
        {
            return new SkillExecutionHistoryItem
            {
                TimestampText = record.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                StatusText = FormatExecutionStatus(record.Status),
                DetailText = FormatAuditRecordDetail(record),
                StatusColor = GetExecutionStatusBrush(record.Status)
            };
        }

        private static SkillEvalResultItem CreateSkillEvalResultItem(SkillEvalResult result)
        {
            return new SkillEvalResultItem
            {
                Name = result.Name,
                SkillId = result.SkillId,
                StatusText = result.Passed ? Loc.Get("Skills.Eval.Pass") : Loc.Get("Skills.Eval.Fail"),
                ExpectedActualText = Loc.Format("Skills.Eval.ExpectedActual", result.ExpectedStatus, result.ActualStatus),
                DetailText = FormatEvalResultDetail(result),
                StatusColor = result.Passed
                    ? Brush.Parse("#10B981")
                    : Brush.Parse("#EF4444")
            };
        }

        private static string FormatAuditRecordDetail(SkillAuditRecord record)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(record.ResultMessage))
            {
                parts.Add(record.ResultMessage);
            }

            if (!string.IsNullOrWhiteSpace(record.ErrorCode))
            {
                parts.Add(record.ErrorCode);
            }

            if (record.DurationMilliseconds > 0)
            {
                parts.Add($"{record.DurationMilliseconds} ms");
            }

            return parts.Count == 0
                ? Loc.Get("Skills.Detail.NoExecutionDetail")
                : string.Join(" | ", parts);
        }

        private static string FormatEvalResultDetail(SkillEvalResult result)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                parts.Add(result.Message);
            }

            if (result.DurationMilliseconds > 0)
            {
                parts.Add($"{result.DurationMilliseconds} ms");
            }

            return parts.Count == 0
                ? Loc.Get("Skills.Eval.NoDetail")
                : string.Join(" | ", parts);
        }

        private static Dictionary<string, string> CloneRuntimeOptions(
            IReadOnlyDictionary<string, string>? runtimeOptions)
        {
            if (runtimeOptions is null)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return runtimeOptions
                .Where(option => !string.IsNullOrWhiteSpace(option.Key))
                .ToDictionary(
                    option => option.Key.Trim(),
                    option => option.Value ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase);
        }

        private static string FormatExecutionStatus(SkillExecutionStatus? status)
        {
            return status switch
            {
                SkillExecutionStatus.Succeeded => Loc.Get("Skills.Run.Succeeded"),
                SkillExecutionStatus.ValidationFailed => Loc.Get("Skills.Run.ValidationFailed"),
                SkillExecutionStatus.TimedOut => Loc.Get("Skills.Run.TimedOut"),
                SkillExecutionStatus.SkillNotFound => Loc.Get("Skills.Run.SkillNotFound"),
                SkillExecutionStatus.ExecutorNotFound => Loc.Get("Skills.Run.ExecutorNotFound"),
                SkillExecutionStatus.ReviewRequired => Loc.Get("Skills.Run.ReviewRequired"),
                SkillExecutionStatus.PermissionDenied => Loc.Get("Skills.Run.PermissionDenied"),
                SkillExecutionStatus.Cancelled => Loc.Get("Skills.Run.Cancelled"),
                SkillExecutionStatus.Disabled => Loc.Get("Skills.Run.Disabled"),
                SkillExecutionStatus.Failed => Loc.Get("Skills.Run.Failed"),
                _ => Loc.Get("Skills.Run.Unknown")
            };
        }

        private static string FormatLastRunDetail(SkillHealthReport report)
        {
            if (!report.LastRunAt.HasValue)
            {
                return string.Empty;
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(report.LastRunMessage))
            {
                parts.Add(report.LastRunMessage);
            }

            if (!string.IsNullOrWhiteSpace(report.LastRunErrorCode))
            {
                parts.Add(report.LastRunErrorCode);
            }

            if (report.LastRunDurationMilliseconds > 0)
            {
                parts.Add($"{report.LastRunDurationMilliseconds} ms");
            }

            return parts.Count == 0
                ? report.LastRunAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : string.Join(" | ", parts);
        }

        private static string FormatHealthMetrics(SkillHealthReport report)
        {
            if (report.RecentRunCount <= 0)
            {
                return string.Empty;
            }

            var parts = new List<string>
            {
                Loc.Format(
                    "Skills.Metrics.Recent",
                    report.RecentSuccessCount,
                    report.RecentRunCount,
                    report.RecentSuccessRatePercent)
            };

            if (report.RecentAverageDurationMilliseconds > 0)
            {
                parts.Add(Loc.Format("Skills.Metrics.Average", report.RecentAverageDurationMilliseconds));
            }

            return string.Join(" | ", parts);
        }

        private static string FormatFailureMetrics(SkillHealthReport report)
        {
            if (report.RecentFailureCount <= 0)
            {
                return string.Empty;
            }

            var parts = new List<string>
            {
                report.LastFailureAt.HasValue
                    ? Loc.Format(
                        "Skills.Metrics.LastFailure",
                        report.LastFailureAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
                    : Loc.Get("Skills.Metrics.LastFailureUnknown")
            };

            if (!string.IsNullOrWhiteSpace(report.LastFailureErrorCode))
            {
                parts.Add(report.LastFailureErrorCode);
            }

            if (!string.IsNullOrWhiteSpace(report.LastFailureMessage))
            {
                parts.Add(report.LastFailureMessage);
            }

            return string.Join(" | ", parts);
        }

        private static string FormatSelectedHealthMetrics(PluginItem plugin)
        {
            if (!plugin.HasHealthMetrics)
            {
                return Loc.Get("Skills.Metrics.NoRuns");
            }

            return plugin.HasFailureMetrics
                ? $"{plugin.HealthMetricsText} | {plugin.FailureMetricsText}"
                : plugin.HealthMetricsText;
        }

        private static IBrush GetExecutionStatusBrush(SkillExecutionStatus? status)
        {
            return status switch
            {
                SkillExecutionStatus.Succeeded => Brush.Parse("#10B981"),
                SkillExecutionStatus.Failed => Brush.Parse("#EF4444"),
                SkillExecutionStatus.ValidationFailed => Brush.Parse("#F59E0B"),
                SkillExecutionStatus.TimedOut => Brush.Parse("#F59E0B"),
                SkillExecutionStatus.ReviewRequired => Brush.Parse("#F59E0B"),
                SkillExecutionStatus.PermissionDenied => Brush.Parse("#EF4444"),
                _ => Brush.Parse("#71717A")
            };
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

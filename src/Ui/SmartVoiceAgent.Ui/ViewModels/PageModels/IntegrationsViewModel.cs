using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Security;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// ViewModel for the Integrations view - manages external service API keys
    /// </summary>
    public class IntegrationsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IGitHubAppClientFactory? _githubAppClientFactory;
        
        // Todoist
        private string _todoistApiKey = string.Empty;
        private bool _isTaskAgentEnabled;

        // GitHub App
        private string _githubAppId = string.Empty;
        private string _githubInstallationId = string.Empty;
        private string _githubPrivateKeyPath = string.Empty;
        private bool _isGitHubAppConfigured;
        private bool _isGitHubAppConnected;
        private bool _isTestingGitHubAppConnection;
        private string _githubConnectionStatusText = "Not tested";
        private string _githubConnectionDetailText = "Save and test the GitHub App settings to verify repository access.";
        private string _githubRepositoryPreviewText = string.Empty;
        private bool _hasGitHubRepositoryPreview;
        
        // Email (SMTP)
        private string _smtpHost = string.Empty;
        private int _smtpPort = 587;
        private string _smtpUsername = string.Empty;
        private string _smtpPassword = string.Empty;
        private string _senderEmail = string.Empty;
        private bool _smtpEnableSsl = true;
        private string _emailProvider = "Gmail";
        private bool _isEmailEnabled;
        private bool _showEmailAdvanced;
        
        // SMS (Twilio)
        private string _twilioAccountSid = string.Empty;
        private string _twilioAuthToken = string.Empty;
        private string _twilioPhoneNumber = string.Empty;
        private bool _isSmsEnabled;

        public IntegrationsViewModel()
            : this(new JsonSettingsService())
        {
        }

        public IntegrationsViewModel(
            ISettingsService settingsService,
            IGitHubAppClientFactory? githubAppClientFactory = null)
        {
            Title = "INTEGRATIONS";
            _settingsService = settingsService;
            _githubAppClientFactory = githubAppClientFactory;
            
            // Load saved settings
            _settingsService.Load();
            LoadAllSettings();
            
            // Commands
            SaveTodoistCommand = ReactiveCommand.Create(SaveTodoist);
            ClearTodoistCommand = ReactiveCommand.Create(ClearTodoist);
            
            SaveEmailCommand = ReactiveCommand.Create(SaveEmail);
            ClearEmailCommand = ReactiveCommand.Create(ClearEmail);
            
            SaveSmsCommand = ReactiveCommand.Create(SaveSms);
            ClearSmsCommand = ReactiveCommand.Create(ClearSms);

            SaveGitHubAppCommand = ReactiveCommand.Create(SaveGitHubApp);
            ClearGitHubAppCommand = ReactiveCommand.Create(ClearGitHubApp);
            TestGitHubAppConnectionCommand = ReactiveCommand.CreateFromTask(TestGitHubAppConnectionAsync);
            ListGitHubAppRepositoriesCommand = ReactiveCommand.CreateFromTask(ListGitHubAppRepositoriesAsync);
        }

        #region Todoist Properties

        public string TodoistApiKey
        {
            get => _todoistApiKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _todoistApiKey, value);
                IsTaskAgentEnabled = !string.IsNullOrWhiteSpace(value);
            }
        }

        public bool IsTaskAgentEnabled
        {
            get => _isTaskAgentEnabled;
            private set => this.RaiseAndSetIfChanged(ref _isTaskAgentEnabled, value);
        }

        public string TodoistDescription => "Connect to Todoist for task management capabilities. The Task Agent will be enabled when a valid API key is provided.";
        public string TodoistStatusText => IsTaskAgentEnabled ? "ACTIVE" : "NOT CONFIGURED";

        #endregion

        #region GitHub App Properties

        public string GitHubAppId
        {
            get => _githubAppId;
            set
            {
                this.RaiseAndSetIfChanged(ref _githubAppId, value);
                RefreshGitHubAppConfigured();
            }
        }

        public string GitHubInstallationId
        {
            get => _githubInstallationId;
            set
            {
                this.RaiseAndSetIfChanged(ref _githubInstallationId, value);
                RefreshGitHubAppConfigured();
            }
        }

        public string GitHubPrivateKeyPath
        {
            get => _githubPrivateKeyPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _githubPrivateKeyPath, value);
                RefreshGitHubAppConfigured();
            }
        }

        public bool IsGitHubAppConfigured
        {
            get => _isGitHubAppConfigured;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isGitHubAppConfigured, value);
                this.RaisePropertyChanged(nameof(GitHubAppStatusText));
                this.RaisePropertyChanged(nameof(CanTestGitHubAppConnection));
            }
        }

        public string GitHubAppDescription => "Connect a GitHub App installation so Kam can inspect selected repositories with least-privilege access.";
        public string GitHubAppStatusText => IsGitHubAppConfigured ? "CONFIGURED" : "NOT CONFIGURED";

        public bool IsTestingGitHubAppConnection
        {
            get => _isTestingGitHubAppConnection;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isTestingGitHubAppConnection, value);
                this.RaisePropertyChanged(nameof(CanTestGitHubAppConnection));
                this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
            }
        }

        public bool CanTestGitHubAppConnection => IsGitHubAppConfigured && !IsTestingGitHubAppConnection;

        public bool CanListGitHubAppRepositories => _isGitHubAppConnected && !IsTestingGitHubAppConnection;

        public string GitHubConnectionStatusText
        {
            get => _githubConnectionStatusText;
            private set => this.RaiseAndSetIfChanged(ref _githubConnectionStatusText, value);
        }

        public string GitHubConnectionDetailText
        {
            get => _githubConnectionDetailText;
            private set => this.RaiseAndSetIfChanged(ref _githubConnectionDetailText, value);
        }

        public string GitHubRepositoryPreviewText
        {
            get => _githubRepositoryPreviewText;
            private set => this.RaiseAndSetIfChanged(ref _githubRepositoryPreviewText, value);
        }

        public bool HasGitHubRepositoryPreview
        {
            get => _hasGitHubRepositoryPreview;
            private set => this.RaiseAndSetIfChanged(ref _hasGitHubRepositoryPreview, value);
        }

        #endregion

        #region Email (SMTP) Properties

        public string EmailProvider
        {
            get => _emailProvider;
            set
            {
                this.RaiseAndSetIfChanged(ref _emailProvider, value);
                UpdateEmailDefaults(value);
            }
        }

        public string SmtpHost
        {
            get => _smtpHost;
            set => this.RaiseAndSetIfChanged(ref _smtpHost, value);
        }

        public int SmtpPort
        {
            get => _smtpPort;
            set => this.RaiseAndSetIfChanged(ref _smtpPort, value);
        }

        public string SmtpUsername
        {
            get => _smtpUsername;
            set => this.RaiseAndSetIfChanged(ref _smtpUsername, value);
        }

        public string SmtpPassword
        {
            get => _smtpPassword;
            set => this.RaiseAndSetIfChanged(ref _smtpPassword, value);
        }

        public string SenderEmail
        {
            get => _senderEmail;
            set => this.RaiseAndSetIfChanged(ref _senderEmail, value);
        }

        public bool SmtpEnableSsl
        {
            get => _smtpEnableSsl;
            set => this.RaiseAndSetIfChanged(ref _smtpEnableSsl, value);
        }

        public bool IsEmailEnabled
        {
            get => _isEmailEnabled;
            private set => this.RaiseAndSetIfChanged(ref _isEmailEnabled, value);
        }

        public bool ShowEmailAdvanced
        {
            get => _showEmailAdvanced;
            set => this.RaiseAndSetIfChanged(ref _showEmailAdvanced, value);
        }

        public string EmailDescription => "Configure SMTP settings to enable email sending capabilities. The Communication Agent will be able to send emails on your behalf.";
        public string EmailStatusText => IsEmailEnabled ? "CONFIGURED" : "NOT CONFIGURED";

        public string[] EmailProviders => new[] { "Gmail", "Outlook", "Yahoo", "Custom" };

        #endregion

        #region SMS (Twilio) Properties

        public string TwilioAccountSid
        {
            get => _twilioAccountSid;
            set => this.RaiseAndSetIfChanged(ref _twilioAccountSid, value);
        }

        public string TwilioAuthToken
        {
            get => _twilioAuthToken;
            set => this.RaiseAndSetIfChanged(ref _twilioAuthToken, value);
        }

        public string TwilioPhoneNumber
        {
            get => _twilioPhoneNumber;
            set => this.RaiseAndSetIfChanged(ref _twilioPhoneNumber, value);
        }

        public bool IsSmsEnabled
        {
            get => _isSmsEnabled;
            private set => this.RaiseAndSetIfChanged(ref _isSmsEnabled, value);
        }

        public string SmsDescription => "Connect to Twilio for SMS messaging capabilities. The Communication Agent will be able to send text messages.";
        public string SmsStatusText => IsSmsEnabled ? "CONFIGURED" : "NOT CONFIGURED";

        #endregion

        #region Commands

        // Todoist Commands
        public ICommand SaveTodoistCommand { get; }
        public ICommand ClearTodoistCommand { get; }

        // Email Commands
        public ICommand SaveEmailCommand { get; }
        public ICommand ClearEmailCommand { get; }

        // SMS Commands
        public ICommand SaveSmsCommand { get; }
        public ICommand ClearSmsCommand { get; }

        // GitHub App Commands
        public ICommand SaveGitHubAppCommand { get; }
        public ICommand ClearGitHubAppCommand { get; }
        public ReactiveCommand<Unit, Unit> TestGitHubAppConnectionCommand { get; }
        public ReactiveCommand<Unit, Unit> ListGitHubAppRepositoriesCommand { get; }

        #endregion

        #region Methods

        private void LoadAllSettings()
        {
            // Todoist
            TodoistApiKey = _settingsService.TodoistApiKey;

            // GitHub App
            GitHubAppId = _settingsService.GitHubAppId;
            GitHubInstallationId = _settingsService.GitHubAppInstallationId;
            GitHubPrivateKeyPath = _settingsService.GitHubAppPrivateKeyPath;
            RefreshGitHubAppConfigured();
            
            // Email
            EmailProvider = _settingsService.EmailProvider;
            SmtpHost = _settingsService.SmtpHost;
            SmtpPort = _settingsService.SmtpPort;
            SmtpUsername = _settingsService.SmtpUsername;
            SmtpPassword = _settingsService.SmtpPassword;
            SenderEmail = _settingsService.SenderEmail;
            SmtpEnableSsl = _settingsService.SmtpEnableSsl;
            IsEmailEnabled = !string.IsNullOrWhiteSpace(SmtpHost) && 
                            !string.IsNullOrWhiteSpace(SmtpUsername) &&
                            !string.IsNullOrWhiteSpace(SmtpPassword);
            
            // SMS
            TwilioAccountSid = _settingsService.TwilioAccountSid;
            TwilioAuthToken = _settingsService.TwilioAuthToken;
            TwilioPhoneNumber = _settingsService.TwilioPhoneNumber;
            IsSmsEnabled = !string.IsNullOrWhiteSpace(TwilioAccountSid) &&
                          !string.IsNullOrWhiteSpace(TwilioAuthToken) &&
                          !string.IsNullOrWhiteSpace(TwilioPhoneNumber);
        }

        private void UpdateEmailDefaults(string provider)
        {
            switch (provider)
            {
                case "Gmail":
                    SmtpHost = "smtp.gmail.com";
                    SmtpPort = 587;
                    SmtpEnableSsl = true;
                    break;
                case "Outlook":
                    SmtpHost = "smtp.office365.com";
                    SmtpPort = 587;
                    SmtpEnableSsl = true;
                    break;
                case "Yahoo":
                    SmtpHost = "smtp.mail.yahoo.com";
                    SmtpPort = 587;
                    SmtpEnableSsl = true;
                    break;
                case "Custom":
                    // Keep existing values or clear
                    if (SmtpHost == "smtp.gmail.com" || 
                        SmtpHost == "smtp.office365.com" || 
                        SmtpHost == "smtp.mail.yahoo.com")
                    {
                        SmtpHost = string.Empty;
                        SmtpPort = 587;
                    }
                    break;
            }
        }

        private void RefreshGitHubAppConfigured()
        {
            IsGitHubAppConfigured = !string.IsNullOrWhiteSpace(GitHubAppId) &&
                                    !string.IsNullOrWhiteSpace(GitHubInstallationId) &&
                                    !string.IsNullOrWhiteSpace(GitHubPrivateKeyPath);
            if (!IsGitHubAppConfigured)
            {
                SetGitHubAppDisconnected(
                    "Missing settings",
                    $"Missing settings: {string.Join(", ", GetMissingGitHubAppFieldLabels())}.");
                return;
            }

            if (!_isGitHubAppConnected && GitHubConnectionStatusText == "Missing settings")
            {
                GitHubConnectionStatusText = "Not tested";
                GitHubConnectionDetailText = "Settings are present. Test connection to verify repository access.";
            }
        }

        #region Todoist Methods

        private void SaveTodoist()
        {
            _settingsService.TodoistApiKey = TodoistApiKey;
            _settingsService.Save();
        }

        private void ClearTodoist()
        {
            TodoistApiKey = string.Empty;
            _settingsService.TodoistApiKey = string.Empty;
            _settingsService.Save();
        }

        #endregion

        #region GitHub App Methods

        private void SaveGitHubApp()
        {
            _settingsService.GitHubAppId = GitHubAppId;
            _settingsService.GitHubAppInstallationId = GitHubInstallationId;
            _settingsService.GitHubAppPrivateKeyPath = GitHubPrivateKeyPath;
            _settingsService.Save();
            RefreshGitHubAppConfigured();
            if (IsGitHubAppConfigured && !_isGitHubAppConnected)
            {
                GitHubConnectionStatusText = "Not tested";
                GitHubConnectionDetailText = "Settings saved. Test connection to verify repository access.";
            }
        }

        private void ClearGitHubApp()
        {
            GitHubAppId = string.Empty;
            GitHubInstallationId = string.Empty;
            GitHubPrivateKeyPath = string.Empty;

            _settingsService.GitHubAppId = string.Empty;
            _settingsService.GitHubAppInstallationId = string.Empty;
            _settingsService.GitHubAppPrivateKeyPath = string.Empty;
            _settingsService.Save();
            IsGitHubAppConfigured = false;
            SetGitHubAppDisconnected("Not configured", "GitHub App settings were cleared.");
        }

        public async Task TestGitHubAppConnectionAsync(CancellationToken cancellationToken = default)
        {
            RefreshGitHubAppConfigured();
            if (!IsGitHubAppConfigured)
            {
                SetGitHubAppDisconnected(
                    "Missing settings",
                    $"Missing settings: {string.Join(", ", GetMissingGitHubAppFieldLabels())}.");
                return;
            }

            if (_githubAppClientFactory is null)
            {
                SetGitHubAppDisconnected(
                    "Unavailable",
                    "GitHub App client factory is not available in this runtime.");
                return;
            }

            SaveGitHubApp();
            IsTestingGitHubAppConnection = true;
            GitHubConnectionStatusText = "Testing...";
            GitHubConnectionDetailText = "Checking GitHub App credentials and repository access.";
            GitHubRepositoryPreviewText = string.Empty;
            HasGitHubRepositoryPreview = false;

            try
            {
                var client = CreateGitHubAppClientFromCurrentSettings();
                var status = await client.GetStatusAsync(cancellationToken);
                if (!status.IsConnected)
                {
                    SetGitHubAppDisconnected(
                        status.IsConfigured ? "Needs action" : "Missing settings",
                        BuildGitHubAppStatusDetail(status));
                    return;
                }

                _isGitHubAppConnected = true;
                this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
                GitHubConnectionStatusText = "Connected";
                GitHubConnectionDetailText = BuildGitHubAppStatusDetail(status);

                var repositories = await client.ListRepositoriesAsync(cancellationToken);
                ApplyGitHubRepositoryListResult(status, repositories);
            }
            catch (Exception ex)
            {
                SetGitHubAppDisconnected(
                    "Unavailable",
                    SecretRedactor.Redact($"GitHub App connection test failed: {ex.Message}"));
            }
            finally
            {
                IsTestingGitHubAppConnection = false;
            }
        }

        public async Task ListGitHubAppRepositoriesAsync(CancellationToken cancellationToken = default)
        {
            if (!CanListGitHubAppRepositories)
            {
                GitHubConnectionStatusText = "Not tested";
                GitHubConnectionDetailText = "Test connection before listing repositories.";
                GitHubRepositoryPreviewText = string.Empty;
                HasGitHubRepositoryPreview = false;
                return;
            }

            if (_githubAppClientFactory is null)
            {
                SetGitHubAppDisconnected(
                    "Unavailable",
                    "GitHub App client factory is not available in this runtime.");
                return;
            }

            IsTestingGitHubAppConnection = true;
            try
            {
                var status = GitHubAppConnectionStatus.Connected(
                    GitHubAppId,
                    GitHubInstallationId,
                    "https://api.github.com",
                    null,
                    null,
                    null);
                var repositories = await CreateGitHubAppClientFromCurrentSettings()
                    .ListRepositoriesAsync(cancellationToken);
                ApplyGitHubRepositoryListResult(status, repositories);
            }
            catch (Exception ex)
            {
                SetGitHubAppDisconnected(
                    "Unavailable",
                    SecretRedactor.Redact($"GitHub App repository list failed: {ex.Message}"));
            }
            finally
            {
                IsTestingGitHubAppConnection = false;
            }
        }

        private IGitHubAppClient CreateGitHubAppClientFromCurrentSettings()
        {
            return _githubAppClientFactory!.Create(new GitHubAppOptions
            {
                AppId = GitHubAppId,
                InstallationId = GitHubInstallationId,
                PrivateKeyPath = GitHubPrivateKeyPath
            });
        }

        private void ApplyGitHubRepositoryListResult(
            GitHubAppConnectionStatus status,
            GitHubRepositoryListResult repositories)
        {
            if (!repositories.Success)
            {
                SetGitHubAppDisconnected(
                    "Needs action",
                    SecretRedactor.Redact($"GitHub App connected, but repository list failed: {repositories.Message}"));
                return;
            }

            _isGitHubAppConnected = true;
            this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
            var repositoryCount = status.RepositoryCount ?? repositories.Repositories.Count;
            GitHubConnectionStatusText = "Connected";
            GitHubConnectionDetailText = $"GitHub App connected. {repositoryCount} repositories visible.";
            GitHubRepositoryPreviewText = FormatGitHubRepositoryPreview(repositories.Repositories, repositoryCount);
            HasGitHubRepositoryPreview = !string.IsNullOrWhiteSpace(GitHubRepositoryPreviewText);
        }

        private void SetGitHubAppDisconnected(string status, string detail)
        {
            _isGitHubAppConnected = false;
            this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
            GitHubConnectionStatusText = status;
            GitHubConnectionDetailText = SecretRedactor.Redact(detail);
            GitHubRepositoryPreviewText = string.Empty;
            HasGitHubRepositoryPreview = false;
        }

        private string BuildGitHubAppStatusDetail(GitHubAppConnectionStatus status)
        {
            var detail = status.Message;
            if (status.MissingSettings is { Count: > 0 })
            {
                detail += $" Missing: {string.Join(", ", status.MissingSettings)}.";
            }

            if (status.IsConnected && status.RepositoryCount is not null)
            {
                detail += $" {status.RepositoryCount} repositories visible.";
            }

            return SecretRedactor.Redact(detail);
        }

        private IReadOnlyList<string> GetMissingGitHubAppFieldLabels()
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(GitHubAppId))
            {
                missing.Add("App ID");
            }

            if (string.IsNullOrWhiteSpace(GitHubInstallationId))
            {
                missing.Add("Installation ID");
            }

            if (string.IsNullOrWhiteSpace(GitHubPrivateKeyPath))
            {
                missing.Add("Private Key Path");
            }

            return missing;
        }

        private static string FormatGitHubRepositoryPreview(
            IReadOnlyList<GitHubRepositorySummary> repositories,
            int expectedRepositoryCount)
        {
            if (repositories.Count == 0)
            {
                return string.Empty;
            }

            const int maxVisibleRepositories = 5;
            var visible = repositories
                .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
                .Take(maxVisibleRepositories)
                .Select(repository =>
                    $"{repository.FullName} ({(repository.IsPrivate ? "private" : "public")}, {repository.DefaultBranch})")
                .ToArray();
            var preview = string.Join(Environment.NewLine, visible);
            var hiddenCount = Math.Max(expectedRepositoryCount, repositories.Count) - visible.Length;
            if (hiddenCount > 0)
            {
                preview += $"{Environment.NewLine}+ {hiddenCount} more";
            }

            return preview;
        }

        #endregion

        #region Email Methods

        private void SaveEmail()
        {
            _settingsService.EmailProvider = EmailProvider;
            _settingsService.SmtpHost = SmtpHost;
            _settingsService.SmtpPort = SmtpPort;
            _settingsService.SmtpUsername = SmtpUsername;
            _settingsService.SmtpPassword = SmtpPassword;
            _settingsService.SenderEmail = SenderEmail;
            _settingsService.SmtpEnableSsl = SmtpEnableSsl;
            _settingsService.Save();
            
            IsEmailEnabled = !string.IsNullOrWhiteSpace(SmtpHost) && 
                            !string.IsNullOrWhiteSpace(SmtpUsername) &&
                            !string.IsNullOrWhiteSpace(SmtpPassword);
        }

        private void ClearEmail()
        {
            EmailProvider = "Gmail";
            SmtpHost = string.Empty;
            SmtpPort = 587;
            SmtpUsername = string.Empty;
            SmtpPassword = string.Empty;
            SenderEmail = string.Empty;
            SmtpEnableSsl = true;
            
            _settingsService.EmailProvider = "Gmail";
            _settingsService.SmtpHost = string.Empty;
            _settingsService.SmtpPort = 587;
            _settingsService.SmtpUsername = string.Empty;
            _settingsService.SmtpPassword = string.Empty;
            _settingsService.SenderEmail = string.Empty;
            _settingsService.SmtpEnableSsl = true;
            _settingsService.Save();
            
            IsEmailEnabled = false;
        }

        #endregion

        #region SMS Methods

        private void SaveSms()
        {
            _settingsService.TwilioAccountSid = TwilioAccountSid;
            _settingsService.TwilioAuthToken = TwilioAuthToken;
            _settingsService.TwilioPhoneNumber = TwilioPhoneNumber;
            _settingsService.SmsEnabled = true;
            _settingsService.Save();
            
            IsSmsEnabled = !string.IsNullOrWhiteSpace(TwilioAccountSid) &&
                          !string.IsNullOrWhiteSpace(TwilioAuthToken) &&
                          !string.IsNullOrWhiteSpace(TwilioPhoneNumber);
        }

        private void ClearSms()
        {
            TwilioAccountSid = string.Empty;
            TwilioAuthToken = string.Empty;
            TwilioPhoneNumber = string.Empty;
            
            _settingsService.TwilioAccountSid = string.Empty;
            _settingsService.TwilioAuthToken = string.Empty;
            _settingsService.TwilioPhoneNumber = string.Empty;
            _settingsService.SmsEnabled = false;
            _settingsService.Save();
            
            IsSmsEnabled = false;
        }

        #endregion

        public override void OnNavigatedTo()
        {
            // Reload settings when navigating to this view
            _settingsService.Load();
            LoadAllSettings();
        }

        public override void OnNavigatedFrom()
        {
            // Auto-save when leaving the view
            SaveTodoist();
            SaveGitHubApp();
            SaveEmail();
            SaveSms();
        }

        #endregion
    }
}

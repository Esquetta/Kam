using ReactiveUI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.GitHub;
using SmartVoiceAgent.Core.Security;
using SmartVoiceAgent.Ui.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
    public class IntegrationsViewModel : ViewModelBase, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IGitHubAppClientFactory? _githubAppClientFactory;
        private readonly IGitHubDesktopConnector _githubDesktopConnector;
        
        // Todoist
        private string _todoistApiKey = string.Empty;
        private bool _isTaskAgentEnabled;

        // Web search (Google Programmable Search)
        private string _webSearchApiKey = string.Empty;
        private string _webSearchEngineId = string.Empty;
        private bool _isWebSearchConfigured;

        // GitHub App
        private string _githubAppId = string.Empty;
        private string _githubInstallationId = string.Empty;
        private string _githubPrivateKeyPath = string.Empty;
        private bool _isGitHubAppConfigured;
        private bool _isGitHubAppConnected;
        private bool _isGitHubDesktopConnected;
        private bool _isTestingGitHubAppConnection;
        private bool _isConnectingGitHub;
        private bool _showGitHubAppAdvancedSettings;
        private GitHubConnectionState _githubConnectionState = GitHubConnectionState.NotConnected;
        private Func<string> _githubConnectionDetail = static () => Loc.Get("Integrations.GitHub.Detail.Idle");
        private Func<string>? _githubRepositoryPreview;
        private string _githubConnectionStatusText = Loc.Get("Common.NotConnected");
        private string _githubConnectionDetailText = Loc.Get("Integrations.GitHub.Detail.Idle");
        private string _githubRepositoryPreviewText = string.Empty;
        private bool _hasGitHubRepositoryPreview;
        private bool _isLoadingSettings;
        
        // Email (SMTP)
        private string _smtpHost = string.Empty;
        private int _smtpPort = 587;
        private string _smtpUsername = string.Empty;
        private string _smtpPassword = string.Empty;
        private string _senderEmail = string.Empty;
        private bool _smtpEnableSsl = true;
        private string _emailProvider = "Gmail";
        private IReadOnlyList<EmailProviderOption> _emailProviderOptions = CreateEmailProviderOptions();
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
            IGitHubAppClientFactory? githubAppClientFactory = null,
            IGitHubDesktopConnector? githubDesktopConnector = null)
        {
            Title = Loc.Get("Integrations.Title");
            _settingsService = settingsService;
            _githubAppClientFactory = githubAppClientFactory;
            _githubDesktopConnector = githubDesktopConnector ?? new GitHubCliDesktopConnector();
            
            // Load saved settings
            _settingsService.Load();
            LoadAllSettings();
            
            // Commands
            SaveTodoistCommand = ReactiveCommand.Create(SaveTodoist);
            ClearTodoistCommand = ReactiveCommand.Create(ClearTodoist);

            SaveWebSearchCommand = ReactiveCommand.Create(SaveWebSearch);
            ClearWebSearchCommand = ReactiveCommand.Create(ClearWebSearch);
            
            SaveEmailCommand = ReactiveCommand.Create(SaveEmail);
            ClearEmailCommand = ReactiveCommand.Create(ClearEmail);
            
            SaveSmsCommand = ReactiveCommand.Create(SaveSms);
            ClearSmsCommand = ReactiveCommand.Create(ClearSms);

            SaveGitHubAppCommand = ReactiveCommand.Create(SaveGitHubApp);
            ClearGitHubAppCommand = ReactiveCommand.Create(ClearGitHubApp);
            ConnectGitHubCommand = ReactiveCommand.CreateFromTask(ConnectGitHubAsync);
            ToggleGitHubAppAdvancedSettingsCommand = ReactiveCommand.Create(ToggleGitHubAppAdvancedSettings);
            TestGitHubAppConnectionCommand = ReactiveCommand.CreateFromTask(TestGitHubAppConnectionAsync);
            ListGitHubAppRepositoriesCommand = ReactiveCommand.CreateFromTask(ListGitHubAppRepositoriesAsync);

            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        }

        /// <summary>
        /// The GitHub connection states shown above the connection detail.
        /// </summary>
        private enum GitHubConnectionState
        {
            NotConnected,
            Connecting,
            Connected,
            Testing,
            NotTested,
            MissingSettings,
            RetestRequired,
            InvalidPrivateKeyPath,
            SignInRequired,
            NeedsAction,
            Unavailable,
            NotConfigured
        }

        /// <summary>
        /// Rebuilds the text this view model writes in code when the interface language changes.
        /// </summary>
        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            Title = Loc.Get("Integrations.Title");
            this.RaisePropertyChanged(nameof(TodoistDescription));
            this.RaisePropertyChanged(nameof(TodoistStatusText));
            this.RaisePropertyChanged(nameof(WebSearchDescription));
            this.RaisePropertyChanged(nameof(WebSearchStatusText));
            this.RaisePropertyChanged(nameof(GitHubAppDescription));
            this.RaisePropertyChanged(nameof(GitHubAppStatusText));
            this.RaisePropertyChanged(nameof(EmailDescription));
            this.RaisePropertyChanged(nameof(EmailStatusText));
            this.RaisePropertyChanged(nameof(SmsDescription));
            this.RaisePropertyChanged(nameof(SmsStatusText));
            EmailProviderOptions = CreateEmailProviderOptions();
            this.RaisePropertyChanged(nameof(SelectedEmailProviderOption));
            RefreshGitHubConnectionText();
            RefreshGitHubAppSetupSteps();
        }

        /// <summary>
        /// Stops following language changes.
        /// </summary>
        public void Dispose()
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            GC.SuppressFinalize(this);
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
            private set
            {
                this.RaiseAndSetIfChanged(ref _isTaskAgentEnabled, value);
                this.RaisePropertyChanged(nameof(TodoistStatusText));
            }
        }

        public string TodoistDescription => Loc.Get("Integrations.Todoist.Description");
        public string TodoistStatusText => IsTaskAgentEnabled
            ? Loc.Get("Integrations.Status.Active")
            : Loc.Get("Integrations.Status.NotConfigured");

        #endregion

        #region Web Search Properties

        /// <summary>Gets or sets the Google Custom Search JSON API key being edited.</summary>
        public string WebSearchApiKey
        {
            get => _webSearchApiKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _webSearchApiKey, value);
                this.RaisePropertyChanged(nameof(CanSaveWebSearch));
            }
        }

        /// <summary>Gets or sets the Programmable Search engine ID being edited.</summary>
        public string WebSearchEngineId
        {
            get => _webSearchEngineId;
            set
            {
                this.RaiseAndSetIfChanged(ref _webSearchEngineId, value);
                this.RaisePropertyChanged(nameof(CanSaveWebSearch));
            }
        }

        /// <summary>Gets whether a saved key and engine ID let the agent search the web.</summary>
        public bool IsWebSearchConfigured
        {
            get => _isWebSearchConfigured;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isWebSearchConfigured, value);
                this.RaisePropertyChanged(nameof(WebSearchStatusText));
            }
        }

        /// <summary>Gets whether both fields are filled in.</summary>
        public bool CanSaveWebSearch =>
            !string.IsNullOrWhiteSpace(WebSearchApiKey) && !string.IsNullOrWhiteSpace(WebSearchEngineId);

        public string WebSearchDescription => Loc.Get("Integrations.WebSearch.Description");

        public string WebSearchStatusText => IsWebSearchConfigured
            ? Loc.Get("Integrations.Status.Active")
            : Loc.Get("Integrations.Status.NotConfigured");

        #endregion

        #region GitHub App Properties

        public string GitHubAppId
        {
            get => _githubAppId;
            set
            {
                if (_githubAppId == value)
                {
                    return;
                }

                var requiresRetest = _isGitHubAppConnected && !_isLoadingSettings;
                this.RaiseAndSetIfChanged(ref _githubAppId, value);
                RefreshGitHubAppConfigured(requiresRetest);
            }
        }

        public string GitHubInstallationId
        {
            get => _githubInstallationId;
            set
            {
                if (_githubInstallationId == value)
                {
                    return;
                }

                var requiresRetest = _isGitHubAppConnected && !_isLoadingSettings;
                this.RaiseAndSetIfChanged(ref _githubInstallationId, value);
                RefreshGitHubAppConfigured(requiresRetest);
            }
        }

        public string GitHubPrivateKeyPath
        {
            get => _githubPrivateKeyPath;
            set
            {
                if (_githubPrivateKeyPath == value)
                {
                    return;
                }

                var requiresRetest = _isGitHubAppConnected && !_isLoadingSettings;
                this.RaiseAndSetIfChanged(ref _githubPrivateKeyPath, value);
                RefreshGitHubAppConfigured(requiresRetest);
            }
        }

        public bool IsGitHubAppConfigured
        {
            get => _isGitHubAppConfigured;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isGitHubAppConfigured, value);
                this.RaisePropertyChanged(nameof(GitHubAppStatusText));
                this.RaisePropertyChanged(nameof(IsGitHubStatusPositive));
                this.RaisePropertyChanged(nameof(CanTestGitHubAppConnection));
            }
        }

        public string GitHubAppDescription => Loc.Get("Integrations.GitHub.Description");
        public string GitHubAppStatusText => _isGitHubDesktopConnected || _isGitHubAppConnected
            ? Loc.Get("Integrations.Status.Connected")
            : IsGitHubAppConfigured
                ? Loc.Get("Integrations.Status.Configured")
                : Loc.Get("Integrations.Status.NotConnected");

        public bool IsGitHubStatusPositive => _isGitHubDesktopConnected || _isGitHubAppConnected || IsGitHubAppConfigured;

        public bool IsTestingGitHubAppConnection
        {
            get => _isTestingGitHubAppConnection;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isTestingGitHubAppConnection, value);
                this.RaisePropertyChanged(nameof(CanTestGitHubAppConnection));
                this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
                this.RaisePropertyChanged(nameof(CanConnectGitHub));
                RefreshGitHubAppSetupSteps();
            }
        }

        public bool CanTestGitHubAppConnection => IsGitHubAppConfigured && !IsTestingGitHubAppConnection;

        public bool CanListGitHubAppRepositories => (_isGitHubAppConnected || _isGitHubDesktopConnected) && !IsTestingGitHubAppConnection;

        public bool IsConnectingGitHub
        {
            get => _isConnectingGitHub;
            private set
            {
                this.RaiseAndSetIfChanged(ref _isConnectingGitHub, value);
                this.RaisePropertyChanged(nameof(CanConnectGitHub));
            }
        }

        public bool CanConnectGitHub => !IsConnectingGitHub && !IsTestingGitHubAppConnection;

        public bool ShowGitHubAppAdvancedSettings
        {
            get => _showGitHubAppAdvancedSettings;
            private set => this.RaiseAndSetIfChanged(ref _showGitHubAppAdvancedSettings, value);
        }

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

        public ObservableCollection<RuntimeDiagnosticItemViewModel> GitHubAppSetupSteps { get; } = [];

        public bool HasGitHubAppSetupSteps => GitHubAppSetupSteps.Count > 0;

        #endregion

        #region Email (SMTP) Properties

        public string EmailProvider
        {
            get => _emailProvider;
            set
            {
                this.RaiseAndSetIfChanged(ref _emailProvider, value);
                this.RaisePropertyChanged(nameof(SelectedEmailProviderOption));
                UpdateEmailDefaults(value);
            }
        }

        /// <summary>
        /// Gets the email providers to choose from, named in the current language.
        /// </summary>
        public IReadOnlyList<EmailProviderOption> EmailProviderOptions
        {
            get => _emailProviderOptions;
            private set => this.RaiseAndSetIfChanged(ref _emailProviderOptions, value);
        }

        /// <summary>
        /// Gets or sets the chosen email provider; choosing one sets <see cref="EmailProvider"/>.
        /// </summary>
        public EmailProviderOption? SelectedEmailProviderOption
        {
            get => EmailProviderOptions.FirstOrDefault(option => option.Id == EmailProvider);
            set
            {
                // The list is rebuilt when the language changes and the picker clears its selection meanwhile.
                if (value is not null && value.Id != EmailProvider)
                {
                    EmailProvider = value.Id;
                }
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
            private set
            {
                this.RaiseAndSetIfChanged(ref _isEmailEnabled, value);
                this.RaisePropertyChanged(nameof(EmailStatusText));
            }
        }

        public bool ShowEmailAdvanced
        {
            get => _showEmailAdvanced;
            set => this.RaiseAndSetIfChanged(ref _showEmailAdvanced, value);
        }

        public string EmailDescription => Loc.Get("Integrations.Email.Description");
        public string EmailStatusText => IsEmailEnabled
            ? Loc.Get("Integrations.Status.Configured")
            : Loc.Get("Integrations.Status.NotConfigured");

        public string[] EmailProviders => new[] { "Gmail", "Outlook", "Yahoo", "Custom" };

        private static IReadOnlyList<EmailProviderOption> CreateEmailProviderOptions()
        {
            return
            [
                new EmailProviderOption("Gmail", "Gmail"),
                new EmailProviderOption("Outlook", "Outlook"),
                new EmailProviderOption("Yahoo", "Yahoo"),
                new EmailProviderOption("Custom", Loc.Get("Integrations.Email.CustomProvider"))
            ];
        }

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
            private set
            {
                this.RaiseAndSetIfChanged(ref _isSmsEnabled, value);
                this.RaisePropertyChanged(nameof(SmsStatusText));
            }
        }

        public string SmsDescription => Loc.Get("Integrations.Sms.Description");
        public string SmsStatusText => IsSmsEnabled
            ? Loc.Get("Integrations.Status.Configured")
            : Loc.Get("Integrations.Status.NotConfigured");

        #endregion

        #region Commands

        // Todoist Commands
        public ICommand SaveTodoistCommand { get; }
        public ICommand ClearTodoistCommand { get; }

        // Web Search Commands
        public ICommand SaveWebSearchCommand { get; }
        public ICommand ClearWebSearchCommand { get; }

        // Email Commands
        public ICommand SaveEmailCommand { get; }
        public ICommand ClearEmailCommand { get; }

        // SMS Commands
        public ICommand SaveSmsCommand { get; }
        public ICommand ClearSmsCommand { get; }

        // GitHub App Commands
        public ICommand SaveGitHubAppCommand { get; }
        public ICommand ClearGitHubAppCommand { get; }
        public ReactiveCommand<Unit, Unit> ConnectGitHubCommand { get; }
        public ICommand ToggleGitHubAppAdvancedSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> TestGitHubAppConnectionCommand { get; }
        public ReactiveCommand<Unit, Unit> ListGitHubAppRepositoriesCommand { get; }

        #endregion

        #region Methods

        private void LoadAllSettings()
        {
            // Todoist
            TodoistApiKey = _settingsService.TodoistApiKey;

            // Web search
            WebSearchApiKey = _settingsService.WebSearchApiKey;
            WebSearchEngineId = _settingsService.WebSearchEngineId;
            IsWebSearchConfigured = CanSaveWebSearch;

            // GitHub App
            _isLoadingSettings = true;
            try
            {
                GitHubAppId = _settingsService.GitHubAppId;
                GitHubInstallationId = _settingsService.GitHubAppInstallationId;
                GitHubPrivateKeyPath = _settingsService.GitHubAppPrivateKeyPath;
            }
            finally
            {
                _isLoadingSettings = false;
            }

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

        private void RefreshGitHubAppConfigured(bool requiresRetest = false)
        {
            IsGitHubAppConfigured = !string.IsNullOrWhiteSpace(GitHubAppId) &&
                                    !string.IsNullOrWhiteSpace(GitHubInstallationId) &&
                                    !string.IsNullOrWhiteSpace(GitHubPrivateKeyPath) &&
                                    !ContainsRawPrivateKeyMaterial(GitHubPrivateKeyPath);
            if (_isGitHubDesktopConnected)
            {
                RefreshGitHubAppSetupSteps();
                return;
            }

            if (ContainsRawPrivateKeyMaterial(GitHubPrivateKeyPath))
            {
                SetGitHubAppDisconnected(
                    GitHubConnectionState.InvalidPrivateKeyPath,
                    static () => Loc.Get("Integrations.GitHub.Detail.PemPathOnly"));
                return;
            }

            if (!IsGitHubAppConfigured)
            {
                if (ShowGitHubAppAdvancedSettings)
                {
                    SetGitHubAppDisconnected(GitHubConnectionState.MissingSettings, DescribeMissingGitHubAppSettings());
                }
                else
                {
                    SetGitHubConnectionIdle();
                }

                return;
            }

            if (requiresRetest)
            {
                SetGitHubAppDisconnected(
                    GitHubConnectionState.RetestRequired,
                    static () => Loc.Get("Integrations.GitHub.Detail.SettingsChanged"));
                return;
            }

            if (!_isGitHubAppConnected && _githubConnectionState == GitHubConnectionState.MissingSettings)
            {
                SetGitHubConnectionText(
                    GitHubConnectionState.NotTested,
                    static () => Loc.Get("Integrations.GitHub.Detail.SettingsPresent"));
            }

            RefreshGitHubAppSetupSteps();
        }

        private void SetGitHubConnectionIdle()
        {
            _isGitHubAppConnected = false;
            _isGitHubDesktopConnected = false;
            this.RaisePropertyChanged(nameof(GitHubAppStatusText));
            this.RaisePropertyChanged(nameof(IsGitHubStatusPositive));
            this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
            SetGitHubConnectionText(
                GitHubConnectionState.NotConnected,
                static () => Loc.Get("Integrations.GitHub.Detail.Idle"));
            SetGitHubRepositoryPreview(null);
            RefreshGitHubAppSetupSteps();
        }

        /// <summary>
        /// Shows a GitHub connection state and detail, and keeps how the detail was built so a language change can
        /// show it again.
        /// </summary>
        /// <param name="state">The connection state.</param>
        /// <param name="detail">Builds the detail text in the current language.</param>
        private void SetGitHubConnectionText(GitHubConnectionState state, Func<string> detail)
        {
            _githubConnectionState = state;
            _githubConnectionDetail = detail;
            GitHubConnectionStatusText = GetGitHubConnectionStateText(state);
            GitHubConnectionDetailText = SanitizeGitHubAppDetail(detail());
        }

        /// <summary>
        /// Shows the repository preview, or hides it when <paramref name="preview"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="preview">Builds the preview text in the current language.</param>
        private void SetGitHubRepositoryPreview(Func<string>? preview)
        {
            _githubRepositoryPreview = preview;
            GitHubRepositoryPreviewText = preview?.Invoke() ?? string.Empty;
            HasGitHubRepositoryPreview = !string.IsNullOrWhiteSpace(GitHubRepositoryPreviewText);
        }

        private void RefreshGitHubConnectionText()
        {
            GitHubConnectionStatusText = GetGitHubConnectionStateText(_githubConnectionState);
            GitHubConnectionDetailText = SanitizeGitHubAppDetail(_githubConnectionDetail());
            GitHubRepositoryPreviewText = _githubRepositoryPreview?.Invoke() ?? string.Empty;
        }

        private static string GetGitHubConnectionStateText(GitHubConnectionState state)
        {
            return state switch
            {
                GitHubConnectionState.Connecting => Loc.Get("Integrations.GitHub.State.Connecting"),
                GitHubConnectionState.Connected => Loc.Get("Common.Connected"),
                GitHubConnectionState.Testing => Loc.Get("Integrations.GitHub.State.Testing"),
                GitHubConnectionState.NotTested => Loc.Get("Integrations.GitHub.State.NotTested"),
                GitHubConnectionState.MissingSettings => Loc.Get("Integrations.GitHub.State.MissingSettings"),
                GitHubConnectionState.RetestRequired => Loc.Get("Integrations.GitHub.State.RetestRequired"),
                GitHubConnectionState.InvalidPrivateKeyPath => Loc.Get("Integrations.GitHub.State.InvalidKeyPath"),
                GitHubConnectionState.SignInRequired => Loc.Get("Integrations.GitHub.State.SignInRequired"),
                GitHubConnectionState.NeedsAction => Loc.Get("Integrations.GitHub.State.NeedsAction"),
                GitHubConnectionState.Unavailable => Loc.Get("Integrations.GitHub.State.Unavailable"),
                GitHubConnectionState.NotConfigured => Loc.Get("Integrations.GitHub.State.NotConfigured"),
                _ => Loc.Get("Common.NotConnected")
            };
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

        #region Web Search Methods

        private void SaveWebSearch()
        {
            _settingsService.WebSearchApiKey = WebSearchApiKey.Trim();
            _settingsService.WebSearchEngineId = WebSearchEngineId.Trim();
            _settingsService.Save();
            IsWebSearchConfigured = CanSaveWebSearch;
        }

        private void ClearWebSearch()
        {
            WebSearchApiKey = string.Empty;
            WebSearchEngineId = string.Empty;
            SaveWebSearch();
        }

        #endregion

        #region GitHub App Methods

        private void ToggleGitHubAppAdvancedSettings()
        {
            ShowGitHubAppAdvancedSettings = !ShowGitHubAppAdvancedSettings;
            RefreshGitHubAppConfigured();
        }

        public async Task ConnectGitHubAsync(CancellationToken cancellationToken = default)
        {
            IsConnectingGitHub = true;
            SetGitHubConnectionText(
                GitHubConnectionState.Connecting,
                static () => Loc.Get("Integrations.GitHub.Detail.CheckingSignIn"));
            SetGitHubRepositoryPreview(null);

            try
            {
                var result = await _githubDesktopConnector.ConnectAsync(cancellationToken);
                if (!result.Success)
                {
                    _isGitHubDesktopConnected = false;
                    var message = SanitizeGitHubAppDetail(result.Message);
                    SetGitHubAppDisconnected(GitHubConnectionState.SignInRequired, () => message);
                    return;
                }

                ApplyGitHubDesktopConnectionResult(result);
            }
            catch (Exception ex)
            {
                _isGitHubDesktopConnected = false;
                var message = SanitizeGitHubAppDetail(ex.Message);
                SetGitHubAppDisconnected(
                    GitHubConnectionState.Unavailable,
                    () => Loc.Format("Integrations.GitHub.Detail.ConnectionFailed", message));
            }
            finally
            {
                IsConnectingGitHub = false;
            }
        }

        private void SaveGitHubApp()
        {
            if (ContainsRawPrivateKeyMaterial(GitHubPrivateKeyPath))
            {
                GitHubPrivateKeyPath = string.Empty;
                _settingsService.GitHubAppId = GitHubAppId;
                _settingsService.GitHubAppInstallationId = GitHubInstallationId;
                _settingsService.GitHubAppPrivateKeyPath = string.Empty;
                _settingsService.Save();
                SetGitHubAppDisconnected(
                    GitHubConnectionState.InvalidPrivateKeyPath,
                    static () => Loc.Get("Integrations.GitHub.Detail.KeyDiscarded"));
                return;
            }

            _settingsService.GitHubAppId = GitHubAppId;
            _settingsService.GitHubAppInstallationId = GitHubInstallationId;
            _settingsService.GitHubAppPrivateKeyPath = GitHubPrivateKeyPath;
            _settingsService.Save();
            RefreshGitHubAppConfigured();
            if (IsGitHubAppConfigured && !_isGitHubAppConnected)
            {
                SetGitHubConnectionText(
                    GitHubConnectionState.NotTested,
                    static () => Loc.Get("Integrations.GitHub.Detail.SettingsSaved"));
            }

            RefreshGitHubAppSetupSteps();
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
            SetGitHubAppDisconnected(
                GitHubConnectionState.NotConfigured,
                static () => Loc.Get("Integrations.GitHub.Detail.SettingsCleared"));
        }

        public async Task TestGitHubAppConnectionAsync(CancellationToken cancellationToken = default)
        {
            RefreshGitHubAppConfigured();
            if (!IsGitHubAppConfigured)
            {
                if (ContainsRawPrivateKeyMaterial(GitHubPrivateKeyPath))
                {
                    SetGitHubAppDisconnected(
                        GitHubConnectionState.InvalidPrivateKeyPath,
                        static () => Loc.Get("Integrations.GitHub.Detail.PemPathOnly"));
                    return;
                }

                SetGitHubAppDisconnected(GitHubConnectionState.MissingSettings, DescribeMissingGitHubAppSettings());
                return;
            }

            if (_githubAppClientFactory is null)
            {
                SetGitHubAppDisconnected(
                    GitHubConnectionState.Unavailable,
                    static () => Loc.Get("Integrations.GitHub.Detail.FactoryUnavailable"));
                return;
            }

            SaveGitHubApp();
            IsTestingGitHubAppConnection = true;
            SetGitHubConnectionText(
                GitHubConnectionState.Testing,
                static () => Loc.Get("Integrations.GitHub.Detail.CheckingApp"));
            SetGitHubRepositoryPreview(null);

            try
            {
                var client = CreateGitHubAppClientFromCurrentSettings();
                var status = await client.GetStatusAsync(cancellationToken);
                if (!status.IsConnected)
                {
                    SetGitHubAppDisconnected(
                        status.IsConfigured ? GitHubConnectionState.NeedsAction : GitHubConnectionState.MissingSettings,
                        DescribeGitHubAppStatus(status));
                    return;
                }

                SetGitHubAppConnected();
                SetGitHubConnectionText(GitHubConnectionState.Connected, DescribeGitHubAppStatus(status));

                var repositories = await client.ListRepositoriesAsync(cancellationToken);
                ApplyGitHubRepositoryListResult(status, repositories);
            }
            catch (Exception ex)
            {
                var message = SanitizeGitHubAppDetail(ex.Message);
                SetGitHubAppDisconnected(
                    GitHubConnectionState.Unavailable,
                    () => Loc.Format("Integrations.GitHub.Detail.AppTestFailed", message));
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
                SetGitHubConnectionText(
                    GitHubConnectionState.NotTested,
                    static () => Loc.Get("Integrations.GitHub.Detail.TestBeforeListing"));
                SetGitHubRepositoryPreview(null);
                RefreshGitHubAppSetupSteps();
                return;
            }

            if (_isGitHubDesktopConnected)
            {
                IsTestingGitHubAppConnection = true;
                try
                {
                    var result = await _githubDesktopConnector.ListRepositoriesAsync(cancellationToken);
                    if (!result.Success)
                    {
                        var message = SanitizeGitHubAppDetail(result.Message);
                        SetGitHubAppDisconnected(GitHubConnectionState.NeedsAction, () => message);
                        return;
                    }

                    ApplyGitHubDesktopConnectionResult(result);
                }
                catch (Exception ex)
                {
                    var message = SanitizeGitHubAppDetail(ex.Message);
                    SetGitHubAppDisconnected(
                        GitHubConnectionState.Unavailable,
                        () => Loc.Format("Integrations.GitHub.Detail.ListFailed", message));
                }
                finally
                {
                    IsTestingGitHubAppConnection = false;
                }

                return;
            }

            if (_githubAppClientFactory is null)
            {
                SetGitHubAppDisconnected(
                    GitHubConnectionState.Unavailable,
                    static () => Loc.Get("Integrations.GitHub.Detail.FactoryUnavailable"));
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
                var message = SanitizeGitHubAppDetail(ex.Message);
                SetGitHubAppDisconnected(
                    GitHubConnectionState.Unavailable,
                    () => Loc.Format("Integrations.GitHub.Detail.AppListFailed", message));
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
                var message = SanitizeGitHubAppDetail(repositories.Message);
                SetGitHubAppDisconnected(
                    GitHubConnectionState.NeedsAction,
                    () => Loc.Format("Integrations.GitHub.Detail.AppConnectedListFailed", message));
                return;
            }

            SetGitHubAppConnected();
            var repositoryCount = status.RepositoryCount ?? repositories.Repositories.Count;
            SetGitHubConnectionText(
                GitHubConnectionState.Connected,
                () => Loc.Format("Integrations.GitHub.Detail.AppConnected", repositoryCount));
            SetGitHubRepositoryPreview(DescribeGitHubRepositoryPreview(repositories.Repositories, repositoryCount));
            RefreshGitHubAppSetupSteps();
        }

        private void ApplyGitHubDesktopConnectionResult(GitHubDesktopConnectionResult result)
        {
            _isGitHubDesktopConnected = true;
            _isGitHubAppConnected = false;
            this.RaisePropertyChanged(nameof(GitHubAppStatusText));
            this.RaisePropertyChanged(nameof(IsGitHubStatusPositive));
            this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
            var repositoryCount = result.Repositories.Count;
            SetGitHubConnectionText(
                GitHubConnectionState.Connected,
                () => Loc.Format("Integrations.GitHub.Detail.SignInRepositoriesVisible", repositoryCount));
            SetGitHubRepositoryPreview(DescribeGitHubRepositoryPreview(result.Repositories, repositoryCount));
            RefreshGitHubAppSetupSteps();
        }

        private void SetGitHubAppConnected()
        {
            _isGitHubAppConnected = true;
            this.RaisePropertyChanged(nameof(GitHubAppStatusText));
            this.RaisePropertyChanged(nameof(IsGitHubStatusPositive));
            this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
        }

        private void SetGitHubAppDisconnected(GitHubConnectionState state, Func<string> detail)
        {
            _isGitHubAppConnected = false;
            _isGitHubDesktopConnected = false;
            this.RaisePropertyChanged(nameof(GitHubAppStatusText));
            this.RaisePropertyChanged(nameof(IsGitHubStatusPositive));
            this.RaisePropertyChanged(nameof(CanListGitHubAppRepositories));
            SetGitHubConnectionText(state, detail);
            SetGitHubRepositoryPreview(null);
            RefreshGitHubAppSetupSteps();
        }

        private void RefreshGitHubAppSetupSteps()
        {
            GitHubAppSetupSteps.Clear();
            GitHubAppSetupSteps.Add(BuildRequiredFieldStep(
                Loc.Get("Integrations.GitHub.AppId"),
                GitHubAppId,
                Loc.Get("Integrations.GitHub.Step.AppIdHelp")));
            GitHubAppSetupSteps.Add(BuildRequiredFieldStep(
                Loc.Get("Integrations.GitHub.InstallationId"),
                GitHubInstallationId,
                Loc.Get("Integrations.GitHub.Step.InstallationIdHelp")));
            GitHubAppSetupSteps.Add(BuildPrivateKeyPathStep());
            GitHubAppSetupSteps.Add(BuildConnectionTestStep());
            this.RaisePropertyChanged(nameof(HasGitHubAppSetupSteps));
        }

        private static RuntimeDiagnosticItemViewModel BuildRequiredFieldStep(
            string name,
            string value,
            string detail)
        {
            return string.IsNullOrWhiteSpace(value)
                ? new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Required"),
                    detail,
                    RuntimeDiagnosticSeverity.Blocked)
                : new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Provided"),
                    Loc.Format("Integrations.GitHub.Step.FieldPresent", name),
                    RuntimeDiagnosticSeverity.Ready);
        }

        private RuntimeDiagnosticItemViewModel BuildPrivateKeyPathStep()
        {
            var name = Loc.Get("Integrations.GitHub.PrivateKeyPath");
            if (string.IsNullOrWhiteSpace(GitHubPrivateKeyPath))
            {
                return new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Required"),
                    Loc.Get("Integrations.GitHub.Step.KeyPathHelp"),
                    RuntimeDiagnosticSeverity.Blocked);
            }

            if (ContainsRawPrivateKeyMaterial(GitHubPrivateKeyPath))
            {
                return new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.InvalidInput"),
                    Loc.Get("Integrations.GitHub.Step.KeyPathInvalid"),
                    RuntimeDiagnosticSeverity.Blocked);
            }

            if (FileExists(GitHubPrivateKeyPath))
            {
                return new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Provided"),
                    Loc.Get("Integrations.GitHub.Step.KeyPathReady"),
                    RuntimeDiagnosticSeverity.Ready);
            }

            return new RuntimeDiagnosticItemViewModel(
                name,
                Loc.Get("Integrations.GitHub.Step.CheckPath"),
                Loc.Get("Integrations.GitHub.Step.KeyPathMissing"),
                RuntimeDiagnosticSeverity.Warning);
        }

        private RuntimeDiagnosticItemViewModel BuildConnectionTestStep()
        {
            var name = Loc.Get("Integrations.GitHub.Step.ConnectionTest");
            if (_isGitHubAppConnected)
            {
                return new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Verified"),
                    Loc.Get("Integrations.GitHub.Step.TestVerified"),
                    RuntimeDiagnosticSeverity.Ready);
            }

            if (IsTestingGitHubAppConnection)
            {
                return new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Testing"),
                    Loc.Get("Integrations.GitHub.Detail.CheckingApp"),
                    RuntimeDiagnosticSeverity.Warning);
            }

            if (!IsGitHubAppConfigured)
            {
                return new RuntimeDiagnosticItemViewModel(
                    name,
                    Loc.Get("Integrations.GitHub.Step.Required"),
                    Loc.Get("Integrations.GitHub.Step.TestBlocked"),
                    RuntimeDiagnosticSeverity.Blocked);
            }

            return new RuntimeDiagnosticItemViewModel(
                name,
                Loc.Get("Integrations.GitHub.Step.RunTest"),
                Loc.Get("Integrations.GitHub.Step.TestPending"),
                RuntimeDiagnosticSeverity.Warning);
        }

        private static bool FileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsRawPrivateKeyMaterial(string value)
        {
            return value.Contains("-----BEGIN ", StringComparison.OrdinalIgnoreCase) &&
                   value.Contains("PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase);
        }

        private string SanitizeGitHubAppDetail(string? detail)
        {
            var redacted = SecretRedactor.Redact(detail);
            var currentPath = GitHubPrivateKeyPath.Trim();
            if (!string.IsNullOrWhiteSpace(currentPath) &&
                !ContainsRawPrivateKeyMaterial(currentPath))
            {
                redacted = redacted.Replace(
                    currentPath,
                    SecretRedactor.Replacement,
                    StringComparison.OrdinalIgnoreCase);
            }

            return redacted;
        }

        /// <summary>
        /// Describes a GitHub App status: the client's message, the settings it misses and the repositories it sees.
        /// </summary>
        private Func<string> DescribeGitHubAppStatus(GitHubAppConnectionStatus status)
        {
            var message = SanitizeGitHubAppDetail(status.Message);
            var missing = status.MissingSettings is { Count: > 0 }
                ? string.Join(", ", status.MissingSettings)
                : null;
            var repositoryCount = status.IsConnected ? status.RepositoryCount : null;

            return () =>
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    parts.Add(message);
                }

                if (missing is not null)
                {
                    parts.Add(Loc.Format("Integrations.GitHub.Detail.MissingFields", missing));
                }

                if (repositoryCount is not null)
                {
                    parts.Add(Loc.Format("Integrations.GitHub.Detail.RepositoriesVisible", repositoryCount));
                }

                return string.Join(" ", parts);
            };
        }

        /// <summary>
        /// Describes which GitHub App fields are empty now, named in the language current when shown.
        /// </summary>
        private Func<string> DescribeMissingGitHubAppSettings()
        {
            var missing = new List<Func<string>>();
            if (string.IsNullOrWhiteSpace(GitHubAppId))
            {
                missing.Add(static () => Loc.Get("Integrations.GitHub.AppId"));
            }

            if (string.IsNullOrWhiteSpace(GitHubInstallationId))
            {
                missing.Add(static () => Loc.Get("Integrations.GitHub.InstallationId"));
            }

            if (string.IsNullOrWhiteSpace(GitHubPrivateKeyPath))
            {
                missing.Add(static () => Loc.Get("Integrations.GitHub.PrivateKeyPath"));
            }

            return () => Loc.Format(
                "Integrations.GitHub.Detail.MissingSettings",
                string.Join(", ", missing.Select(label => label())));
        }

        /// <summary>
        /// Describes up to five repositories and how many more there are, or returns <see langword="null"/> for none.
        /// </summary>
        private static Func<string>? DescribeGitHubRepositoryPreview(
            IReadOnlyList<GitHubRepositorySummary> repositories,
            int expectedRepositoryCount)
        {
            if (repositories.Count == 0)
            {
                return null;
            }

            const int maxVisibleRepositories = 5;
            var visible = repositories
                .OrderBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
                .Take(maxVisibleRepositories)
                .ToArray();
            var hiddenCount = Math.Max(expectedRepositoryCount, repositories.Count) - visible.Length;

            return () =>
            {
                var lines = visible
                    .Select(repository => Loc.Format(
                        "Integrations.GitHub.Repository.Line",
                        repository.FullName,
                        repository.IsPrivate
                            ? Loc.Get("Integrations.GitHub.Repository.Private")
                            : Loc.Get("Integrations.GitHub.Repository.Public"),
                        repository.DefaultBranch))
                    .ToList();
                if (hiddenCount > 0)
                {
                    lines.Add(Loc.Format("Integrations.GitHub.Repository.More", hiddenCount));
                }

                return string.Join(Environment.NewLine, lines);
            };
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
            SaveWebSearch();
            SaveGitHubApp();
            SaveEmail();
            SaveSms();
        }

        #endregion
    }

    /// <summary>
    /// An email provider in the Integrations picker.
    /// </summary>
    /// <param name="Id">The saved provider name, such as <c>Gmail</c> or <c>Custom</c>.</param>
    /// <param name="DisplayName">The name shown in the current language.</param>
    public sealed record EmailProviderOption(string Id, string DisplayName)
    {
        /// <inheritdoc />
        public override string ToString() => DisplayName;
    }
}

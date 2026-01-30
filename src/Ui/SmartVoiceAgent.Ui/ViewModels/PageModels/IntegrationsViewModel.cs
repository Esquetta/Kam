using ReactiveUI;
using SmartVoiceAgent.Ui.Services;
using System.Windows.Input;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    /// <summary>
    /// ViewModel for the Integrations view - manages external service API keys
    /// </summary>
    public class IntegrationsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        
        // Todoist
        private string _todoistApiKey = string.Empty;
        private bool _isTaskAgentEnabled;
        
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
        {
            Title = "INTEGRATIONS";
            _settingsService = new JsonSettingsService();
            
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

        #endregion

        #region Methods

        private void LoadAllSettings()
        {
            // Todoist
            TodoistApiKey = _settingsService.TodoistApiKey;
            
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
            SaveEmail();
            SaveSms();
        }

        #endregion
    }
}

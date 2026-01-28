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
        private string _todoistApiKey = string.Empty;
        private bool _isTaskAgentEnabled;

        public IntegrationsViewModel()
        {
            Title = "INTEGRATIONS";
            _settingsService = new JsonSettingsService();
            
            // Load saved settings
            _settingsService.Load();
            
            // Load existing API key
            TodoistApiKey = _settingsService.TodoistApiKey;
            IsTaskAgentEnabled = !string.IsNullOrWhiteSpace(TodoistApiKey);
            
            // Commands
            SaveApiKeyCommand = ReactiveCommand.Create(SaveApiKey);
            ClearApiKeyCommand = ReactiveCommand.Create(ClearApiKey);
        }

        #region Properties

        /// <summary>
        /// Todoist API key for task management integration
        /// </summary>
        public string TodoistApiKey
        {
            get => _todoistApiKey;
            set
            {
                this.RaiseAndSetIfChanged(ref _todoistApiKey, value);
                // Update enabled status when key changes
                IsTaskAgentEnabled = !string.IsNullOrWhiteSpace(value);
            }
        }

        /// <summary>
        /// Indicates whether the Task Agent is enabled (has valid API key)
        /// </summary>
        public bool IsTaskAgentEnabled
        {
            get => _isTaskAgentEnabled;
            private set => this.RaiseAndSetIfChanged(ref _isTaskAgentEnabled, value);
        }

        /// <summary>
        /// Placeholder text for the API key input
        /// </summary>
        public string ApiKeyPlaceholder => "Enter your Todoist API key...";

        /// <summary>
        /// Description text for the Todoist integration
        /// </summary>
        public string TodoistDescription => "Connect to Todoist for task management capabilities. The Task Agent will be enabled when a valid API key is provided.";

        /// <summary>
        /// Status text indicating whether the integration is active
        /// </summary>
        public string IntegrationStatusText => IsTaskAgentEnabled ? "ACTIVE" : "NOT CONFIGURED";

        #endregion

        #region Commands

        public ICommand SaveApiKeyCommand { get; }
        public ICommand ClearApiKeyCommand { get; }

        #endregion

        #region Methods

        private void SaveApiKey()
        {
            _settingsService.TodoistApiKey = TodoistApiKey;
            _settingsService.Save();
        }

        private void ClearApiKey()
        {
            TodoistApiKey = string.Empty;
            _settingsService.TodoistApiKey = string.Empty;
            _settingsService.Save();
        }

        public override void OnNavigatedTo()
        {
            // Reload settings when navigating to this view
            _settingsService.Load();
            TodoistApiKey = _settingsService.TodoistApiKey;
        }

        public override void OnNavigatedFrom()
        {
            // Auto-save when leaving the view
            if (_settingsService.TodoistApiKey != TodoistApiKey)
            {
                SaveApiKey();
            }
        }

        #endregion
    }
}

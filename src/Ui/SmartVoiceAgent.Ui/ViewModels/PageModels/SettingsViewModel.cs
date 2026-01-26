using ReactiveUI;
using SmartVoiceAgent.Ui.Services;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly MainWindowViewModel? _mainViewModel;
        private int _selectedLanguageIndex;

        public SettingsViewModel()
        {
            Title = "SETTINGS";
            _selectedLanguageIndex = 0;
        }

        public SettingsViewModel(MainWindowViewModel mainViewModel) : this()
        {
            _mainViewModel = mainViewModel;
            _selectedLanguageIndex = mainViewModel.SelectedLanguageIndex;
        }

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
    }
}

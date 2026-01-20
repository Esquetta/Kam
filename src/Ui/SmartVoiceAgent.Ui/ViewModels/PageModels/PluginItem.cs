using ReactiveUI;
using System.Collections.ObjectModel;

namespace SmartVoiceAgent.Ui.ViewModels.PageModels
{
    public class PluginItem : ReactiveObject
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string IconColor { get; set; } = "#00F2FF";
        public string IconPath { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class PluginsViewModel : ViewModelBase
    {
        private ObservableCollection<PluginItem> _plugins = new();

        public ObservableCollection<PluginItem> Plugins
        {
            get => _plugins;
            set => this.RaiseAndSetIfChanged(ref _plugins, value);
        }

        public PluginsViewModel()
        {
            Title = "PLUGINS";
            LoadPlugins();
        }

        private void LoadPlugins()
        {
            Plugins = new ObservableCollection<PluginItem>
            {
                new PluginItem
                {
                    Name = "VOICE ENGINE",
                    Description = "Real-time voice processing and recognition engine with neural network integration.",
                    Status = "Active",
                    IconColor = "#00F2FF",
                    IconPath = "M12,2A3,3 0 0,1 15,5V11A3,3 0 0,1 12,14A3,3 0 0,1 9,11V5A3,3 0 0,1 12,2M19,11C19,14.53 16.39,17.44 13,17.93V21H11V17.93C7.61,17.44 5,14.53 5,11H7A5,5 0 0,0 12,16A5,5 0 0,0 17,11H19Z",
                    IsActive = true
                },
                new PluginItem
                {
                    Name = "AI ANALYZER",
                    Description = "Advanced semantic analysis and pattern recognition using machine learning algorithms.",
                    Status = "Active",
                    IconColor = "#8B5CF6",
                    IconPath = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M11,17V16H9V14H13V15H12V17H13V19H9V17H11M13,13H11V11H13V13M13,10H11V8H13V10M11,7V5H13V7H11Z",
                    IsActive = true
                },
                new PluginItem
                {
                    Name = "TASK SCHEDULER",
                    Description = "Automated task scheduling and execution management with priority queuing.",
                    Status = "Active",
                    IconColor = "#F59E0B",
                    IconPath = "M19,19H5V8H19M16,1V3H8V1H6V3H5C3.89,3 3,3.89 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V5C21,3.89 20.1,3 19,3H18V1M17,12H12V17H17V12Z",
                    IsActive = true
                },
                new PluginItem
                {
                    Name = "DATA SYNC",
                    Description = "Cloud synchronization and backup management system.",
                    Status = "Inactive",
                    IconColor = "#6B7280",
                    IconPath = "M12,18A6,6 0 0,1 6,12C6,11 6.25,10.03 6.7,9.2L5.24,7.74C4.46,8.97 4,10.43 4,12A8,8 0 0,0 12,20V23L16,19L12,15M12,4V1L8,5L12,9V6A6,6 0 0,1 18,12C18,13 17.75,13.97 17.3,14.8L18.76,16.26C19.54,15.03 20,13.57 20,12A8,8 0 0,0 12,4Z",
                    IsActive = false
                },
                new PluginItem
                {
                    Name = "SECURITY MODULE",
                    Description = "Advanced encryption and security monitoring system.",
                    Status = "Inactive",
                    IconColor = "#6B7280",
                    IconPath = "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1Z",
                    IsActive = false
                },
                new PluginItem
                {
                    Name = "PLUGIN SLOT",
                    Description = "Click to install a new plugin module.",
                    Status = "Available",
                    IconColor = "#6B7280",
                    IconPath = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z",
                    IsActive = false
                }
            };
        }

        public override void OnNavigatedTo()
        {
            // Plugins view'a gelince yapılacak işlemler
        }
    }
}
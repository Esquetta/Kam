using Avalonia.Media;
using ReactiveUI;
using SolidColorBrush = Avalonia.Media.SolidColorBrush;

namespace SmartVoiceAgent.Ui.ViewModels
{
    public abstract class ViewModelBase : ReactiveObject
    {
        private string _title = string.Empty;

        public string Title
        {
            get => _title;
            protected set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        private string _statusText = "SYSTEM ONLINE";
        private IBrush _statusColor = new SolidColorBrush(Avalonia.Media.Color.Parse("#10B981"));

        /// <summary>
        /// Status text for header display (e.g., "SYSTEM ONLINE", "SYSTEM OFFLINE")
        /// </summary>
        public virtual string StatusText
        {
            get => _statusText;
            protected set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        /// <summary>
        /// Status color for header display indicator
        /// </summary>
        public virtual IBrush StatusColor
        {
            get => _statusColor;
            protected set => this.RaiseAndSetIfChanged(ref _statusColor, value);
        }

        public virtual void OnNavigatedTo() { }
        public virtual void OnNavigatedFrom() { }
    }
}

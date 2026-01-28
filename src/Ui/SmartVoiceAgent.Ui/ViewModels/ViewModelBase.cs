using Avalonia.Media;
using ReactiveUI;

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

        /// <summary>
        /// Status text for header display (e.g., "SYSTEM ONLINE", "SYSTEM OFFLINE")
        /// </summary>
        public virtual string StatusText => "SYSTEM ONLINE";

        /// <summary>
        /// Status color for header display indicator
        /// </summary>
        public virtual IBrush StatusColor => Brush.Parse("#10B981"); // Default green

        public virtual void OnNavigatedTo() { }
        public virtual void OnNavigatedFrom() { }
    }
}

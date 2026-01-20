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

        public virtual void OnNavigatedTo() { }
        public virtual void OnNavigatedFrom() { }
    }
}

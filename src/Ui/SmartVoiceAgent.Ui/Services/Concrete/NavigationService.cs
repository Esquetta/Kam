using SmartVoiceAgent.Ui.Services.Abstract;
using SmartVoiceAgent.Ui.ViewModels;
using System;

namespace SmartVoiceAgent.Ui.Services
{
    public class NavigationService : INavigationService
    {
        private NavView _currentView = NavView.Coordinator;

        public NavView CurrentView
        {
            get => _currentView;
            private set
            {
                if (_currentView != value)
                {
                    _currentView = value;
                    NavigationChanged?.Invoke(this, value);
                }
            }
        }

        public event EventHandler<NavView>? NavigationChanged;

        public void NavigateTo(NavView view)
        {
            CurrentView = view;
        }
    }
}
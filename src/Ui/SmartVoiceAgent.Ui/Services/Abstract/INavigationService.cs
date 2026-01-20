using SmartVoiceAgent.Ui.ViewModels;
using System;

namespace SmartVoiceAgent.Ui.Services.Abstract
{
    public interface INavigationService
    {
        NavView CurrentView { get; }
        void NavigateTo(NavView view);
        event EventHandler<NavView>? NavigationChanged;
    }
}
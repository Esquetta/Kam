#nullable disable
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using System;
using System.Linq;
using System.Globalization;

namespace SmartVoiceAgent.Ui.Services
{
    public class LocalizationService
    {
        private const string DefaultLanguage = "en-US";

        public static LocalizationService Instance { get; } = new LocalizationService();

        public void SetLanguage(string languageCode)
        {
            var app = global::Avalonia.Application.Current;
            if (app?.Resources?.MergedDictionaries == null)
            {
                return;
            }

            var merged = app!.Resources.MergedDictionaries;
            var translations = merged.OfType<ResourceInclude>()
                .FirstOrDefault(x => x.Source?.OriginalString?.Contains("/Lang/") == true);

            if (translations != null)
            {
                merged.Remove(translations);
            }

            var newSource = new Uri($"avares://SmartVoiceAgent.Ui/Assets/Lang/{languageCode}.axaml");
            merged.Add(
                new ResourceInclude(newSource)
                {
                    Source = newSource
                });
        }
    }
}

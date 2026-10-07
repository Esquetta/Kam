using FluentAssertions;
using SmartVoiceAgent.Ui.Services;
using System.Xml.Linq;

namespace SmartVoiceAgent.Tests.Ui;

public sealed class SettingsViewVoiceMetadataTests
{
    [Fact]
    public void VoiceSection_ReadsEveryLabelFromLanguageResources()
    {
        var section = LoadVoiceSection(localized: false);

        var texts = section
            .DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName is "Text" or "ToolTip.Tip" or "Content")
            .Where(attribute => attribute.Parent!.Name.LocalName != "Setter")
            .Select(attribute => attribute.Value)
            .ToList();

        texts.Should().NotBeEmpty();
        texts.Should().OnlyContain(value =>
                value.StartsWith("{DynamicResource Lang.", StringComparison.Ordinal)
                || value.StartsWith("{Binding ", StringComparison.Ordinal),
            "voice copy must follow the interface language");
        texts.Count(value => value.StartsWith("{DynamicResource Lang.Settings.Voice.", StringComparison.Ordinal))
            .Should().BeGreaterThan(30);
    }

    [Fact]
    public void VoiceSection_HasTheEngineModelDownloadAndApiControls()
    {
        var section = LoadVoiceSection(localized: false);

        Element(section, "ComboBox", "SelectedItem", "{Binding SelectedVoiceLanguage, Mode=TwoWay}");
        Element(section, "ComboBox", "SelectedItem", "{Binding SelectedSpeechEngine, Mode=TwoWay}");

        var models = Element(section, "ComboBox", "ItemsSource", "{Binding LocalSpeechModelOptions}");
        models.Attribute("SelectedItem")!.Value.Should().Be("{Binding SelectedLocalSpeechModel, Mode=TwoWay}");
        PanelVisibility(models).Should().Be("{Binding IsLocalSpeechEngine}");

        var download = Element(section, "Button", "Command", "{Binding DownloadSpeechModelCommand}");
        download.Attribute("IsEnabled")!.Value.Should().Be("{Binding CanDownloadSpeechModel}");
        PanelVisibility(download).Should().Be("{Binding IsLocalSpeechEngine}");

        var progress = Element(section, "ProgressBar", "Value", "{Binding SpeechModelDownloadProgress, Mode=OneWay}");
        progress.Parent!.Attribute("IsVisible")!.Value.Should().Be("{Binding IsDownloadingSpeechModel}");
        Element(section, "TextBlock", "Text", "{Binding SpeechModelDownloadPercent}");
        Element(section, "TextBlock", "Text", "{Binding SpeechModelStatus}");
        Element(section, "TextBlock", "Text", "{Binding SpeechModelDownloadError}");

        var endpoint = Element(section, "TextBox", "Text", "{Binding SpeechApiEndpoint, Mode=TwoWay}");
        endpoint.Attribute("PlaceholderText")!.Value.Should().Be("https://api.openai.com/v1");
        PanelVisibility(endpoint).Should().Be("{Binding IsApiSpeechEngine}");
        Element(section, "TextBox", "Text", "{Binding SpeechApiModel, Mode=TwoWay}")
            .Attribute("PlaceholderText")!.Value.Should().Be("whisper-1");
        Element(section, "TextBox", "Text", "{Binding SpeechApiKey, Mode=TwoWay}")
            .Attribute("PasswordChar")!.Value.Should().Be("*");
        Element(section, "TextBlock", "Text", "{Binding MaskedSpeechApiKey}");
    }

    [Fact]
    public void VoiceSection_HasMicrophoneHandsFreeAndSpokenReplyControls()
    {
        var section = LoadVoiceSection(localized: false);

        Element(section, "ComboBox", "ItemsSource", "{Binding InputDevices}");
        Element(section, "Button", "Command", "{Binding StartMicTestCommand}");

        Element(section, "ToggleSwitch", "IsChecked", "{Binding WakeWordEnabled, Mode=TwoWay}");
        Element(section, "TextBox", "Text", "{Binding WakeWord, Mode=TwoWay}")
            .Attribute("IsEnabled")!.Value.Should().Be("{Binding WakeWordEnabled}");
        Element(section, "TextBlock", "Text", "{Binding WakeWordNote}");
        Element(section, "ComboBox", "SelectedItem", "{Binding SelectedTalkShortcut, Mode=TwoWay}");

        Element(section, "ComboBox", "SelectedItem", "{Binding SelectedSpokenReplies, Mode=TwoWay}");
        Element(section, "ComboBox", "SelectedItem", "{Binding SelectedSpeechVoice, Mode=TwoWay}")
            .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanUseSpeechOutput}");
        var rate = Element(section, "Slider", "Value", "{Binding SpeechRate, Mode=TwoWay}");
        rate.Attribute("Minimum")!.Value.Should().Be("-5");
        rate.Attribute("Maximum")!.Value.Should().Be("5");
        rate.Attribute("TickFrequency")!.Value.Should().Be("1");
        rate.Attribute("IsSnapToTickEnabled")!.Value.Should().Be("True");
        Element(section, "Button", "Command", "{Binding PreviewSpeechCommand}")
            .Attribute("IsEnabled")!.Value.Should().Be("{Binding CanPreviewSpeech}");
        Element(section, "Button", "Command", "{Binding StopSpeechPreviewCommand}");
        Element(section, "TextBlock", "Text", "{Binding SpeechOutputProblem}")
            .Attribute("IsVisible")!.Value.Should().Be("{Binding HasSpeechOutputProblem}");
        Element(section, "ComboBox", "ItemsSource", "{Binding OutputDevices}");
    }

    [Fact]
    public void VoiceSection_ShowsItsCardsInOrder()
    {
        var section = LoadVoiceSection(localized: true);

        var titles = section
            .Elements()
            .Where(element => element.Name.LocalName == "Border" && element.Attribute("Classes")?.Value == "SettingsItem")
            .Select(card => card.Descendants().First(element => element.Name.LocalName == "TextBlock").Attribute("Text")!.Value)
            .ToList();

        titles.Should().Equal("Speech recognition", "Microphone", "Hands-free", "Spoken replies");
        section.Descendants().First(element => element.Name.LocalName == "TextBlock")
            .Attribute("Text")!.Value.Should().Be("VOICE");
    }

    [Fact]
    public void VoiceSection_ExplainsPrivacyAndTheShortcut()
    {
        var text = LocalizedXaml.ReadAllText(SettingsViewMetadataTests.FindSettingsViewXamlPath());

        text.Should().Contain("Text=\"Press to start talking, press again to send. Works while Kam is in the background on Windows.\"");
        text.Should().Contain("Text=\"Your recordings are sent to this service to be turned into text.\"");
        LocalizedXaml.English["Settings.Voice.WakeWord.Note"].Should().Contain("Nothing is sent anywhere until Kam hears it.");
    }

    [Fact]
    public void VoiceText_IsTranslatedIntoTurkish()
    {
        var english = LocalizationService.LoadDictionary("en-US");
        var turkish = LocalizationService.LoadDictionary("tr-TR");
        var voiceKeys = english.Keys.Where(key => key.StartsWith("Settings.Voice.", StringComparison.Ordinal)).ToList();

        voiceKeys.Should().NotBeEmpty();
        foreach (var key in voiceKeys)
        {
            turkish.Should().ContainKey(key);
            turkish[key].Should().NotBeNullOrWhiteSpace(key);
        }

        turkish["Settings.Voice.Recognition"].Should().Be("Konuşma tanıma");
        turkish["Settings.Voice.Engine.Local"].Should().StartWith("Bu bilgisayarda");
        turkish["Settings.Voice.WakeWord"].Should().Be("Uyandırma sözcüğü");
        turkish["Settings.Voice.Shortcut"].Should().Be("Konuşma kısayolu");
        turkish["Settings.Voice.SpokenReplies"].Should().Be("Yanıtları sesli oku");
        turkish["Settings.Voice.Preview.Sample"].Should().StartWith("Merhaba");
    }

    private static XElement LoadVoiceSection(bool localized)
    {
        var path = SettingsViewMetadataTests.FindSettingsViewXamlPath();
        var markup = localized ? LocalizedXaml.ReadAllText(path) : File.ReadAllText(path);
        return SettingsViewMetadataTests.FindVoiceSection(XDocument.Parse(markup).Root!);
    }

    private static XElement Element(XElement section, string name, string attribute, string value)
    {
        var matches = section
            .Descendants()
            .Where(element => element.Name.LocalName == name && element.Attribute(attribute)?.Value == value)
            .ToList();

        matches.Should().ContainSingle($"the voice section has one {name} with {attribute}=\"{value}\"");
        return matches[0];
    }

    private static string? PanelVisibility(XElement element)
    {
        return element
            .Ancestors()
            .Select(ancestor => ancestor.Attribute("IsVisible")?.Value)
            .FirstOrDefault(value => value is not null);
    }
}

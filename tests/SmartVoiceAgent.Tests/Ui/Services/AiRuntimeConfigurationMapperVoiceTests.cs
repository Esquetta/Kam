using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Infrastructure.Services.Voice;
using SmartVoiceAgent.Ui.Services;
using System.Globalization;

namespace SmartVoiceAgent.Tests.Ui.Services;

public sealed class AiRuntimeConfigurationMapperVoiceTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-voice-mapper-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateVoiceOverrides_Defaults_MapEngineModelWakeWordAndRate()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.Language = "en-US";

        var overrides = AiRuntimeConfigurationMapper.CreateVoiceOverrides(settings);

        overrides.Should().Contain("Voice:Language", "auto");
        overrides.Should().Contain("Voice:SpeechEngine", "Local");
        overrides.Should().Contain("Voice:LocalModel", "base");
        overrides.Should().Contain("Voice:WakeWord", "Hey Kam");
        overrides.Should().Contain("Voice:SpeechRate", "0");
        overrides.Should().Contain("Voice:InputDeviceId", string.Empty);
        overrides.Should().Contain("Voice:OutputDeviceId", string.Empty);
        overrides.Keys.Should().NotContain(key => key.StartsWith("Voice:SpeechApi:", StringComparison.Ordinal));
        overrides.Keys.Should().NotContain("Voice:SpeechVoice");
    }

    [Theory]
    [InlineData("tr", "en-US", "tr")]
    [InlineData("EN", "tr-TR", "en")]
    [InlineData("auto", "tr-TR", "auto")]
    [InlineData("", "tr-TR", "auto")]
    [InlineData("", "en-US", "auto")]
    [InlineData(" ", "tr", "auto")]
    public void CreateVoiceOverrides_SpokenLanguage_IsTheChosenLanguageOrDetected(
        string voiceLanguage,
        string interfaceLanguage,
        string expected)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.VoiceLanguage = voiceLanguage;
        settings.Language = interfaceLanguage;

        AiRuntimeConfigurationMapper.CreateVoiceOverrides(settings).Should().Contain("Voice:Language", expected);
        AiRuntimeConfigurationMapper.ResolveSpokenLanguage(settings).Should().Be(expected);
    }

    [Fact]
    public void ResolveInterfaceLanguage_WithoutInterfaceLanguage_UsesTheSystemLanguage()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.VoiceLanguage = string.Empty;
        settings.Language = string.Empty;
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            AiRuntimeConfigurationMapper.ResolveInterfaceLanguage(settings).Should().Be("tr");
            AiRuntimeConfigurationMapper.CreateVoiceOverrides(settings).Should().Contain("Voice:Language", "auto");

            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            AiRuntimeConfigurationMapper.ResolveInterfaceLanguage(settings).Should().Be("en");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void CreateVoiceOverrides_MapsEverySavedVoiceSetting()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.VoiceLanguage = "tr";
        settings.SpeechEngine = "OpenAI";
        settings.LocalSpeechModel = "small";
        settings.SpeechApiEndpoint = " http://localhost:8000/v1 ";
        settings.SpeechApiModel = "whisper-large";
        settings.SpeechApiKey = "sk-voice";
        settings.SelectedInputDeviceId = "mic-1";
        settings.SelectedOutputDeviceId = "speaker-2";
        settings.WakeWord = "Merhaba Kam";
        settings.SpeechVoice = "voice-tolga";
        settings.SpeechRate = -3;

        var overrides = AiRuntimeConfigurationMapper.CreateVoiceOverrides(settings);

        overrides.Should().Contain("Voice:Language", "tr");
        overrides.Should().Contain("Voice:SpeechEngine", "OpenAI");
        overrides.Should().Contain("Voice:LocalModel", "small");
        overrides.Should().Contain("Voice:SpeechApi:Endpoint", "http://localhost:8000/v1");
        overrides.Should().Contain("Voice:SpeechApi:Model", "whisper-large");
        overrides.Should().Contain("Voice:SpeechApi:ApiKey", "sk-voice");
        overrides.Should().Contain("Voice:InputDeviceId", "mic-1");
        overrides.Should().Contain("Voice:OutputDeviceId", "speaker-2");
        overrides.Should().Contain("Voice:WakeWord", "Merhaba Kam");
        overrides.Should().Contain("Voice:SpeechVoice", "voice-tolga");
        overrides.Should().Contain("Voice:SpeechRate", "-3");
    }

    [Fact]
    public void CreateVoiceOverrides_WritesTheRateWithTheInvariantCulture()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.SpeechRate = -2;
        var original = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not "-" would break int.Parse with the invariant culture.
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE") { NumberFormat = { NegativeSign = "−" } };

            AiRuntimeConfigurationMapper.CreateVoiceOverrides(settings).Should().Contain("Voice:SpeechRate", "-2");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void CreateOverrides_IncludesVoiceValues_ThatVoiceSettingsCanRead()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.VoiceLanguage = "tr";
        settings.SpeechEngine = "OpenAI";
        settings.SpeechApiKey = "sk-voice";
        settings.LocalSpeechModel = "large-v3-turbo";
        settings.SpeechVoice = "voice-tolga";
        settings.SpeechRate = 4;

        var overrides = AiRuntimeConfigurationMapper.CreateOverrides(settings);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(overrides).Build();
        var voice = VoiceSettings.Read(configuration);

        voice.Language.Should().Be("tr");
        voice.UsesApi.Should().BeTrue();
        voice.HasApi.Should().BeTrue();
        voice.ApiKey.Should().Be("sk-voice");
        voice.ApiEndpoint.Should().Be(VoiceSettings.DefaultApiEndpoint);
        voice.LocalModel.Should().Be("large-v3-turbo");
        voice.SpeechVoice.Should().Be("voice-tolga");
        voice.SpeechRate.Should().Be(4);
        voice.WakeWord.Should().Be("Hey Kam");
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }
}

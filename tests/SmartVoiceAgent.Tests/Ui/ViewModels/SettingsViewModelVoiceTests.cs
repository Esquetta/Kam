using FluentAssertions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Ui.Services;
using SmartVoiceAgent.Ui.ViewModels.PageModels;

namespace SmartVoiceAgent.Tests.Ui.ViewModels;

public sealed class SettingsViewModelVoiceTests : IDisposable
{
    private readonly string _settingsDirectory = Path.Combine(
        Path.GetTempPath(),
        "kam-settings-voice-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SpokenLanguageOptions_WriteTheirValues()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.VoiceLanguageOptions.Select(option => option.Value).Should().Equal("auto", "tr", "en");
        viewModel.VoiceLanguageOptions.Select(option => option.Label)
            .Should().Equal("Detect automatically", "Türkçe", "English");
        viewModel.SelectedVoiceLanguage!.Value.Should().Be("auto");

        viewModel.SelectedVoiceLanguage = viewModel.VoiceLanguageOptions.Single(option => option.Value == "tr");
        settings.VoiceLanguage.Should().Be("tr");

        viewModel.SelectedVoiceLanguage = viewModel.VoiceLanguageOptions.Single(option => option.Value == "auto");
        settings.VoiceLanguage.Should().Be("auto");
    }

    [Fact]
    public void SpokenLanguage_KeepsASavedLanguageThatIsNotListed()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.VoiceLanguage = "de";
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SelectedVoiceLanguage!.Value.Should().Be("de");
        viewModel.VoiceLanguageOptions.Should().HaveCount(4);
        settings.VoiceLanguage.Should().Be("de");
    }

    [Fact]
    public void SpeechEngineOptions_SwitchBetweenLocalAndApi()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SpeechEngineOptions.Select(option => option.Value).Should().Equal("Local", "OpenAI");
        viewModel.SpeechEngineOptions.Select(option => option.Label)
            .Should().Equal("On this computer (Whisper)", "OpenAI-compatible API");
        viewModel.IsLocalSpeechEngine.Should().BeTrue();
        viewModel.IsApiSpeechEngine.Should().BeFalse();

        viewModel.SelectedSpeechEngine = viewModel.SpeechEngineOptions[1];

        settings.SpeechEngine.Should().Be("OpenAI");
        viewModel.IsApiSpeechEngine.Should().BeTrue();
        viewModel.IsLocalSpeechEngine.Should().BeFalse();

        viewModel.SelectedSpeechEngine = viewModel.SpeechEngineOptions[0];
        settings.SpeechEngine.Should().Be("Local");
    }

    [Fact]
    public void SpeechEngine_ReadsASavedApiEngineIgnoringCase()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.SpeechEngine = "openai";
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SelectedSpeechEngine!.Value.Should().Be("OpenAI");
        viewModel.IsApiSpeechEngine.Should().BeTrue();
    }

    [Fact]
    public void LocalModelOptions_LeaveTinyForTheWakePhraseAndShowSizes()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.LocalSpeechModelOptions.Select(option => option.Name).Should().Equal("base", "small", "large-v3-turbo");
        viewModel.LocalSpeechModelOptions.Select(option => option.Label)
            .Should().Equal("base · 148 MB", "small · 488 MB", "large-v3-turbo · 574 MB");
        viewModel.LocalSpeechModelOptions.Select(option => option.Hint)
            .Should().Equal("Fast", "More accurate", "Most accurate, slower");
        viewModel.SelectedLocalSpeechModel!.Name.Should().Be("base");

        viewModel.SelectedLocalSpeechModel = viewModel.LocalSpeechModelOptions[1];
        settings.LocalSpeechModel.Should().Be("small");
    }

    [Fact]
    public void WithoutSpeechServices_TheSectionExplainsWhatItCannotDo()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.HasSpeechModelStore.Should().BeFalse();
        viewModel.CanDownloadSpeechModel.Should().BeFalse();
        viewModel.SpeechModelStatus.Should().Be("Not downloaded yet. It downloads the first time you talk to Kam.");
        viewModel.IsTextToSpeechAvailable.Should().BeFalse();
        viewModel.CanUseSpeechOutput.Should().BeFalse();
        viewModel.CanPreviewSpeech.Should().BeFalse();
        viewModel.HasSpeechOutputProblem.Should().BeTrue();
        viewModel.SpeechOutputProblem.Should().Contain("no speech engine");
        viewModel.SpeechVoiceOptions.Should().ContainSingle().Which.Label.Should().Be("Automatic");
    }

    [Fact]
    public async Task DownloadSpeechModel_DownloadsTheSelectedModel()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);
        var store = new FakeSpeechModelStore();
        viewModel.UseSpeechServices(store, null);
        viewModel.SelectedLocalSpeechModel = viewModel.LocalSpeechModelOptions.Single(option => option.Name == "small");

        viewModel.HasSpeechModelStore.Should().BeTrue();
        viewModel.IsSpeechModelDownloaded.Should().BeFalse();
        viewModel.CanDownloadSpeechModel.Should().BeTrue();

        await viewModel.DownloadSpeechModelAsync();

        store.Requested.Should().Equal("small");
        viewModel.IsDownloadingSpeechModel.Should().BeFalse();
        viewModel.IsSpeechModelDownloaded.Should().BeTrue();
        viewModel.CanDownloadSpeechModel.Should().BeFalse();
        viewModel.SpeechModelStatus.Should().Be("Downloaded");
        viewModel.HasSpeechModelDownloadError.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadSpeechModel_WhileRunning_ShowsProgressAndLocksTheButton()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new FakeSpeechModelStore { Gate = gate };
        viewModel.UseSpeechServices(store, null);

        var download = viewModel.DownloadSpeechModelAsync();

        viewModel.IsDownloadingSpeechModel.Should().BeTrue();
        viewModel.CanDownloadSpeechModel.Should().BeFalse();
        viewModel.SpeechModelStatus.Should().Be("Downloading...");
        viewModel.SpeechModelDownloadProgress.Should().Be(0);
        viewModel.SpeechModelDownloadPercent.Should().Be("0%");

        await viewModel.DownloadSpeechModelAsync();
        store.Requested.Should().ContainSingle("a second click while downloading does nothing");

        gate.SetResult();
        await download;

        viewModel.IsDownloadingSpeechModel.Should().BeFalse();
        viewModel.IsSpeechModelDownloaded.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadSpeechModel_Failure_ShowsTheErrorAndAllowsAnotherTry()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);
        var store = new FakeSpeechModelStore { Failure = new HttpRequestException("Connection refused") };
        viewModel.UseSpeechServices(store, null);

        await viewModel.DownloadSpeechModelAsync();

        viewModel.IsDownloadingSpeechModel.Should().BeFalse();
        viewModel.IsSpeechModelDownloaded.Should().BeFalse();
        viewModel.HasSpeechModelDownloadError.Should().BeTrue();
        viewModel.SpeechModelDownloadError.Should().Be("Download failed: Connection refused");
        viewModel.CanDownloadSpeechModel.Should().BeTrue();

        store.Failure = null;
        await viewModel.DownloadSpeechModelAsync();

        viewModel.HasSpeechModelDownloadError.Should().BeFalse();
        viewModel.IsSpeechModelDownloaded.Should().BeTrue();
    }

    [Fact]
    public void DownloadedModel_ShowsDownloadedAndFollowsTheSelection()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);
        var store = new FakeSpeechModelStore();
        store.Downloaded.Add("base");
        viewModel.UseSpeechServices(store, null);

        viewModel.IsSpeechModelDownloaded.Should().BeTrue();
        viewModel.SpeechModelStatus.Should().Be("Downloaded");
        viewModel.CanDownloadSpeechModel.Should().BeFalse();

        viewModel.SelectedLocalSpeechModel = viewModel.LocalSpeechModelOptions.Single(option => option.Name == "large-v3-turbo");

        viewModel.IsSpeechModelDownloaded.Should().BeFalse();
        viewModel.CanDownloadSpeechModel.Should().BeTrue();
    }

    [Fact]
    public void SpeechApiFields_SaveAndMaskTheKey()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SpeechApiEndpoint = "http://localhost:8000/v1";
        viewModel.SpeechApiModel = "whisper-1";
        viewModel.SpeechApiKey = "sk-voice-1234567890";

        settings.SpeechApiEndpoint.Should().Be("http://localhost:8000/v1");
        settings.SpeechApiModel.Should().Be("whisper-1");
        settings.SpeechApiKey.Should().Be("sk-voice-1234567890");
        viewModel.MaskedSpeechApiKey.Should().NotBeEmpty().And.NotContain("1234567890");
    }

    [Fact]
    public void TalkShortcutOptions_OfferPresetsAndOff()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.TalkShortcutOptions.Select(option => option.Value)
            .Should().Equal("Ctrl+Alt+Space", "Ctrl+Shift+Space", "Ctrl+Alt+K", "Pause", "");
        viewModel.TalkShortcutOptions[^1].Label.Should().Be("Off");
        viewModel.SelectedTalkShortcut!.Value.Should().Be("Ctrl+Alt+Space");

        viewModel.SelectedTalkShortcut = viewModel.TalkShortcutOptions[^1];
        settings.TalkShortcut.Should().BeEmpty();

        viewModel.SelectedTalkShortcut = viewModel.TalkShortcutOptions.Single(option => option.Value == "Pause");
        settings.TalkShortcut.Should().Be("Pause");
    }

    [Fact]
    public void TalkShortcut_KeepsASavedCustomShortcut()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.TalkShortcut = "Ctrl+Alt+J";
        using var viewModel = new SettingsViewModel(settings);

        viewModel.TalkShortcutOptions.Select(option => option.Value)
            .Should().Equal("Ctrl+Alt+Space", "Ctrl+Shift+Space", "Ctrl+Alt+K", "Pause", "Ctrl+Alt+J", "");
        viewModel.SelectedTalkShortcut!.Label.Should().Be("Ctrl+Alt+J");
        settings.TalkShortcut.Should().Be("Ctrl+Alt+J");
    }

    [Theory]
    [InlineData("ctrl+shift+space", "Ctrl+Shift+Space")]
    [InlineData("", "")]
    public void TalkShortcut_MatchesSavedValuesToTheList(string saved, string expected)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.TalkShortcut = saved;
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SelectedTalkShortcut!.Value.Should().Be(expected);
        viewModel.TalkShortcutOptions.Should().HaveCount(5);
        settings.TalkShortcut.Should().Be(saved, "opening Settings does not rewrite the shortcut");
    }

    [Fact]
    public void SpokenRepliesOptions_WriteTheirValues()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SpokenRepliesOptions.Select(option => option.Value).Should().Equal("Off", "Voice", "All");
        viewModel.SpokenRepliesOptions.Select(option => option.Label)
            .Should().Equal("Off", "Replies to voice commands", "All replies");
        viewModel.SelectedSpokenReplies!.Value.Should().Be("Voice");

        viewModel.SelectedSpokenReplies = viewModel.SpokenRepliesOptions[2];
        settings.SpokenReplies.Should().Be("All");

        viewModel.SelectedSpokenReplies = viewModel.SpokenRepliesOptions[0];
        settings.SpokenReplies.Should().Be("Off");
    }

    [Fact]
    public void WakePhrase_SavesTheToggleAndThePhrase()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.WakeWordEnabled.Should().BeFalse();
        viewModel.WakeWord.Should().Be("Hey Kam");
        viewModel.WakeWordNote.Should().Contain("about 78 MB").And.Contain("Nothing is sent anywhere");

        viewModel.WakeWordEnabled = true;
        viewModel.WakeWord = "Merhaba Kam";

        settings.WakeWordEnabled.Should().BeTrue();
        settings.WakeWord.Should().Be("Merhaba Kam");
    }

    [Fact]
    public void Voices_ListAutomaticThenInstalledVoices()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);
        var speech = new FakeTextToSpeech();
        speech.Voices.Add(new SpeechVoiceInfo("tolga", "Tolga", "tr"));
        speech.Voices.Add(new SpeechVoiceInfo("zira", "Zira", "en"));
        speech.Voices.Add(new SpeechVoiceInfo("plain", "Plain", ""));

        viewModel.UseSpeechServices(null, speech);

        viewModel.CanUseSpeechOutput.Should().BeTrue();
        viewModel.HasSpeechOutputProblem.Should().BeFalse();
        viewModel.SpeechOutputProblem.Should().BeNull();
        viewModel.SpeechVoiceOptions.Select(option => option.Label)
            .Should().Equal("Automatic", "Tolga (tr)", "Zira (en)", "Plain");
        viewModel.SelectedSpeechVoice!.Value.Should().BeEmpty();

        viewModel.SelectedSpeechVoice = viewModel.SpeechVoiceOptions[1];
        settings.SpeechVoice.Should().Be("tolga");

        viewModel.SelectedSpeechVoice = viewModel.SpeechVoiceOptions[0];
        settings.SpeechVoice.Should().BeEmpty();
    }

    [Fact]
    public void Voices_KeepASavedVoiceThatIsNotInstalled()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.SpeechVoice = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices\Tokens\Gone";
        using var viewModel = new SettingsViewModel(settings);
        var speech = new FakeTextToSpeech();
        speech.Voices.Add(new SpeechVoiceInfo("zira", "Zira", "en"));

        viewModel.UseSpeechServices(null, speech);

        viewModel.SelectedSpeechVoice!.Label.Should().Be("Gone (not installed)");
        settings.SpeechVoice.Should().EndWith("Gone");
    }

    [Fact]
    public void Voices_WhenNoneAreInstalled_ExplainAndDisableTheControls()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.UseSpeechServices(null, new FakeTextToSpeech());

        viewModel.IsTextToSpeechAvailable.Should().BeTrue();
        viewModel.CanUseSpeechOutput.Should().BeFalse();
        viewModel.CanPreviewSpeech.Should().BeFalse();
        viewModel.SpeechOutputProblem.Should().Be("No voices are installed. Add one in your system's speech settings.");
    }

    [Theory]
    [InlineData("tr", "en-US", "tr")]
    [InlineData("en", "tr-TR", "en")]
    [InlineData("", "tr-TR", "tr")]
    public async Task Preview_ReadsASampleInTheSpokenLanguage(string voiceLanguage, string interfaceLanguage, string expectedLanguage)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        settings.VoiceLanguage = voiceLanguage;
        settings.Language = interfaceLanguage;
        using var viewModel = new SettingsViewModel(settings);
        var speech = new FakeTextToSpeech();
        speech.Voices.Add(new SpeechVoiceInfo("tolga", "Tolga", "tr"));
        viewModel.UseSpeechServices(null, speech);

        viewModel.EffectiveSpokenLanguage.Should().Be(expectedLanguage);
        await viewModel.PreviewSpeechAsync();

        speech.Spoken.Should().ContainSingle().Which.Should().Be(SettingsViewModel.GetSpeechPreviewSample(expectedLanguage));
        viewModel.IsPreviewingSpeech.Should().BeFalse();
    }

    [Fact]
    public void PreviewSample_IsTurkishOnlyForTurkish()
    {
        SettingsViewModel.GetSpeechPreviewSample("tr").Should().StartWith("Merhaba, ben Kam.");
        SettingsViewModel.GetSpeechPreviewSample("en").Should().StartWith("Hi, I'm Kam.");
        SettingsViewModel.GetSpeechPreviewSample("de").Should().StartWith("Hi, I'm Kam.");
    }

    [Fact]
    public void StopPreview_StopsReading()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);
        var speech = new FakeTextToSpeech();
        viewModel.UseSpeechServices(null, speech);

        viewModel.StopSpeechPreview();

        speech.StopCount.Should().Be(1);
    }

    [Theory]
    [InlineData(2, 2, "+2")]
    [InlineData(-3.4, -3, "-3")]
    [InlineData(9, 5, "+5")]
    [InlineData(-9, -5, "-5")]
    public void SpeechRate_IsClampedToWholeStepsAndSaved(double value, int expected, string expectedText)
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        viewModel.SpeechRateText.Should().Be("Normal");
        viewModel.SpeechRate = value;

        viewModel.SpeechRate.Should().Be(expected);
        viewModel.SpeechRateText.Should().Be(expectedText);
        settings.SpeechRate.Should().Be(expected);
    }

    [Fact]
    public void AudioErrors_AreShownInTheInterfaceLanguage()
    {
        using var settings = new JsonSettingsService(_settingsDirectory);
        using var viewModel = new SettingsViewModel(settings);

        if (!viewModel.IsAudioAvailable)
        {
            viewModel.AudioErrorMessage.Should().Be("Audio devices are not available on this computer.");
        }
        else if (viewModel.HasInputDevices && viewModel.HasOutputDevices)
        {
            viewModel.AudioErrorMessage.Should().BeNull();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_settingsDirectory))
        {
            Directory.Delete(_settingsDirectory, recursive: true);
        }
    }

    private sealed class FakeSpeechModelStore : ISpeechModelStore
    {
        public HashSet<string> Downloaded { get; } = new(StringComparer.Ordinal);

        public List<string> Requested { get; } = [];

        public Exception? Failure { get; set; }

        public TaskCompletionSource? Gate { get; init; }

        public string ModelsDirectory => Path.Combine(Path.GetTempPath(), "kam-fake-models");

        public string GetModelPath(string model) => Path.Combine(ModelsDirectory, $"ggml-{model}.bin");

        public bool IsDownloaded(string model) => Downloaded.Contains(model);

        public async Task<string> EnsureModelAsync(string model, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            Requested.Add(model);
            if (Gate is not null)
            {
                await Gate.Task;
            }

            progress?.Report(0.5);
            if (Failure is not null)
            {
                throw Failure;
            }

            Downloaded.Add(model);
            progress?.Report(1);
            return GetModelPath(model);
        }
    }

    private sealed class FakeTextToSpeech : ITextToSpeechService
    {
        public List<SpeechVoiceInfo> Voices { get; } = [];

        public List<string> Spoken { get; } = [];

        public int StopCount { get; private set; }

        public bool IsAvailable => true;

        public bool IsSpeaking => false;

        public IReadOnlyList<SpeechVoiceInfo> GetVoices() => Voices;

        public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
        {
            Spoken.Add(text);
            return Task.CompletedTask;
        }

        public void Stop() => StopCount++;

        public void Dispose()
        {
        }
    }
}

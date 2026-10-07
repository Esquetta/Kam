using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Services.Speech;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Speech;

public sealed class TextToSpeechServiceTests
{
    private static readonly SpeechVoiceInfo[] Voices =
    [
        new("en-zira", "Microsoft Zira", "en"),
        new("tr-tolga", "Microsoft Tolga", "tr"),
        new("other", "Unknown voice", "")
    ];

    [Fact]
    public void PickVoice_PrefersTheChosenVoiceWhenItSpeaksTheLanguage()
    {
        TextToSpeechService.PickVoice(Voices, "tr-tolga", "tr")!.Id.Should().Be("tr-tolga");
        TextToSpeechService.PickVoice(Voices, "other", "tr")!.Id.Should().Be("other");
    }

    [Fact]
    public void PickVoice_FallsBackToAVoiceInTheLanguage()
    {
        TextToSpeechService.PickVoice(Voices, "en-zira", "tr")!.Id.Should().Be("tr-tolga");
        TextToSpeechService.PickVoice(Voices, null, "en")!.Id.Should().Be("en-zira");
        TextToSpeechService.PickVoice(Voices, null, "de").Should().BeNull();
    }

    [Fact]
    public async Task SpeakAsync_ReadsSentencesWithSettingsVoiceAndRate()
    {
        var synthesizer = new RecordingSynthesizer();
        using var service = new TextToSpeechService(Configuration(new()
        {
            ["Voice:Language"] = "tr",
            ["Voice:SpeechRate"] = "3",
            ["Voice:OutputDeviceId"] = "speaker-1"
        }), NullLogger<TextToSpeechService>.Instance, synthesizer);

        await service.SpeakAsync("Merhaba. Dosyayı açtım.");

        synthesizer.Spoken.Should().Equal(
            ("Merhaba.", "tr-tolga", "tr", 3, "speaker-1"),
            ("Dosyayı açtım.", "tr-tolga", "tr", 3, "speaker-1"));
        service.IsSpeaking.Should().BeFalse();
    }

    [Fact]
    public async Task SpeakAsync_GuessesLanguageWhenSetToAuto()
    {
        var synthesizer = new RecordingSynthesizer();
        using var service = new TextToSpeechService(Configuration(new()), NullLogger<TextToSpeechService>.Instance, synthesizer);

        await service.SpeakAsync("Şimdi açıyorum.");

        synthesizer.Spoken.Single().Language.Should().Be("tr");
    }

    [Fact]
    public async Task SpeakAsync_DoesNothingWithoutASynthesizer()
    {
        using var service = new TextToSpeechService(Configuration(new()), NullLogger<TextToSpeechService>.Instance, synthesizer: null);

        service.IsAvailable.Should().BeFalse();
        await service.SpeakAsync("Hello.");
        service.GetVoices().Should().BeEmpty();
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class RecordingSynthesizer : ISpeechSynthesizer
    {
        public List<(string Text, string? Voice, string Language, int Rate, string? Device)> Spoken { get; } = [];

        public bool IsAvailable => true;

        public IReadOnlyList<SpeechVoiceInfo> GetVoices() => Voices;

        public Task SpeakAsync(string text, string? voiceId, string language, int rate, string? outputDeviceId, CancellationToken cancellationToken)
        {
            Spoken.Add((text, voiceId, language, rate, outputDeviceId));
            return Task.CompletedTask;
        }
    }
}

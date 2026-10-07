using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services;

public sealed class TranscriptCleanerTests
{
    [Theory]
    [InlineData("Altyazı M.K.")]
    [InlineData(" [Müzik] ")]
    [InlineData("(upbeat music)")]
    [InlineData("İzlediğiniz için teşekkür ederim.")]
    [InlineData("Thanks for watching!")]
    [InlineData("♪♪")]
    public void Clean_DropsWhatRecognitionInventsForSilence(string transcript)
    {
        TranscriptCleaner.Clean(transcript).Should().BeEmpty();
    }

    [Fact]
    public void Clean_KeepsSpeechAroundTags()
    {
        TranscriptCleaner.Clean("[Music] Spotify'ı   aç").Should().Be("Spotify'ı aç");
        TranscriptCleaner.Clean("Thank you, open the browser.").Should().Be("Thank you, open the browser.");
    }
}

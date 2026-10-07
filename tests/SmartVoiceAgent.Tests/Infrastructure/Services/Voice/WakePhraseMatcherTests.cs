using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services.Voice;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Voice;

public sealed class WakePhraseMatcherTests
{
    [Theory]
    [InlineData("Hey Kam")]
    [InlineData("Hey, Kam!")]
    [InlineData("hey cam")]
    [InlineData("Heykam.")]
    [InlineData("Hey Kâm")]
    [InlineData("Okay. Hey Kam")]
    public void Match_AcceptsHowRecognitionSpellsThePhrase(string transcript)
    {
        WakePhraseMatcher.Match(transcript, "Hey Kam").IsMatch.Should().BeTrue();
    }

    [Theory]
    [InlineData("What's the weather today")]
    [InlineData("I went to the camp yesterday and then hey kam")]
    [InlineData("")]
    public void Match_RejectsOtherSpeech(string transcript)
    {
        WakePhraseMatcher.Match(transcript, "Hey Kam").IsMatch.Should().BeFalse();
    }

    [Fact]
    public void Match_ReturnsTheWordsAfterThePhrase()
    {
        var match = WakePhraseMatcher.Match("Hey Kam, Spotify'ı aç ve müziği başlat.", "Hey Kam");

        match.IsMatch.Should().BeTrue();
        match.FollowingText.Should().Be("Spotify'ı aç ve müziği başlat.");
    }

    [Fact]
    public void Match_HandlesTurkishDottedAndDotlessI()
    {
        WakePhraseMatcher.Match("İZMİR", "izmir").IsMatch.Should().BeTrue();
        WakePhraseMatcher.Match("Kıvılcım", "KIVILCIM").IsMatch.Should().BeTrue();
    }

    [Fact]
    public void Key_FoldsAccentsAndSimilarLetters()
    {
        WakePhraseMatcher.Key("Çağrı").Should().Be("kagri");
        WakePhraseMatcher.Key("Hey,").Should().Be("hey");
    }
}

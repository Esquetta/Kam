using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Services.Speech;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Speech;

public sealed class SpeechTextFormatterTests
{
    [Fact]
    public void Format_ReadsProseAndSkipsCodeTablesAndLinkTargets()
    {
        const string reply = """
            ## Build fixed

            The release build failed because **two projects** pinned different versions of `Markdig`.

            - Updated the UI project
            - [x] Build passes

            ```powershell
            dotnet build Kam.sln -c Release
            ```

            | Check | Result |
            |---|---|
            | Build | 0 warnings |

            See the [release notes](https://github.com/Esquetta/Kam/releases) or https://example.com.
            """;

        var text = SpeechTextFormatter.Format(reply);

        text.Should().Be("Build fixed. The release build failed because two projects pinned different versions of Markdig. Updated the UI project. Build passes. See the release notes or");
    }

    [Fact]
    public void Format_ShortensLongTextAtASentence()
    {
        var reply = string.Join(" ", Enumerable.Repeat("This sentence is ten words long and ends right here.", 30));

        var text = SpeechTextFormatter.Format(reply, maxLength: 200);

        text.Length.Should().BeLessThanOrEqualTo(200);
        text.Should().EndWith(".");
    }

    [Fact]
    public void Format_ReturnsEmptyForCodeOnlyReplies()
    {
        SpeechTextFormatter.Format("```\nrm -rf build\n```").Should().BeEmpty();
        SpeechTextFormatter.Format(null).Should().BeEmpty();
    }

    [Fact]
    public void SplitSentences_SplitsAfterSentenceEnds()
    {
        SpeechTextFormatter.SplitSentences("Merhaba. Nasılsın? İyiyim!")
            .Should().Equal("Merhaba.", "Nasılsın?", "İyiyim!");
    }

    [Theory]
    [InlineData("Dosyayı açtım.", "tr")]
    [InlineData("I opened the file.", "en")]
    public void GuessLanguage_DetectsTurkishLetters(string text, string language)
    {
        SpeechTextFormatter.GuessLanguage(text).Should().Be(language);
    }

    [Theory]
    [InlineData("41F", "tr")]
    [InlineData("409;9", "en")]
    [InlineData("", "")]
    [InlineData("zz", "")]
    public void SapiLanguage_ReadsVoiceLanguage(string attribute, string language)
    {
        SapiLanguage.FromAttribute(attribute).Should().Be(language);
    }
}

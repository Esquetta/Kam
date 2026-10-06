using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SmartVoiceAgent.Infrastructure.Services.Voice;

namespace SmartVoiceAgent.Tests.Infrastructure.Services.Voice;

public sealed class VoiceSettingsTests
{
    [Fact]
    public void Read_UsesDefaultsWhenNothingIsSet()
    {
        var settings = VoiceSettings.Read(new ConfigurationBuilder().Build());

        settings.Language.Should().Be("auto");
        settings.SpeechEngine.Should().Be(VoiceSettings.LocalEngine);
        settings.LocalModel.Should().Be("base");
        settings.ApiEndpoint.Should().Be(VoiceSettings.DefaultApiEndpoint);
        settings.HasApi.Should().BeFalse();
        settings.WakeWord.Should().Be("Hey Kam");
    }

    [Fact]
    public void Read_ReadsEverySetting()
    {
        var settings = VoiceSettings.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Voice:Language"] = "tr-TR",
            ["Voice:SpeechEngine"] = "openai",
            ["Voice:LocalModel"] = "small",
            ["Voice:SpeechApi:Endpoint"] = " http://localhost:8000/v1 ",
            ["Voice:SpeechApi:Model"] = "whisper-large-v3",
            ["Voice:InputDeviceId"] = "mic-1",
            ["Voice:WakeWord"] = "Jarvis",
            ["Voice:SpeechRate"] = "9",
            ["Whisper:ModelPath"] = "/models/custom.bin"
        }).Build());

        settings.Language.Should().Be("tr");
        settings.UsesApi.Should().BeTrue();
        settings.LocalModel.Should().Be("small");
        settings.ApiEndpoint.Should().Be("http://localhost:8000/v1");
        settings.ApiModel.Should().Be("whisper-large-v3");
        settings.HasApi.Should().BeTrue("an endpoint without a key is a local server");
        settings.InputDeviceId.Should().Be("mic-1");
        settings.WakeWord.Should().Be("Jarvis");
        settings.SpeechRate.Should().Be(5);
        settings.LocalModelPath.Should().Be("/models/custom.bin");
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("AUTO", "auto")]
    [InlineData("TR", "tr")]
    [InlineData("en_US", "en")]
    [InlineData("not-a-language", "auto")]
    public void NormalizeLanguage_ReturnsTwoLetterCodes(string? language, string expected)
    {
        VoiceSettings.NormalizeLanguage(language).Should().Be(expected);
    }
}

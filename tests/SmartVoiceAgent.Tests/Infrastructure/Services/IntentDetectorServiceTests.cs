using FluentAssertions;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services
{
    public class IntentDetectorServiceTests
    {
        private readonly IntentDetectorService _intentDetectorService;

        public IntentDetectorServiceTests()
        {
            _intentDetectorService = new IntentDetectorService();
        }

        [Theory]
        [InlineData("play music", CommandType.PlayMusic)]
        [InlineData("search google", CommandType.SearchWeb)]
        [InlineData("turn off lights", CommandType.ControlDevice)]
        [InlineData("open chrome", CommandType.OpenApplication)]
        public async Task DetectIntentAsync_DefaultConfiguration_ReturnsExpectedCommandType(string input, CommandType expected)
        {
            var result = await _intentDetectorService.DetectIntentAsync(input, "en");

            result.Intent.Should().Be(expected);
        }

        [Fact]
        public async Task DetectIntentAsync_EmptyInput_ReturnsUnknown()
        {
            var result = await _intentDetectorService.DetectIntentAsync("", "tr");

            result.Intent.Should().Be(CommandType.Unknown);
        }
    }
}

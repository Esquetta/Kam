using FluentAssertions;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Infrastructure.Services;

namespace SmartVoiceAgent.Tests.Infrastructure.Services
{
    public class IntentDetectorServiceTests
    {
        private readonly IntentDetectorService intentDetectorService;

        public IntentDetectorServiceTests()
        {
            this.intentDetectorService = new IntentDetectorService();
        }

        [Theory]
        [InlineData("müzik çal", CommandType.PlayMusic)]
        [InlineData("google ara", CommandType.SearchWeb)]
        [InlineData("ışıkları kapat", CommandType.ControlDevice)]
        public async Task DetectIntentAsync_Returns_CorrectCommandType(string input, CommandType expected)
        {
            var result = await intentDetectorService.DetectIntentAsync(input,"tr");

            result.Should().Be(expected);
        }
    }
}

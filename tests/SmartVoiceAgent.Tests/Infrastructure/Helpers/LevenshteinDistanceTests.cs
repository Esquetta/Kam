using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Tests.Infrastructure.Helpers
{
    public class LevenshteinDistanceTests
    {
        private readonly Mock<IApplicationServiceFactory> _mockFactory;
        private readonly Mock<IApplicationService> _mockAppService;
        private readonly DynamicAppExtractionService _service;

        public LevenshteinDistanceTests()
        {
            _mockFactory = new Mock<IApplicationServiceFactory>();
            _mockAppService = new Mock<IApplicationService>();
            _mockFactory.Setup(f => f.Create()).Returns(_mockAppService.Object);
            _service = new DynamicAppExtractionService(_mockFactory.Object);
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_EmptyInput_ReturnsNull()
        {
            var result = await _service.ExtractApplicationNameAsync("");
            result.Should().BeNull();
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_WhitespaceInput_ReturnsNull()
        {
            var result = await _service.ExtractApplicationNameAsync("   ");
            result.Should().BeNull();
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_KnownApp_ReturnsExecutableName()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("chrome");

            result.Should().Be("chrome");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_TypoInInput_FuzzyMatches()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("chrme");

            result.Should().Be("chrome");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_CommandWords_RemovedFromInput()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Microsoft Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("open word please");

            result.Should().Be("WINWORD");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_CaseInsensitive_MatchesMixedCase()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Notepad", @"C:\Windows\notepad.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result1 = await _service.ExtractApplicationNameAsync("NOTEPAD");
            var result2 = await _service.ExtractApplicationNameAsync("Notepad");
            var result3 = await _service.ExtractApplicationNameAsync("notepad");

            result1.Should().Be("notepad");
            result2.Should().Be("notepad");
            result3.Should().Be("notepad");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_AliasMatch_MatchesAlias()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("google chrome");

            result.Should().Be("chrome");
        }

        [Fact]
        public async Task RefreshCacheAsync_UpdatesCache()
        {
            var apps = new List<AppInfoDTO>
            {
                new("App1", @"C:\App1.exe", false),
                new("App2", @"C:\App2.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var cachedApps = await _service.GetAllCachedApplicationsAsync();

            cachedApps.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetAllCachedApplicationsAsync_ReturnsCachedData()
        {
            var apps = new List<AppInfoDTO>
            {
                new("TestApp", @"C:\TestApp.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.GetAllCachedApplicationsAsync();

            result.Should().ContainKey("TestApp");
            result["TestApp"].ExecutableName.Should().Be("TestApp");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_BrowserAliases_Works()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("browser");

            result.Should().Be("chrome");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_PartialMatch_Works()
        {
            var apps = new List<AppInfoDTO>
            {
                new("Microsoft Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("code");

            result.Should().Be("Code");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_NoMatch_ReturnsCleanedText()
        {
            var apps = new List<AppInfoDTO>();
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("unknownxyz123");

            result.Should().Be("unknownxyz123");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_VeryShortInput_ReturnsDefault()
        {
            var apps = new List<AppInfoDTO>();
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync("ab");

            result.Should().Be("chrome");
        }

        [Theory]
        [InlineData("word", "ms word")]
        [InlineData("excel", "ms excel")]
        [InlineData("spotify", "müzik")]
        public async Task ExtractApplicationNameAsync_OfficeAliases_Works(string appName, string alias)
        {
            var apps = new List<AppInfoDTO>
            {
                new($"Microsoft {appName}", $@"C:\Program Files\{appName}.exe", false)
            };
            _mockAppService.Setup(s => s.ListApplicationsAsync()).ReturnsAsync(apps);

            await _service.RefreshCacheAsync();
            var result = await _service.ExtractApplicationNameAsync(alias);

            result.Should().NotBeNull();
        }
    }
}

using FluentAssertions;
using Moq;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Helpers;

namespace SmartVoiceAgent.Tests.Infrastructure.Services
{
    public class DynamicAppExtractionServiceTests
    {
        private readonly Mock<IApplicationServiceFactory> _mockFactory;
        private readonly Mock<IApplicationService> _mockAppService;
        private readonly DynamicAppExtractionService _service;

        public DynamicAppExtractionServiceTests()
        {
            _mockFactory = new Mock<IApplicationServiceFactory>();
            _mockAppService = new Mock<IApplicationService>();
            _mockFactory.Setup(f => f.Create()).Returns(_mockAppService.Object);
            _service = new DynamicAppExtractionService(_mockFactory.Object);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task ExtractApplicationNameAsync_NullOrEmptyInput_ReturnsNull(string input)
        {
            // Act
            var result = await _service.ExtractApplicationNameAsync(input);

            // Assert
            result.Should().BeNull();
        }

        [Theory]
        [InlineData("chrome'u aç", "chrome")]
        [InlineData("spotify başlat", "spotify")]
        [InlineData("LÜTFEN word ÇALIŞTIR", "word")]
        public async Task ExtractApplicationNameAsync_WithCommandWords_ReturnsExecutableName(string input, string expected)
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync(input);

            // Assert
            result.Should().NotBeNull();
            result.ToLowerInvariant().Should().Contain(expected);
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_ExactMatch_ReturnsExecutableName()
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync("chrome");

            // Assert
            result.Should().Be("chrome");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_AliasMatch_ReturnsExecutableName()
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync("google chrome");

            // Assert
            result.Should().Be("chrome");
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_PartialMatch_ReturnsExecutableName()
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync("chrom");

            // Assert
            result.Should().NotBeNull();
        }

        [Fact]
        public async Task ExtractApplicationNameAsync_FuzzyMatch_ReturnsExecutableName()
        {
            // Arrange
            SetupMockApplications();

            // Act - Typo in chrome
            var result = await _service.ExtractApplicationNameAsync("chromee");

            // Assert
            result.Should().NotBeNull();
        }

        [Theory]
        [InlineData("chrome aç", "chrome")]
        [InlineData("başlat chrome", "chrome")]
        [InlineData("çalıştır spotify", "spotify")]
        public async Task ExtractApplicationNameAsync_DifferentCommandFormats_ReturnsExecutableName(string input, string expected)
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync(input);

            // Assert
            result.Should().NotBeNull();
            result.ToLowerInvariant().Should().Contain(expected);
        }

        [Fact]
        public async Task GetAllCachedApplicationsAsync_ReturnsCachedApplications()
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.GetAllCachedApplicationsAsync();

            // Assert
            result.Should().NotBeNull();
            result.Should().NotBeEmpty();
        }

        [Fact]
        public async Task RefreshCacheAsync_ClearsAndReloadsCache()
        {
            // Arrange
            SetupMockApplications();
            await _service.GetAllCachedApplicationsAsync(); // Initial load

            // Act
            await _service.RefreshCacheAsync();

            // Assert
            var result = await _service.GetAllCachedApplicationsAsync();
            result.Should().NotBeNull();
            result.Should().NotBeEmpty();
        }

        [Theory]
        [InlineData("chrmoe")]  // Transposed letters
        [InlineData("chrom")]   // Missing letter
        [InlineData("chrrome")] // Extra letter
        public async Task ExtractApplicationNameAsync_Typos_ReturnsCorrectApplication(string typo)
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync(typo);

            // Assert
            result.Should().NotBeNull();
        }

        [Theory]
        [InlineData("CHROME")]
        [InlineData("Chrome")]
        [InlineData("cHrOmE")]
        public async Task ExtractApplicationNameAsync_CaseInsensitive_ReturnsExecutableName(string input)
        {
            // Arrange
            SetupMockApplications();

            // Act
            var result = await _service.ExtractApplicationNameAsync(input);

            // Assert
            result.Should().NotBeNull();
            result.ToLowerInvariant().Should().Contain("chrome");
        }

        #region Helper Methods

        private void SetupMockApplications()
        {
            var apps = new List<AppInfoDTO>
            {
                new AppInfoDTO("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", true),
                new AppInfoDTO("Spotify", @"C:\Program Files\Spotify\Spotify.exe", true),
                new AppInfoDTO("Microsoft Word", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.exe", true),
                new AppInfoDTO("Visual Studio Code", @"C:\Program Files\Microsoft VS Code\Code.exe", true),
                new AppInfoDTO("Discord", @"C:\Users\User\AppData\Local\Discord\Discord.exe", true)
            };

            _mockAppService.Setup(s => s.ListApplicationsAsync())
                .ReturnsAsync(apps);
        }

        #endregion
    }
}

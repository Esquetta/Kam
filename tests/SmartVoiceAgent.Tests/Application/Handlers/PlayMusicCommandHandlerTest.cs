using FluentAssertions;
using MediatR;
using Moq;
using SmartVoiceAgent.Application.Handlers;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;

namespace SmartVoiceAgent.Tests.Application.Commands
{
    /// <summary>
    /// Unit tests for PlayMusicCommand and its handler
    /// </summary>
    public class PlayMusicCommandTests
    {
        private readonly Mock<IMusicService> _musicServiceMock;
        private readonly Mock<IMediator> _mediatorMock;
        private readonly PlayMusicCommandHandler _handler;

        public PlayMusicCommandTests()
        {
            _musicServiceMock = new Mock<IMusicService>();
            _mediatorMock = new Mock<IMediator>();
            _handler = new PlayMusicCommandHandler(_musicServiceMock.Object, _mediatorMock.Object);
        }

        [Fact]
        public async Task Handle_ValidTrack_Should_PlayMusic_And_Return_Success()
        {
            // Arrange
            var trackName = "metallica";
            var command = new PlayMusicCommand(trackName);

            _musicServiceMock
                .Setup(m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            _mediatorMock
                .Setup(m => m.Publish(It.IsAny<MusicPlayedNotification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.Message.Should().Contain(trackName);

            _musicServiceMock.Verify(
                m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()),
                Times.Once);

            _mediatorMock.Verify(
                m => m.Publish(
                    It.Is<MusicPlayedNotification>(n => n.SongName == trackName),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_With_CancellationToken_Should_Pass_To_Services()
        {
            // Arrange
            var trackName = "test-song";
            var command = new PlayMusicCommand(trackName);
            using var cts = new CancellationTokenSource();

            _musicServiceMock
                .Setup(m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            await _handler.Handle(command, cts.Token);

            // Assert - Verify the method was called with any cancellation token
            _musicServiceMock.Verify(
                m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_When_MusicService_Throws_Should_Propagate_Exception()
        {
            // Arrange
            var trackName = "invalid-track";
            var command = new PlayMusicCommand(trackName);
            var expectedException = new InvalidOperationException("Music playback failed");

            _musicServiceMock
                .Setup(m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            // Act & Assert
            Func<Task> act = async () => await _handler.Handle(command, CancellationToken.None);
            await act.Should().ThrowAsync<InvalidOperationException>();

            _mediatorMock.Verify(
                m => m.Publish(It.IsAny<MusicPlayedNotification>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public void PlayMusicCommand_Should_Implement_Caching_Properties()
        {
            // Arrange
            var trackName = "test-track";
            var command = new PlayMusicCommand(trackName);

            // Assert
            command.CacheKey.Should().Be($"PlayMusic-{trackName}");
            command.CacheGroupKey.Should().Be("MusicCommands");
            command.BypassCache.Should().BeFalse();
            command.SlidingExpiration.Should().Be(TimeSpan.FromMinutes(10));
        }

        [Fact]
        public void PlayMusicCommand_Should_Implement_Performance_Properties()
        {
            // Arrange
            var command = new PlayMusicCommand("test-track");

            // Assert
            command.EnablePerformanceLogging.Should().BeTrue();
            command.Interval.Should().Be(3);
        }

        [Theory]
        [InlineData("song1")]
        [InlineData("song with spaces")]
        [InlineData("Song-With-Dashes")]
        [InlineData("Song_With_Underscores")]
        public async Task Handle_Various_Track_Names_Should_Work_Correctly(string trackName)
        {
            // Arrange
            var command = new PlayMusicCommand(trackName);

            _musicServiceMock
                .Setup(m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            _musicServiceMock.Verify(
                m => m.PlayMusicAsync(trackName, false, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task Handle_Empty_TrackName_Should_Still_Call_Service()
        {
            // Arrange
            var command = new PlayMusicCommand(string.Empty);

            _musicServiceMock
                .Setup(m => m.PlayMusicAsync(string.Empty, false, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
        }
    }

    /// <summary>
    /// Integration tests for PlayMusicCommand (requires actual music service)
    /// </summary>
    public class PlayMusicCommandIntegrationTests : IDisposable
    {
        private readonly IMusicService _musicService;
        private readonly string _testAudioPath;

        public PlayMusicCommandIntegrationTests()
        {
            var factory = new SmartVoiceAgent.Infrastructure.Factories.MusicServiceFactory();
            _musicService = factory.Create();
            
            // Create a path to a test audio file (won't exist, but tests the flow)
            _testAudioPath = Path.Combine(Path.GetTempPath(), "test_music.mp3");
        }

        public void Dispose()
        {
            (_musicService as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task Integration_PlayMusic_With_Invalid_File_Should_Throw()
        {
            // Arrange - Use a non-existent file
            var invalidPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.mp3");

            // Act & Assert
            Func<Task> act = async () => await _musicService.PlayMusicAsync(invalidPath);
            await act.Should().ThrowAsync<Exception>();
        }

        [Fact]
        public async Task Integration_StopMusic_When_Not_Playing_Should_Not_Throw()
        {
            // Act & Assert
            Func<Task> act = async () => await _musicService.StopMusicAsync();
            await act.Should().NotThrowAsync();
        }

        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.25f)]
        [InlineData(0.5f)]
        [InlineData(0.75f)]
        [InlineData(1.0f)]
        public async Task Integration_SetVolume_With_Valid_Values_Should_Not_Throw(float volume)
        {
            // Act & Assert
            Func<Task> act = async () => await _musicService.SetVolumeAsync(volume);
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task Integration_Full_Lifecycle_Without_Playback()
        {
            // This test verifies the service lifecycle without actually playing audio
            // Arrange
            var service = _musicService;

            // Act - Test all operations when not playing
            await service.SetVolumeAsync(0.5f);
            await service.PauseMusicAsync();
            await service.ResumeMusicAsync();
            await service.StopMusicAsync();

            // Assert - No exceptions should occur
            service.Should().NotBeNull();
        }
    }
}

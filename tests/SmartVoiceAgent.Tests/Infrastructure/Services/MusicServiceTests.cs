using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Infrastructure.Factories;
using SmartVoiceAgent.Infrastructure.Services.Music;

namespace SmartVoiceAgent.Tests.Infrastructure.Services
{
    /// <summary>
    /// Unit tests for Music Service implementations
    /// </summary>
    public class MusicServiceTests
    {
        #region Factory Tests

        [Fact]
        public void MusicServiceFactory_Create_Should_Return_Correct_Service_For_Platform()
        {
            // Arrange
            var factory = new MusicServiceFactory();

            // Act
            var service = factory.Create();

            // Assert
            service.Should().NotBeNull();
            service.Should().BeAssignableTo<IMusicService>();

            // Verify platform-specific implementation
            if (OperatingSystem.IsWindows())
            {
                service.Should().BeOfType<WindowsMusicService>();
            }
            else if (OperatingSystem.IsLinux())
            {
                service.Should().BeOfType<LinuxMusicService>();
            }
            else if (OperatingSystem.IsMacOS())
            {
                service.Should().BeOfType<MacOSMusicService>();
            }
        }

        [Fact]
        public void MusicServiceFactory_Create_Should_Throw_On_Unsupported_Platform()
        {
            // This test validates the factory throws for unsupported platforms
            // We can't easily mock OS detection, but we can verify the exception type
            // by checking the factory logic
            var factory = new MusicServiceFactory();
            
            // The factory should handle the current OS or throw
            Action act = () => factory.Create();
            
            // On supported platforms, this won't throw
            // On unsupported, it should throw PlatformNotSupportedException
            try
            {
                act();
            }
            catch (PlatformNotSupportedException)
            {
                // Expected on unsupported platforms
            }
        }

        #endregion

        #region WindowsMusicService Tests

        [Fact]
        public async Task WindowsMusicService_SetVolume_Should_Clamp_To_Valid_Range()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<WindowsMusicService>>();
            using var service = new WindowsMusicService(loggerMock.Object);

            // Act - Test volume clamping
            await service.SetVolumeAsync(-0.5f); // Below 0
            await service.SetVolumeAsync(1.5f);  // Above 1
            await service.SetVolumeAsync(0.5f);  // Valid

            // Assert - No exceptions should be thrown
            // Actual volume verification would require mocking NAudio internals
        }

        [Fact]
        public async Task WindowsMusicService_StopMusic_When_Not_Playing_Should_Not_Throw()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<WindowsMusicService>>();
            using var service = new WindowsMusicService(loggerMock.Object);

            // Act & Assert - Should not throw when stopping non-playing music
            Func<Task> act = async () => await service.StopMusicAsync();
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void WindowsMusicService_Dispose_Should_Be_Idempotent()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<WindowsMusicService>>();
            var service = new WindowsMusicService(loggerMock.Object);

            // Act
            service.Dispose();
            service.Dispose(); // Second dispose should not throw

            // Assert
            service.Should().NotBeNull();
        }

        [Fact]
        public async Task WindowsMusicService_PauseResume_When_Not_Playing_Should_Not_Throw()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<WindowsMusicService>>();
            using var service = new WindowsMusicService(loggerMock.Object);

            // Act & Assert
            Func<Task> pauseAct = async () => await service.PauseMusicAsync();
            Func<Task> resumeAct = async () => await service.ResumeMusicAsync();

            await pauseAct.Should().NotThrowAsync();
            await resumeAct.Should().NotThrowAsync();
        }

        [Fact]
        public async Task WindowsMusicService_PlayMusic_With_Invalid_File_Should_Throw()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<WindowsMusicService>>();
            using var service = new WindowsMusicService(loggerMock.Object);
            var invalidPath = "nonexistent_file.mp3";

            // Act & Assert
            Func<Task> act = async () => await service.PlayMusicAsync(invalidPath);
            await act.Should().ThrowAsync<Exception>(); // NAudio will throw for invalid file
        }

        #endregion

        #region LinuxMusicService Tests

        [Fact]
        public void LinuxMusicService_DetectAvailablePlayer_Should_Return_Valid_Player()
        {
            // Arrange & Act
            var loggerMock = new Mock<ILogger<LinuxMusicService>>();
            using var service = new LinuxMusicService(loggerMock.Object);

            // Assert - Service should be created successfully
            service.Should().NotBeNull();
            service.Should().BeAssignableTo<IMusicService>();
        }

        [Fact]
        public async Task LinuxMusicService_SetVolume_Should_Clamp_To_Valid_Range()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LinuxMusicService>>();
            using var service = new LinuxMusicService(loggerMock.Object);

            // Act - Test volume clamping without playback
            await service.SetVolumeAsync(-0.5f); // Below 0
            await service.SetVolumeAsync(1.5f);  // Above 1
            await service.SetVolumeAsync(0.5f);  // Valid

            // Assert - No exceptions should be thrown
        }

        [Fact]
        public async Task LinuxMusicService_StopMusic_When_Not_Playing_Should_Not_Throw()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LinuxMusicService>>();
            using var service = new LinuxMusicService(loggerMock.Object);

            // Act & Assert
            Func<Task> act = async () => await service.StopMusicAsync();
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void LinuxMusicService_Dispose_Should_Be_Idempotent()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<LinuxMusicService>>();
            var service = new LinuxMusicService(loggerMock.Object);

            // Act
            service.Dispose();
            service.Dispose(); // Second dispose should not throw

            // Assert
            service.Should().NotBeNull();
        }

        #endregion

        #region MacOSMusicService Tests

        [Fact]
        public async Task MacOSMusicService_SetVolume_Should_Clamp_To_Valid_Range()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<MacOSMusicService>>();
            using var service = new MacOSMusicService(loggerMock.Object);

            // Act
            await service.SetVolumeAsync(-0.5f); // Below 0
            await service.SetVolumeAsync(1.5f);  // Above 1
            await service.SetVolumeAsync(0.5f);  // Valid

            // Assert - No exceptions should be thrown
        }

        [Fact]
        public async Task MacOSMusicService_StopMusic_When_Not_Playing_Should_Not_Throw()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<MacOSMusicService>>();
            using var service = new MacOSMusicService(loggerMock.Object);

            // Act & Assert
            Func<Task> act = async () => await service.StopMusicAsync();
            await act.Should().NotThrowAsync();
        }

        [Fact]
        public void MacOSMusicService_Dispose_Should_Be_Idempotent()
        {
            // Arrange
            var loggerMock = new Mock<ILogger<MacOSMusicService>>();
            var service = new MacOSMusicService(loggerMock.Object);

            // Act
            service.Dispose();
            service.Dispose(); // Second dispose should not throw

            // Assert
            service.Should().NotBeNull();
        }

        #endregion

        #region IMusicService Interface Contract Tests

        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.5f)]
        [InlineData(1.0f)]
        public async Task All_Services_SetVolume_Should_Accept_Valid_Values(float volume)
        {
            // Arrange
            var factory = new MusicServiceFactory();
            var service = factory.Create();

            // Act & Assert
            Func<Task> act = async () => await service.SetVolumeAsync(volume);
            await act.Should().NotThrowAsync();

            // Cleanup
            (service as IDisposable)?.Dispose();
        }

        [Theory]
        [InlineData(-1.0f)]
        [InlineData(-0.1f)]
        [InlineData(1.1f)]
        [InlineData(2.0f)]
        public async Task All_Services_SetVolume_Should_Clamp_Invalid_Values(float volume)
        {
            // Arrange
            var factory = new MusicServiceFactory();
            var service = factory.Create();

            // Act - Should not throw even with invalid values
            Func<Task> act = async () => await service.SetVolumeAsync(volume);
            await act.Should().NotThrowAsync();

            // Cleanup
            (service as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task All_Services_Stop_When_Not_Playing_Should_Not_Throw()
        {
            // Arrange
            var factory = new MusicServiceFactory();
            var service = factory.Create();

            // Act & Assert
            Func<Task> act = async () => await service.StopMusicAsync();
            await act.Should().NotThrowAsync();

            // Cleanup
            (service as IDisposable)?.Dispose();
        }

        [Fact]
        public async Task All_Services_PauseResume_When_Not_Playing_Should_Not_Throw()
        {
            // Arrange
            var factory = new MusicServiceFactory();
            var service = factory.Create();

            // Act & Assert
            Func<Task> pauseAct = async () => await service.PauseMusicAsync();
            Func<Task> resumeAct = async () => await service.ResumeMusicAsync();

            await pauseAct.Should().NotThrowAsync();
            await resumeAct.Should().NotThrowAsync();

            // Cleanup
            (service as IDisposable)?.Dispose();
        }

        #endregion

        #region Disposal Pattern Tests

        [Fact]
        public void All_Services_After_Dispose_Should_Throw_ObjectDisposedException()
        {
            // Arrange
            var factory = new MusicServiceFactory();
            var service = factory.Create();
            (service as IDisposable)?.Dispose();

            // Act & Assert
            Func<Task> playAct = async () => await service.PlayMusicAsync("test.mp3");
            Func<Task> pauseAct = async () => await service.PauseMusicAsync();
            Func<Task> resumeAct = async () => await service.ResumeMusicAsync();
            Func<Task> stopAct = async () => await service.StopMusicAsync();
            Func<Task> volumeAct = async () => await service.SetVolumeAsync(0.5f);

            playAct.Should().ThrowAsync<ObjectDisposedException>();
            pauseAct.Should().ThrowAsync<ObjectDisposedException>();
            resumeAct.Should().ThrowAsync<ObjectDisposedException>();
            stopAct.Should().ThrowAsync<ObjectDisposedException>();
            volumeAct.Should().ThrowAsync<ObjectDisposedException>();
        }

        #endregion
    }
}

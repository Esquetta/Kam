using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using SmartVoiceAgent.Application.Commands;
using SmartVoiceAgent.Application.Handlers.CommandHandlers;
using SmartVoiceAgent.Application.Notifications;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;

namespace SmartVoiceAgent.Tests.Application.Handlers.CommandHandlers
{
    public class ControlDeviceCommandHandlerTests
    {
        private readonly Mock<IMediator> _mediatorMock;
        private readonly Mock<ISystemControlServiceFactory> _factoryMock;
        private readonly Mock<ISystemControlService> _systemControlMock;
        private readonly Mock<ILogger<ControlDeviceCommandHandler>> _loggerMock;
        private readonly ControlDeviceCommandHandler _handler;

        public ControlDeviceCommandHandlerTests()
        {
            _mediatorMock = new Mock<IMediator>();
            _factoryMock = new Mock<ISystemControlServiceFactory>();
            _systemControlMock = new Mock<ISystemControlService>();
            _loggerMock = new Mock<ILogger<ControlDeviceCommandHandler>>();

            _factoryMock.Setup(f => f.CreateSystemService())
                .Returns(_systemControlMock.Object);

            _handler = new ControlDeviceCommandHandler(
                _mediatorMock.Object,
                _factoryMock.Object,
                _loggerMock.Object);
        }

        #region Volume Control Tests

        [Theory]
        [InlineData("increase", "Volume increased to 60%")]
        [InlineData("up", "Volume increased to 60%")]
        [InlineData("artır", "Volume increased to 60%")]
        public async Task Handle_VolumeIncrease_ReturnsSuccess(string action, string expectedMessage)
        {
            // Arrange
            _systemControlMock.Setup(s => s.IncreaseSystemVolumeAsync(10))
                .ReturnsAsync(true);
            _systemControlMock.Setup(s => s.GetSystemVolumeAsync())
                .ReturnsAsync(60);

            var command = new ControlDeviceCommand("volume", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain(expectedMessage);
            _systemControlMock.Verify(s => s.IncreaseSystemVolumeAsync(10), Times.Once);
        }

        [Theory]
        [InlineData("decrease", "Volume decreased to 40%")]
        [InlineData("down", "Volume decreased to 40%")]
        [InlineData("azalt", "Volume decreased to 40%")]
        public async Task Handle_VolumeDecrease_ReturnsSuccess(string action, string expectedMessage)
        {
            // Arrange
            _systemControlMock.Setup(s => s.DecreaseSystemVolumeAsync(10))
                .ReturnsAsync(true);
            _systemControlMock.Setup(s => s.GetSystemVolumeAsync())
                .ReturnsAsync(40);

            var command = new ControlDeviceCommand("volume", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain(expectedMessage);
            _systemControlMock.Verify(s => s.DecreaseSystemVolumeAsync(10), Times.Once);
        }

        [Theory]
        [InlineData("mute", "Volume muted")]
        [InlineData("sessiz", "Volume muted")]
        public async Task Handle_VolumeMute_ReturnsSuccess(string action, string expectedMessage)
        {
            // Arrange
            _systemControlMock.Setup(s => s.MuteSystemVolumeAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("volume", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Be(expectedMessage);
            _systemControlMock.Verify(s => s.MuteSystemVolumeAsync(), Times.Once);
        }

        [Theory]
        [InlineData("unmute", "Volume unmuted")]
        [InlineData("sesli", "Volume unmuted")]
        public async Task Handle_VolumeUnmute_ReturnsSuccess(string action, string expectedMessage)
        {
            // Arrange
            _systemControlMock.Setup(s => s.UnmuteSystemVolumeAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("volume", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Be(expectedMessage);
            _systemControlMock.Verify(s => s.UnmuteSystemVolumeAsync(), Times.Once);
        }

        #endregion

        #region Brightness Control Tests

        [Theory]
        [InlineData("increase")]
        [InlineData("up")]
        [InlineData("artır")]
        public async Task Handle_BrightnessIncrease_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.IncreaseScreenBrightnessAsync(10))
                .ReturnsAsync(true);
            _systemControlMock.Setup(s => s.GetScreenBrightnessAsync())
                .ReturnsAsync(70);

            var command = new ControlDeviceCommand("brightness", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("brightness increased");
            _systemControlMock.Verify(s => s.IncreaseScreenBrightnessAsync(10), Times.Once);
        }

        [Theory]
        [InlineData("decrease")]
        [InlineData("down")]
        [InlineData("azalt")]
        public async Task Handle_BrightnessDecrease_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.DecreaseScreenBrightnessAsync(10))
                .ReturnsAsync(true);
            _systemControlMock.Setup(s => s.GetScreenBrightnessAsync())
                .ReturnsAsync(50);

            var command = new ControlDeviceCommand("brightness", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("brightness decreased");
            _systemControlMock.Verify(s => s.DecreaseScreenBrightnessAsync(10), Times.Once);
        }

        [Theory]
        [InlineData("screen")]
        [InlineData("parlaklık")]
        [InlineData("ekran")]
        public async Task Handle_BrightnessAlias_ReturnsSuccess(string deviceName)
        {
            // Arrange
            _systemControlMock.Setup(s => s.IncreaseScreenBrightnessAsync(10))
                .ReturnsAsync(true);
            _systemControlMock.Setup(s => s.GetScreenBrightnessAsync())
                .ReturnsAsync(70);

            var command = new ControlDeviceCommand(deviceName, "increase");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
        }

        #endregion

        #region WiFi Control Tests

        [Theory]
        [InlineData("on")]
        [InlineData("enable")]
        [InlineData("aç")]
        public async Task Handle_WiFiEnable_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.EnableWiFiAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("wifi", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("enabled");
            _systemControlMock.Verify(s => s.EnableWiFiAsync(), Times.Once);
        }

        [Theory]
        [InlineData("off")]
        [InlineData("disable")]
        [InlineData("kapat")]
        public async Task Handle_WiFiDisable_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.DisableWiFiAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("wifi", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("disabled");
            _systemControlMock.Verify(s => s.DisableWiFiAsync(), Times.Once);
        }

        [Theory]
        [InlineData("status", true, "enabled")]
        [InlineData("durum", false, "disabled")]
        public async Task Handle_WiFiStatus_ReturnsExpectedStatus(string action, bool isEnabled, string expectedStatus)
        {
            // Arrange
            _systemControlMock.Setup(s => s.GetWiFiStatusAsync())
                .ReturnsAsync(isEnabled);

            var command = new ControlDeviceCommand("wifi", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain(expectedStatus);
        }

        #endregion

        #region Bluetooth Control Tests

        [Theory]
        [InlineData("on")]
        [InlineData("enable")]
        [InlineData("aç")]
        public async Task Handle_BluetoothEnable_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.EnableBluetoothAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("bluetooth", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("enabled");
        }

        [Theory]
        [InlineData("off")]
        [InlineData("disable")]
        [InlineData("kapat")]
        public async Task Handle_BluetoothDisable_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.DisableBluetoothAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("bluetooth", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("disabled");
        }

        [Theory]
        [InlineData("status", true, "enabled")]
        [InlineData("durum", false, "disabled")]
        public async Task Handle_BluetoothStatus_ReturnsExpectedStatus(string action, bool isEnabled, string expectedStatus)
        {
            // Arrange
            _systemControlMock.Setup(s => s.GetBluetoothStatusAsync())
                .ReturnsAsync(isEnabled);

            var command = new ControlDeviceCommand("bluetooth", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain(expectedStatus);
        }

        #endregion

        #region Power Control Tests

        [Theory]
        [InlineData("shutdown")]
        [InlineData("kapat")]
        public async Task Handle_PowerShutdown_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.ShutdownSystemAsync(0))
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("power", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("shutting down");
        }

        [Theory]
        [InlineData("restart")]
        [InlineData("reboot")]
        [InlineData("yeniden başlat")]
        public async Task Handle_PowerRestart_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.RestartSystemAsync(0))
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("power", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("restarting");
        }

        [Theory]
        [InlineData("sleep")]
        [InlineData("suspend")]
        [InlineData("uyku")]
        public async Task Handle_PowerSleep_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.SleepSystemAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("power", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("sleep");
        }

        [Theory]
        [InlineData("lock")]
        [InlineData("kilitle")]
        public async Task Handle_PowerLock_ReturnsSuccess(string action)
        {
            // Arrange
            _systemControlMock.Setup(s => s.LockSystemAsync())
                .ReturnsAsync(true);

            var command = new ControlDeviceCommand("power", action);

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("locked");
        }

        [Fact]
        public async Task Handle_PowerStatus_ReturnsSystemInfo()
        {
            // Arrange
            var systemStatus = new SystemStatusInfo
            {
                VolumeLevel = 75,
                BrightnessLevel = 80,
                IsWiFiEnabled = true,
                IsBluetoothEnabled = false,
                BatteryLevel = 85
            };

            _systemControlMock.Setup(s => s.GetSystemStatusAsync())
                .ReturnsAsync(systemStatus);

            var command = new ControlDeviceCommand("power", "status");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeTrue();
            result.Message.Should().Contain("Volume: 75%");
            result.Message.Should().Contain("Brightness: 80%");
            result.Message.Should().Contain("WiFi: On");
            result.Message.Should().Contain("Bluetooth: Off");
            result.Message.Should().Contain("Battery: 85%");
        }

        #endregion

        #region Error Handling Tests

        [Fact]
        public async Task Handle_UnknownDevice_ReturnsFailure()
        {
            // Arrange
            var command = new ControlDeviceCommand("unknown_device", "action");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unknown device");
        }

        [Fact]
        public async Task Handle_UnknownVolumeAction_ReturnsFailure()
        {
            // Arrange
            var command = new ControlDeviceCommand("volume", "unknown_action");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Unknown volume action");
        }

        [Fact]
        public async Task Handle_ServiceThrowsException_ReturnsFailure()
        {
            // Arrange
            _factoryMock.Setup(f => f.CreateSystemService())
                .Throws(new Exception("Service creation failed"));

            var command = new ControlDeviceCommand("volume", "increase");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("Failed to");
        }

        [Fact]
        public async Task Handle_PlatformNotSupported_ReturnsFailure()
        {
            // Arrange
            _factoryMock.Setup(f => f.CreateSystemService())
                .Throws(new PlatformNotSupportedException("Unsupported OS"));

            var command = new ControlDeviceCommand("volume", "increase");

            // Act
            var result = await _handler.Handle(command, CancellationToken.None);

            // Assert
            result.Success.Should().BeFalse();
            result.Message.Should().Contain("not supported");
        }

        #endregion

        #region Notification Tests

        [Fact]
        public async Task Handle_SuccessfulCommand_PublishesNotification()
        {
            // Arrange
            _systemControlMock.Setup(s => s.IncreaseSystemVolumeAsync(10))
                .ReturnsAsync(true);
            _systemControlMock.Setup(s => s.GetSystemVolumeAsync())
                .ReturnsAsync(60);

            var command = new ControlDeviceCommand("volume", "increase");

            // Act
            await _handler.Handle(command, CancellationToken.None);

            // Assert
            _mediatorMock.Verify(m => m.Publish(
                It.Is<DeviceControlledNotification>(n => 
                    n.DeviceName == "volume" && 
                    n.Action == "increase" &&
                    n.Success == true),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        #endregion
    }
}

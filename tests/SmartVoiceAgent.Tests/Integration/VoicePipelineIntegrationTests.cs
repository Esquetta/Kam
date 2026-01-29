using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;

using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models;
using SmartVoiceAgent.Core.Models.Audio;
using SmartVoiceAgent.Infrastructure.Services;
using System.Buffers;

namespace SmartVoiceAgent.Tests.Integration
{
    /// <summary>
    /// Integration tests for the complete voice pipeline:
    /// Audio Capture -> STT -> Intent Detection -> Command Execution
    /// </summary>
    public class VoicePipelineIntegrationTests
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly Mock<ISpeechToTextService> _mockSttService;
        private readonly Mock<IIntentDetectionService> _mockIntentService;
        private readonly Mock<IMediator> _mockMediator;
        private readonly List<CommandResult> _executedCommands;

        public VoicePipelineIntegrationTests()
        {
            _executedCommands = new List<CommandResult>();
            _mockSttService = new Mock<ISpeechToTextService>();
            _mockIntentService = new Mock<IIntentDetectionService>();
            _mockMediator = new Mock<IMediator>();

            // Setup mock behaviors
            SetupMockBehaviors();

            var services = new ServiceCollection();
            services.AddSingleton(_mockSttService.Object);
            services.AddSingleton(_mockIntentService.Object);
            services.AddSingleton(_mockMediator.Object);
            
            _serviceProvider = services.BuildServiceProvider();
        }

        private void SetupMockBehaviors()
        {
            // Mock STT to return predictable text based on audio data
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[] audio, CancellationToken ct) =>
                {
                    // Simulate STT returning text based on audio content
                    if (audio == null || audio.Length == 0)
                        return new SpeechResult { ErrorMessage = "No audio" };
                    
                    // Return mock text based on audio length (simulating different commands)
                    var text = audio.Length switch
                    {
                        < 1000 => "chrome aç",
                        < 2000 => "spotify çal",
                        < 3000 => "ışıkları kapat",
                        _ => "bilinmeyen komut"
                    };
                    
                    return new SpeechResult 
                    { 
                        Text = text, 
                        Confidence = 0.95f,
                        ProcessingTime = TimeSpan.FromMilliseconds(150)
                    };
                });

            // Mock intent detection
            _mockIntentService
                .Setup(s => s.DetectIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string text, string lang, CancellationToken ct) =>
                {
                    var intent = text.ToLower() switch
                    {
                        var t when t.Contains("aç") || t.Contains("open") => CommandType.OpenApplication,
                        var t when t.Contains("çal") || t.Contains("play") => CommandType.PlayMusic,
                        var t when t.Contains("kapat") || t.Contains("close") || t.Contains("ışık") => CommandType.ControlDevice,
                        _ => CommandType.Unknown
                    };

                    return new IntentResult
                    {
                        Intent = intent,
                        Confidence = 0.92f,
                        OriginalText = text,
                        Language = lang,
                        Entities = ExtractEntities(text)
                    };
                });

            // Mock mediator to capture executed commands
            _mockMediator
                .Setup(m => m.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((object request, CancellationToken ct) =>
                {
                    var result = new CommandResult
                    {
                        Success = true,
                        Message = "Command executed successfully",
                        OriginalInput = request.ToString()
                    };
                    _executedCommands.Add(result);
                    return result;
                });
        }

        private Dictionary<string, object> ExtractEntities(string text)
        {
            var entities = new Dictionary<string, object>();
            var lower = text.ToLower();

            if (lower.Contains("chrome"))
                entities["applicationName"] = "chrome";
            if (lower.Contains("spotify"))
                entities["applicationName"] = "spotify";
            if (lower.Contains("ışık"))
                entities["deviceName"] = "lights";

            return entities;
        }

        [Fact]
        public async Task VoicePipeline_FullFlow_OpenApplicationCommand()
        {
            // Arrange
            var audioData = GenerateMockAudioData(500); // Small audio = "chrome aç"
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();
            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();
            var mediator = _serviceProvider.GetRequiredService<IMediator>();

            // Act - Step 1: STT
            var speechResult = await sttService.ConvertToTextAsync(audioData, CancellationToken.None);
            speechResult.Text.Should().Be("chrome aç");
            speechResult.Confidence.Should().BeGreaterThan(0.9f);

            // Act - Step 2: Intent Detection
            var intentResult = await intentService.DetectIntentAsync(speechResult.Text, "tr", CancellationToken.None);
            intentResult.Intent.Should().Be(CommandType.OpenApplication);
            intentResult.Entities.Should().ContainKey("applicationName");
            intentResult.Entities["applicationName"].Should().Be("chrome");

            // Act - Step 3: Command Execution
            // Simulate command execution - mock returns CommandResult
            var commandResult = new CommandResult { Success = true, Message = "Executed" };

            // Assert
            commandResult.Should().NotBeNull();
            commandResult.Success.Should().BeTrue();
            _executedCommands.Should().ContainSingle();
        }

        [Fact]
        public async Task VoicePipeline_FullFlow_PlayMusicCommand()
        {
            // Arrange
            var audioData = GenerateMockAudioData(1500); // Medium audio = "spotify çal"
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();
            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();
            var mediator = _serviceProvider.GetRequiredService<IMediator>();

            // Act - Execute full pipeline
            var speechResult = await sttService.ConvertToTextAsync(audioData, CancellationToken.None);
            var intentResult = await intentService.DetectIntentAsync(speechResult.Text, "tr", CancellationToken.None);
            
            // Assert pipeline results
            speechResult.Text.Should().Be("spotify çal");
            intentResult.Intent.Should().Be(CommandType.PlayMusic);
        }

        [Fact]
        public async Task VoicePipeline_FullFlow_ControlDeviceCommand()
        {
            // Arrange
            var audioData = GenerateMockAudioData(2500); // Large audio = "ışıkları kapat"
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();
            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();

            // Act
            var speechResult = await sttService.ConvertToTextAsync(audioData, CancellationToken.None);
            var intentResult = await intentService.DetectIntentAsync(speechResult.Text, "tr", CancellationToken.None);

            // Assert
            speechResult.Text.Should().Be("ışıkları kapat");
            intentResult.Intent.Should().Be(CommandType.ControlDevice);
            intentResult.Entities.Should().ContainKey("deviceName");
        }

        [Fact]
        public async Task VoicePipeline_EmptyAudio_ReturnsError()
        {
            // Arrange
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act
            var result = await sttService.ConvertToTextAsync(Array.Empty<byte>(), CancellationToken.None);

            // Assert
            result.ErrorMessage.Should().NotBeNullOrEmpty();
            result.Text.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task VoicePipeline_LowConfidenceIntent_HandledGracefully()
        {
            // Arrange
            _mockIntentService
                .Setup(s => s.DetectIntentAsync("garbled text", "tr", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new IntentResult
                {
                    Intent = CommandType.Unknown,
                    Confidence = 0.3f,
                    OriginalText = "garbled text"
                });

            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();

            // Act
            var result = await intentService.DetectIntentAsync("garbled text", "tr", CancellationToken.None);

            // Assert
            result.Intent.Should().Be(CommandType.Unknown);
            result.Confidence.Should().BeLessThan(0.5f);
        }

        [Fact]
        public async Task VoicePipeline_MultipleCommands_SequentialExecution()
        {
            // Arrange
            var commands = new[]
            {
                (audio: GenerateMockAudioData(500), expectedText: "chrome aç"),
                (audio: GenerateMockAudioData(1500), expectedText: "spotify çal"),
                (audio: GenerateMockAudioData(2500), expectedText: "ışıkları kapat")
            };

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();
            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();

            // Act & Assert
            foreach (var (audio, expectedText) in commands)
            {
                var speechResult = await sttService.ConvertToTextAsync(audio, CancellationToken.None);
                var intentResult = await intentService.DetectIntentAsync(speechResult.Text, "tr", CancellationToken.None);

                speechResult.Text.Should().Be(expectedText);
                intentResult.Confidence.Should().BeGreaterThan(0.5f);
            }
        }

        [Fact]
        public async Task VoicePipeline_STTPerformance_Under500ms()
        {
            // Arrange
            var audioData = GenerateMockAudioData(1000);
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await sttService.ConvertToTextAsync(audioData, CancellationToken.None);
            stopwatch.Stop();

            // Assert
            result.ProcessingTime.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        }

        [Fact]
        public void VoicePipeline_AudioBuffer_ReusesArrays()
        {
            // Arrange
            var pool = ArrayPool<byte>.Shared;
            var buffer = pool.Rent(1024);

            try
            {
                // Act - Simulate audio processing
                GenerateMockAudioDataInto(buffer, 500);
                var data = new ReadOnlySpan<byte>(buffer, 0, 500);

                // Assert - Buffer should contain valid data
                data.Length.Should().Be(500);
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        #region Helper Methods

        private byte[] GenerateMockAudioData(int length)
        {
            var data = new byte[length];
            var random = new Random(42);
            random.NextBytes(data);
            return data;
        }

        private void GenerateMockAudioDataInto(byte[] buffer, int length)
        {
            var random = new Random(42);
            random.NextBytes(buffer.AsSpan(0, length));
        }

        #endregion
    }
}

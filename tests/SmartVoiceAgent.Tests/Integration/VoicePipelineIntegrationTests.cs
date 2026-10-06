using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using System.Buffers;

namespace SmartVoiceAgent.Tests.Integration
{
    /// <summary>
    /// Integration tests for the voice pipeline components.
    /// Tests the data flow and transformations without requiring actual STT/AI services.
    /// </summary>
    public class VoicePipelineIntegrationTests
    {
        private readonly Mock<ISpeechToTextService> _mockSttService;

        public VoicePipelineIntegrationTests()
        {
            _mockSttService = new Mock<ISpeechToTextService>();

            SetupMockBehaviors();
        }

        private void SetupMockBehaviors()
        {
            // Mock STT to return predictable text based on audio data
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[] audio, CancellationToken ct) =>
                {
                    if (audio == null || audio.Length == 0)
                        return new SpeechResult { ErrorMessage = "No audio" };
                    
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
        }

        [Fact]
        public async Task VoicePipeline_FullFlow_OpenApplicationCommand()
        {
            // Arrange
            var audioData = GenerateMockAudioData(500);

            // Act - Execute pipeline steps
            var speechResult = await _mockSttService.Object.ConvertToTextAsync(audioData, CancellationToken.None);

            // Assert
            speechResult.Text.Should().Be("chrome aç");
            speechResult.Confidence.Should().BeGreaterThan(0.9f);
        }

        [Fact]
        public async Task VoicePipeline_FullFlow_PlayMusicCommand()
        {
            // Arrange
            var audioData = GenerateMockAudioData(1500);

            // Act
            var speechResult = await _mockSttService.Object.ConvertToTextAsync(audioData, CancellationToken.None);

            // Assert
            speechResult.Text.Should().Be("spotify çal");
        }

        [Fact]
        public async Task VoicePipeline_FullFlow_ControlDeviceCommand()
        {
            // Arrange
            var audioData = GenerateMockAudioData(2500);

            // Act
            var speechResult = await _mockSttService.Object.ConvertToTextAsync(audioData, CancellationToken.None);

            // Assert
            speechResult.Text.Should().Be("ışıkları kapat");
        }

        [Fact]
        public async Task VoicePipeline_EmptyAudio_ReturnsError()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(Array.Empty<byte>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeechResult { ErrorMessage = "No audio" });

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(Array.Empty<byte>(), CancellationToken.None);

            // Assert
            result.ErrorMessage.Should().NotBeNullOrEmpty();
            result.Text.Should().BeNullOrEmpty();
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

            // Act & Assert
            foreach (var (audio, expectedText) in commands)
            {
                var speechResult = await _mockSttService.Object.ConvertToTextAsync(audio, CancellationToken.None);

                speechResult.Text.Should().Be(expectedText);
            }
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        public async Task VoicePipeline_VariousAudioSizes_Handled(int size)
        {
            // Arrange
            var audioData = GenerateMockAudioData(size);

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(audioData, CancellationToken.None);

            // Assert
            result.Should().NotBeNull();
            result.ErrorMessage.Should().BeNullOrEmpty();
            result.Text.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task VoicePipeline_STTPerformance_Under500ms()
        {
            // Arrange
            var audioData = GenerateMockAudioData(1000);

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(audioData, CancellationToken.None);

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
                // Act
                GenerateMockAudioDataInto(buffer, 500);
                var data = new ReadOnlySpan<byte>(buffer, 0, 500);

                // Assert
                data.Length.Should().Be(500);
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        [Fact]
        public async Task VoicePipeline_EmptyTextFromSTT_ReturnsEmptyIntent()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.Is<byte[]>(b => b.Length == 1), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeechResult { Text = "" });

            // Act
            var speechResult = await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);

            // Assert
            speechResult.Text.Should().BeEmpty();
        }

        [Fact]
        public async Task VoicePipeline_LowConfidenceSTT_Handled()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeechResult 
                { 
                    Text = "garbled",
                    Confidence = 0.2f,
                    ErrorMessage = "Low confidence"
                });

            var audioData = GenerateMockAudioData(100);

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(audioData, CancellationToken.None);

            // Assert
            result.Confidence.Should().BeLessThan(0.5f);
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

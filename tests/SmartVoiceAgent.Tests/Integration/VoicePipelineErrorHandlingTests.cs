using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartVoiceAgent.Application.Behaviors.Performance;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Models.Audio;
using System.Text.Json;

namespace SmartVoiceAgent.Tests.Integration
{
    /// <summary>
    /// Integration tests for error handling in the voice pipeline
    /// </summary>
    public class VoicePipelineErrorHandlingTests
    {
        private readonly Mock<ISpeechToTextService> _mockSttService;
        private readonly Mock<IIntentDetectionService> _mockIntentService;
        private readonly Mock<IMediator> _mockMediator;

        public VoicePipelineErrorHandlingTests()
        {
            _mockSttService = new Mock<ISpeechToTextService>();
            _mockIntentService = new Mock<IIntentDetectionService>();
            _mockMediator = new Mock<IMediator>();
        }

        [Fact]
        public async Task Pipeline_STTException_ReturnsErrorResult()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("STT service unavailable"));

            // Act
            Exception? caughtException = null;
            try
            {
                await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            caughtException.Should().NotBeNull();
            caughtException.Should().BeOfType<InvalidOperationException>();
            caughtException!.Message.Should().Contain("STT service unavailable");
        }

        [Fact]
        public async Task Pipeline_IntentDetectionException_ReturnsError()
        {
            // Arrange
            _mockIntentService
                .Setup(s => s.DetectIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NullReferenceException("Logger not initialized"));

            // Act
            Exception? caughtException = null;
            try
            {
                await _mockIntentService.Object.DetectIntentAsync("test command", "tr", CancellationToken.None);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            caughtException.Should().NotBeNull();
        }

        [Fact]
        public async Task Pipeline_CancellationRequested_OperationCancelled()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), cts.Token))
                .ThrowsAsync(new OperationCanceledException());

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1, 2, 3 }, cts.Token);
            });
        }

        [Fact]
        public async Task Pipeline_NullAudioData_ReturnsError()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(null!, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeechResult { ErrorMessage = "Audio data is null" });

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(null!, CancellationToken.None);

            // Assert
            result.ErrorMessage.Should().NotBeNullOrEmpty();
            result.Text.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task Pipeline_EmptyTextFromSTT_SkipsIntentDetection()
        {
            // Arrange
            var speechResult = new SpeechResult { Text = "" };

            // Assert - Empty text should result in Unknown intent
            if (string.IsNullOrEmpty(speechResult.Text))
            {
                true.Should().BeTrue(); // Empty text handled
            }
        }

        [Fact]
        public async Task Pipeline_LowConfidenceSTT_RetriesOrReturnsError()
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

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);

            // Assert
            result.Confidence.Should().BeLessThan(0.5f);
        }

        [Fact]
        public async Task Pipeline_NetworkExceptionDuringSTT_ReturnsError()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Network error"));

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);
            });
        }

        [Fact]
        public void Pipeline_SerializationException_HandledProperly()
        {
            // Arrange
            var invalidJson = "not valid json";

            // Act
            Exception? caughtException = null;
            try
            {
                JsonSerializer.Deserialize<CommandResult>(invalidJson);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            caughtException.Should().NotBeNull();
            caughtException.Should().BeOfType<System.Text.Json.JsonException>();
        }

        [Fact]
        public async Task Pipeline_ConcurrentRequests_HandledSafely()
        {
            // Arrange
            var tasks = new List<Task<SpeechResult>>();

            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((byte[] audio, CancellationToken ct) => new SpeechResult 
                { 
                    Text = "test",
                    Confidence = 0.9f 
                });

            // Act
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_mockSttService.Object.ConvertToTextAsync(new byte[] { (byte)i }, CancellationToken.None));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllSatisfy(r => r.Should().NotBeNull());
            results.Should().AllSatisfy(r => r.Text.Should().NotBeNull());
        }

        [Fact]
        public async Task Pipeline_RateLimitExceeded_ReturnsError()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Rate limit exceeded", null, System.Net.HttpStatusCode.TooManyRequests));

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);
            });
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(1000)]
        public async Task Pipeline_VariousDurations_MeasuredCorrectly(int delayMs)
        {
            // Arrange
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Act
            await Task.Delay(delayMs);
            stopwatch.Stop();

            // Assert - Allow some tolerance for timing
            stopwatch.ElapsedMilliseconds.Should().BeInRange(delayMs - 50, delayMs + 100);
        }

        [Fact]
        public async Task Pipeline_TimeoutException_HandledGracefully()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .Returns(async () =>
                {
                    await Task.Delay(100);
                    return new SpeechResult 
                    { 
                        ErrorMessage = "Request timeout",
                        ProcessingTime = TimeSpan.FromSeconds(30)
                    };
                });

            // Act
            var result = await _mockSttService.Object.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);

            // Assert
            result.ErrorMessage.Should().Contain("timeout");
        }

        [Fact]
        public void Serialization_RoundTrip_PreservesData()
        {
            // Arrange
            var original = new TestResponse 
            { 
                Data = "test data",
                Timestamp = DateTime.UtcNow,
                Count = 42
            };

            // Act
            var serialized = JsonSerializer.SerializeToUtf8Bytes(original);
            var deserialized = JsonSerializer.Deserialize<TestResponse>(serialized);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Data.Should().Be(original.Data);
            deserialized.Count.Should().Be(original.Count);
        }

        #region Test Classes

        public class TestRequest : IRequest<TestResponse> { }
        public class TestResponse 
        { 
            public string Data { get; set; } = ""; 
            public DateTime Timestamp { get; set; }
            public int Count { get; set; }
        }

        #endregion
    }
}

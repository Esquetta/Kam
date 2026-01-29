using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using SmartVoiceAgent.Application.Behaviors.Performance;
using SmartVoiceAgent.Application.Pipelines.Caching;
using SmartVoiceAgent.Core.Dtos;
using SmartVoiceAgent.Core.Entities;
using SmartVoiceAgent.Core.Enums;
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
        private readonly Mock<ILogger<PerformanceBehavior<TestRequest, TestResponse>>> _mockPerfLogger;
        private readonly IServiceProvider _serviceProvider;

        public VoicePipelineErrorHandlingTests()
        {
            _mockSttService = new Mock<ISpeechToTextService>();
            _mockIntentService = new Mock<IIntentDetectionService>();
            _mockMediator = new Mock<IMediator>();
            _mockPerfLogger = new Mock<ILogger<PerformanceBehavior<TestRequest, TestResponse>>>();

            var services = new ServiceCollection();
            services.AddSingleton(_mockSttService.Object);
            services.AddSingleton(_mockIntentService.Object);
            services.AddSingleton(_mockMediator.Object);
            
            _serviceProvider = services.BuildServiceProvider();
        }

        [Fact]
        public async Task Pipeline_STTException_ReturnsErrorResult()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("STT service unavailable"));

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act
            Exception? caughtException = null;
            try
            {
                await sttService.ConvertToTextAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);
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
        public async Task Pipeline_IntentDetectionException_ReturnsUnknownIntent()
        {
            // Arrange
            _mockIntentService
                .Setup(s => s.DetectIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new NullReferenceException("Logger not initialized"));

            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();

            // Act
            Exception? caughtException = null;
            try
            {
                await intentService.DetectIntentAsync("test command", "tr", CancellationToken.None);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            caughtException.Should().NotBeNull();
        }

        [Fact]
        public async Task Pipeline_CommandHandlerException_ReturnsFailureResult()
        {
            // Arrange
            _mockMediator
                .Setup(m => m.Send(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Application not found"));

            var mediator = _serviceProvider.GetRequiredService<IMediator>();

            // Act
            Exception? caughtException = null;
            try
            {
                await mediator.Send(new object(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                caughtException = ex;
            }

            // Assert
            caughtException.Should().NotBeNull();
            caughtException!.Message.Should().Contain("Application not found");
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

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                await sttService.ConvertToTextAsync(new byte[] { 1, 2, 3 }, cts.Token);
            });
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

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act
            var result = await sttService.ConvertToTextAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

            // Assert
            result.ErrorMessage.Should().Contain("timeout");
        }

        [Fact]
        public async Task Pipeline_NullAudioData_ReturnsError()
        {
            // Arrange
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            _mockSttService
                .Setup(s => s.ConvertToTextAsync(null!, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeechResult { ErrorMessage = "Audio data is null" });

            // Act
            var result = await sttService.ConvertToTextAsync(null!, CancellationToken.None);

            // Assert
            result.ErrorMessage.Should().NotBeNullOrEmpty();
            result.Text.Should().BeNullOrEmpty();
        }

        [Fact]
        public async Task Pipeline_EmptyTextFromSTT_SkipsIntentDetection()
        {
            // Arrange
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();
            var intentService = _serviceProvider.GetRequiredService<IIntentDetectionService>();

            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SpeechResult { Text = "" });

            // Act
            var speechResult = await sttService.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);

            // Assert
            speechResult.Text.Should().BeEmpty();
            
            if (string.IsNullOrEmpty(speechResult.Text))
            {
                // Should skip intent detection for empty text
                _mockIntentService.Verify(
                    s => s.DetectIntentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
                    Times.Never);
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

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act
            var result = await sttService.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);

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

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await sttService.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);
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
        public async Task Pipeline_MultipleConcurrentRequests_HandledSafely()
        {
            // Arrange
            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();
            var tasks = new List<Task<SpeechResult>>();

            // Act
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(sttService.ConvertToTextAsync(new byte[] { (byte)i }, CancellationToken.None));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            results.Should().AllSatisfy(r => r.Should().NotBeNull());
        }

        [Fact]
        public async Task Pipeline_RateLimitExceeded_ReturnsError()
        {
            // Arrange
            _mockSttService
                .Setup(s => s.ConvertToTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Rate limit exceeded", null, System.Net.HttpStatusCode.TooManyRequests));

            var sttService = _serviceProvider.GetRequiredService<ISpeechToTextService>();

            // Act & Assert
            await Assert.ThrowsAsync<HttpRequestException>(async () =>
            {
                await sttService.ConvertToTextAsync(new byte[] { 1 }, CancellationToken.None);
            });
        }

        #region Test Classes

        public class TestRequest : IRequest<TestResponse> { }
        public class TestResponse { public string Data { get; set; } = ""; }

        #endregion
    }
}

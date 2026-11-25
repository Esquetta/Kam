using System.Net;
using System.Net.Sockets;
using Anthropic.Exceptions;
using Anthropic.Models;
using FluentAssertions;
using SmartVoiceAgent.Infrastructure.Agent.Runtime;

namespace SmartVoiceAgent.Tests.Infrastructure.Agent.Runtime;

public sealed class ModelErrorClassifierTests
{
    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(529)]
    public void Classify_TemporaryStatus_IsTransient(int status)
    {
        var error = ModelErrorClassifier.Classify(new StatusException(status, "failed"));

        error.Kind.Should().Be(ModelErrorKind.Transient);
        error.StatusCode.Should().Be(status);
    }

    [Theory]
    [InlineData(400, ModelErrorKind.InvalidRequest)]
    [InlineData(401, ModelErrorKind.Authentication)]
    [InlineData(402, ModelErrorKind.Quota)]
    [InlineData(403, ModelErrorKind.Authentication)]
    [InlineData(404, ModelErrorKind.NotFound)]
    [InlineData(409, ModelErrorKind.InvalidRequest)]
    [InlineData(413, ModelErrorKind.ContextTooLong)]
    [InlineData(422, ModelErrorKind.InvalidRequest)]
    public void Classify_StatusTheUserMustFix_IsNotTransient(int status, ModelErrorKind kind)
    {
        var error = ModelErrorClassifier.Classify(new StatusException(status, "failed"));

        error.Kind.Should().Be(kind);
        error.IsTransient.Should().BeFalse();
    }

    [Fact]
    public void Classify_StatusOnAnInnerException_IsFound()
    {
        var wrapped = new InvalidOperationException("stream failed", new StatusException(503, "unavailable"));

        ModelErrorClassifier.Classify(wrapped).Should().Match<ModelError>(e => e.IsTransient && e.StatusCode == 503);
    }

    [Fact]
    public void Classify_RateLimitThatIsAnEmptyQuota_IsNotRetried()
    {
        var error = ModelErrorClassifier.Classify(
            new StatusException(429, "HTTP 429 (insufficient_quota) You exceeded your current quota."));

        error.Kind.Should().Be(ModelErrorKind.Quota);
        error.UserMessage.Should().Contain("billing");
    }

    [Theory]
    [InlineData("HTTP 400 (invalid_request_error: context_length_exceeded)")]
    [InlineData("This model's maximum context length is 128000 tokens.")]
    [InlineData("prompt is too long: 210000 tokens > 200000 maximum")]
    public void Classify_ContextTooLong_IsRecognizedFromTheProviderMessage(string message)
    {
        ModelErrorClassifier.Classify(new StatusException(400, message)).Kind.Should().Be(ModelErrorKind.ContextTooLong);
    }

    [Fact]
    public void Classify_ConnectionFailures_AreTransient()
    {
        Exception[] failures =
        [
            new HttpRequestException("No such host is known."),
            new IOException("The response ended prematurely."),
            new HttpRequestException("failed", new SocketException((int)SocketError.ConnectionReset)),
            new AnthropicIOException("I/O failure", new HttpRequestException("connection reset"))
        ];

        failures.Select(ModelErrorClassifier.Classify).Should().OnlyContain(e => e.IsTransient && e.Reason == "connection lost");
    }

    [Fact]
    public void Classify_Timeouts_AreTransient()
    {
        Exception[] failures =
        [
            new TimeoutException(),
            new TaskCanceledException("timeout", new TimeoutException()),
            new OperationCanceledException()
        ];

        failures.Select(ModelErrorClassifier.Classify).Should().OnlyContain(e => e.IsTransient && e.Reason == "request timed out");
    }

    [Fact]
    public void Classify_AnthropicStatus_IsReadFromTheSdkException()
    {
        var limited = new AnthropicRateLimitException(new HttpRequestException("rate limited"))
        {
            StatusCode = HttpStatusCode.TooManyRequests,
            ResponseBody = "{}"
        };
        var unauthorized = new AnthropicApiException("invalid x-api-key", new HttpRequestException("unauthorized"))
        {
            StatusCode = HttpStatusCode.Unauthorized,
            ResponseBody = "{}"
        };

        ModelErrorClassifier.Classify(limited).Should().Match<ModelError>(e => e.IsTransient && e.Reason == "rate limited (HTTP 429)");
        ModelErrorClassifier.Classify(unauthorized).Kind.Should().Be(ModelErrorKind.Authentication);
    }

    [Theory]
    [InlineData(ErrorType.OverloadedError, ModelErrorKind.Transient)]
    [InlineData(ErrorType.ApiError, ModelErrorKind.Transient)]
    [InlineData(ErrorType.RateLimitError, ModelErrorKind.Transient)]
    [InlineData(ErrorType.TimeoutError, ModelErrorKind.Transient)]
    [InlineData(ErrorType.InvalidRequestError, ModelErrorKind.InvalidRequest)]
    [InlineData(ErrorType.AuthenticationError, ModelErrorKind.Authentication)]
    [InlineData(ErrorType.PermissionError, ModelErrorKind.Authentication)]
    [InlineData(ErrorType.NotFoundError, ModelErrorKind.NotFound)]
    [InlineData(ErrorType.BillingError, ModelErrorKind.Quota)]
    public void Classify_ErrorSentInsideAnAnthropicStream_FollowsItsType(ErrorType type, ModelErrorKind kind)
    {
        var error = new AnthropicSseException("stream error", new InvalidOperationException()) { ErrorType = type };

        ModelErrorClassifier.Classify(error).Kind.Should().Be(kind);
    }

    [Fact]
    public void Classify_UnknownError_IsNotRetriedAndKeepsItsMessageWithoutSecrets()
    {
        var error = ModelErrorClassifier.Classify(new InvalidOperationException("Bad state for key sk-abc123def456ghi789"));

        error.Kind.Should().Be(ModelErrorKind.Unknown);
        error.UserMessage.Should().StartWith("Bad state for key").And.NotContain("sk-abc123def456ghi789");
    }

    /// <summary>Has the shape of <c>System.ClientModel.ClientResultException</c>: an int <c>Status</c>.</summary>
    private sealed class StatusException(int status, string message) : Exception(message)
    {
        public int Status { get; } = status;
    }
}

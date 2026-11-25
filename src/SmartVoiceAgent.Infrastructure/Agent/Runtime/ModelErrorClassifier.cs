using System.Net;
using System.Net.Sockets;
using Anthropic.Exceptions;
using Anthropic.Models;
using SmartVoiceAgent.Core.Security;

namespace SmartVoiceAgent.Infrastructure.Agent.Runtime;

/// <summary>What went wrong with a model request, as far as the agent loop needs to know.</summary>
public enum ModelErrorKind
{
    /// <summary>A timeout, rate limit, server error or lost connection. Sending the request again can work.</summary>
    Transient,

    /// <summary>The provider rejected the API key or the account may not use the model.</summary>
    Authentication,

    /// <summary>The account has no quota or balance left.</summary>
    Quota,

    /// <summary>The request is larger than the model's context window.</summary>
    ContextTooLong,

    /// <summary>The model or endpoint does not exist.</summary>
    NotFound,

    /// <summary>The provider refused the request as malformed.</summary>
    InvalidRequest,

    /// <summary>Anything else. Not retried, because nothing says a second try would differ.</summary>
    Unknown
}

/// <summary>
/// A classified model error.
/// </summary>
/// <param name="Kind">The kind of failure.</param>
/// <param name="StatusCode">The HTTP status, when the provider returned one.</param>
/// <param name="Reason">Short cause for logs and retry events, such as "rate limited (HTTP 429)".</param>
/// <param name="UserMessage">What to tell the user when the turn fails on this error.</param>
public sealed record ModelError(ModelErrorKind Kind, int? StatusCode, string Reason, string UserMessage)
{
    /// <summary>Gets whether sending the same request again can succeed.</summary>
    public bool IsTransient => Kind == ModelErrorKind.Transient;
}

/// <summary>
/// Sorts model request failures into temporary ones worth retrying and ones the user has to fix.
/// Works on the HTTP status and exception types, so it covers the OpenAI-compatible and Anthropic clients alike.
/// </summary>
public static class ModelErrorClassifier
{
    private const int MaxDetailLength = 300;

    private static readonly string[] ContextTooLongHints =
    [
        "context_length_exceeded",
        "context length",
        "context window",
        "maximum context",
        "prompt is too long",
        "input is too long",
        "too many tokens",
        "reduce the length"
    ];

    private static readonly string[] QuotaHints =
    [
        "insufficient_quota",
        "exceeded your current quota",
        "credit balance"
    ];

    /// <summary>
    /// Classifies a failed model request. The caller handles a cancelled turn before asking: a cancellation
    /// that reaches this method is a timeout, not a Stop.
    /// </summary>
    /// <param name="exception">What the chat client threw.</param>
    public static ModelError Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var chain = Chain(exception).ToList();
        var status = chain.Select(TryGetStatusCode).FirstOrDefault(code => code is not null);
        var errorType = chain.OfType<AnthropicServiceException>().Select(error => error.ErrorType).FirstOrDefault(type => type is not null);
        var text = string.Join('\n', chain.Select(error => error.Message));
        var detail = Detail(exception.Message);

        if (status is null or 400 or 413 or 422 && (status == 413 || ContainsAny(text, ContextTooLongHints)))
        {
            return new ModelError(
                ModelErrorKind.ContextTooLong,
                status,
                "context too long",
                "The conversation is too long for this model. Send /compact to summarize earlier turns, or start a new task.");
        }

        if (status == 402 || errorType == ErrorType.BillingError || ContainsAny(text, QuotaHints))
        {
            return new ModelError(
                ModelErrorKind.Quota,
                status,
                "no quota or balance",
                "The provider says the account has no quota or balance left. Check billing with the provider, or pick another model.");
        }

        if (status is 401 or 403 || errorType is ErrorType.AuthenticationError or ErrorType.PermissionError)
        {
            return new ModelError(
                ModelErrorKind.Authentication,
                status,
                WithStatus("API key rejected", status),
                $"The provider rejected the API key{StatusSuffix(status)}. Check the key for this model in Settings, then send the message again. Detail: {detail}");
        }

        if (status == 404 || errorType == ErrorType.NotFoundError)
        {
            return new ModelError(
                ModelErrorKind.NotFound,
                status,
                WithStatus("model or endpoint not found", status),
                $"The provider did not find the model or endpoint{StatusSuffix(status)}. Check the model name and endpoint in Settings. Detail: {detail}");
        }

        if (status is 408 or 425 or 429 or >= 500)
        {
            var cause = status switch
            {
                408 => "request timed out",
                429 => "rate limited",
                _ => "provider error"
            };
            return Transient(status, WithStatus(cause, status));
        }

        if (status is not null || errorType == ErrorType.InvalidRequestError)
        {
            return new ModelError(
                ModelErrorKind.InvalidRequest,
                status,
                WithStatus("request rejected", status),
                $"The provider rejected the request{StatusSuffix(status)}: {detail}");
        }

        if (errorType is not null)
        {
            // What is left are the provider's own temporary errors: rate limit, timeout, overloaded, API error.
            return Transient(null, errorType == ErrorType.RateLimitError ? "rate limited" : "provider error");
        }

        if (chain.Any(error => error is TimeoutException or OperationCanceledException))
        {
            return Transient(null, "request timed out");
        }

        if (chain.Any(error => error is HttpRequestException or IOException or SocketException))
        {
            return Transient(null, "connection lost");
        }

        return new ModelError(ModelErrorKind.Unknown, null, "unexpected error", SecretRedactor.Redact(exception.Message));
    }

    private static ModelError Transient(int? status, string reason) =>
        new(ModelErrorKind.Transient, status, reason, reason);

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    /// <summary>
    /// Reads the HTTP status from the exception types the chat clients throw: <c>ClientResultException.Status</c>,
    /// <c>AnthropicApiException.StatusCode</c> and <c>HttpRequestException.StatusCode</c>.
    /// </summary>
    private static int? TryGetStatusCode(Exception exception)
    {
        foreach (var name in new[] { "Status", "StatusCode" })
        {
            var code = exception.GetType().GetProperty(name)?.GetValue(exception) switch
            {
                int value => value,
                HttpStatusCode value => (int)value,
                _ => 0
            };
            if (code > 0)
            {
                return code;
            }
        }

        return null;
    }

    private static string Detail(string message)
    {
        var line = SecretRedactor.Redact(message).ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxDetailLength ? line : line[..MaxDetailLength] + "...";
    }

    private static string WithStatus(string cause, int? status) =>
        status is null ? cause : $"{cause} (HTTP {status})";

    private static string StatusSuffix(int? status) =>
        status is null ? string.Empty : $" (HTTP {status})";

    private static bool ContainsAny(string text, string[] hints) =>
        hints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
}

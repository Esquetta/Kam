using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using SmartVoiceAgent.Core.Interfaces;
using SmartVoiceAgent.Core.Security;

namespace SmartVoiceAgent.Infrastructure.Services;

public sealed class AiProviderAlertingChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly IUiLogService? _uiLogService;
    private readonly string _modelId;

    public AiProviderAlertingChatClient(
        IChatClient inner,
        IUiLogService? uiLogService,
        string modelId)
    {
        _inner = inner;
        _uiLogService = uiLogService;
        _modelId = modelId;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _inner.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex) when (NotifyIfProviderLimitFailure(ex))
        {
            throw;
        }
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAsyncEnumerator<ChatResponseUpdate>? enumerator;
        try
        {
            enumerator = _inner.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex) when (NotifyIfProviderLimitFailure(ex))
        {
            throw;
        }

        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await enumerator.MoveNextAsync())
                    {
                        yield break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception ex) when (NotifyIfProviderLimitFailure(ex))
                {
                    throw;
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return _inner.GetService(serviceType, serviceKey);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }

    private bool NotifyIfProviderLimitFailure(Exception exception)
    {
        var alert = Classify(exception);
        if (alert is null)
        {
            return false;
        }

        var model = string.IsNullOrWhiteSpace(_modelId) ? "(unknown model)" : _modelId;
        var detail = SecretRedactor.Redact(exception.Message);
        _uiLogService?.Log(
            $"AI_PROVIDER_{alert.Value.Code}: {alert.Value.Message} Model: {model}. Detail: {detail}",
            LogLevel.Warning,
            "AI");
        return false;
    }

    private static ProviderAlert? Classify(Exception exception)
    {
        var message = exception.ToString();
        var statusCode = TryGetStatusCode(exception);

        if (statusCode == 429
            || ContainsAny(message, "rate limit", "rate_limit", "too many requests", "tokens per minute", "requests per minute"))
        {
            return new ProviderAlert("RATE_LIMIT", "Provider rate limit reached. Wait for reset or switch model/provider.");
        }

        if (statusCode == 402
            || ContainsAny(message, "insufficient_quota", "quota", "billing", "balance", "credit", "payment required", "exceeded your current quota"))
        {
            return new ProviderAlert("QUOTA_OR_BALANCE", "Provider quota or balance looks exhausted. Check billing or switch model/provider.");
        }

        if (statusCode is 401 or 403
            || ContainsAny(message, "unauthorized", "forbidden", "invalid api key", "incorrect api key", "authentication"))
        {
            return new ProviderAlert("AUTH", "Provider rejected the configured API key. Update the key in Settings.");
        }

        return null;
    }

    private static int? TryGetStatusCode(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            foreach (var propertyName in new[] { "Status", "StatusCode", "HttpStatusCode" })
            {
                var property = current.GetType().GetProperty(propertyName);
                var value = property?.GetValue(current);
                if (value is int intValue)
                {
                    return intValue;
                }

                if (value is global::System.Net.HttpStatusCode httpStatusCode)
                {
                    return (int)httpStatusCode;
                }
            }
        }

        return null;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private readonly record struct ProviderAlert(string Code, string Message);
}

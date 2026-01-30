using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartVoiceAgent.Mailing.Entities;
using SmartVoiceAgent.Mailing.Interfaces;

namespace SmartVoiceAgent.Mailing.Services;

/// <summary>
/// Base class for SMS service implementations
/// Provides common functionality like rate limiting, validation, and retry logic
/// </summary>
public abstract class SmsServiceBase : ISmsService
{
    protected readonly SmsSettings _settings;
    protected readonly SmsSendingOptions _options;
    protected readonly ILogger _logger;
    
    private readonly ConcurrentDictionary<string, DateTime> _lastSendTimes = new();
    private readonly ConcurrentDictionary<string, int> _sendCounts = new();
    private readonly SemaphoreSlim _rateLimitLock = new(1, 1);

    protected SmsServiceBase(
        IOptions<SmsSettings> settings,
        IOptions<SmsSendingOptions> options,
        ILogger logger)
    {
        _settings = settings.Value;
        _options = options.Value;
        _logger = logger;
        
        _logger.LogInformation("📱 {Provider} SMS Service initialized", ProviderName);
    }

    public abstract string ProviderName { get; }
    
    public virtual SmsFeatures SupportedFeatures => new()
    {
        SupportsDeliveryReports = true,
        SupportsUnicode = true,
        SupportsBulkSending = true,
        SupportsFlashMessages = false,
        SupportsScheduledMessages = false,
        SupportsTwoWay = false,
        SupportsAlphanumericSender = false,
        SupportsBalanceCheck = false,
        MaxMessageLength = 1600
    };

    public virtual async Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            // Sandbox mode
            if (_settings.SandboxMode || _options.SandboxMode)
            {
                _logger.LogInformation("🧪 [SANDBOX] SMS would be sent to {To}: {Body}", message.To, message.Body);
                var sandboxId = "sandbox-" + Guid.NewGuid().ToString("N").Substring(0, 10);
                var sandboxResult = SmsSendResult.CreateSuccess(message.Id, sandboxId, ProviderName, message.CalculateSegments());
                sandboxResult.Message = "[SANDBOX MODE] SMS not actually sent";
                return sandboxResult;
            }

            // Validate message
            if (_options.ValidateBeforeSend)
            {
                var validation = message.Validate();
                if (!validation.IsValid)
                {
                    return SmsSendResult.CreateFailure(message.Id, string.Join(", ", validation.Errors));
                }
            }

            // Format phone number
            message.To = SmsValidationHelper.FormatToE164(message.To, _options.DefaultCountryCode);
            
            // Auto-detect Unicode
            if (_options.AutoDetectUnicode)
            {
                message.UseUnicode = !IsGsm7Bit(message.Body);
            }
            
            // Calculate segments
            message.Segments = message.CalculateSegments();
            
            // Rate limiting
            await EnforceRateLimitAsync(cancellationToken);
            
            // Send via provider-specific implementation
            var result = await SendInternalAsync(message, cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("✅ SMS sent to {To} via {Provider}. Segments: {Segments}, ID: {MessageId}",
                    message.To, ProviderName, message.Segments, result.ProviderMessageId);
            }
            else
            {
                _logger.LogWarning("❌ Failed to send SMS to {To} via {Provider}: {Error}",
                    message.To, ProviderName, result.Error);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Exception sending SMS to {To} via {Provider}", message.To, ProviderName);
            return SmsSendResult.CreateFailure(message.Id, ex.Message);
        }
    }

    public virtual async Task<SmsSendResult> SendAsync(string to, string body, string? from = null, CancellationToken cancellationToken = default)
    {
        var message = new SmsMessage
        {
            To = to,
            Body = body,
            From = from ?? _settings.DefaultSender
        };
        
        return await SendAsync(message, cancellationToken);
    }

    public virtual async Task<IEnumerable<SmsSendResult>> SendBulkAsync(List<string> recipients, string body, string? from = null, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsSendResult>();
        
        foreach (var recipient in recipients)
        {
            var result = await SendAsync(recipient, body, from, cancellationToken);
            results.Add(result);
            
            // Small delay between bulk sends to respect rate limits
            if (recipients.Count > 1)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
        
        return results;
    }

    public virtual Task<DeliveryStatus> GetDeliveryStatusAsync(Guid messageId, string? providerMessageId = null, CancellationToken cancellationToken = default)
    {
        // Default implementation - override in provider-specific classes
        if (!SupportedFeatures.SupportsDeliveryReports)
        {
            _logger.LogDebug("Provider {Provider} does not support delivery reports", ProviderName);
        }
        
        return Task.FromResult(DeliveryStatus.Unknown);
    }

    public virtual SmsValidationResult ValidatePhoneNumber(string phoneNumber, string? defaultCountryCode = null)
    {
        return SmsValidationHelper.ValidatePhoneNumber(phoneNumber);
    }

    public virtual async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_settings.SandboxMode || _options.SandboxMode)
        {
            _logger.LogInformation("🧪 [SANDBOX] Connection test simulated");
            return true;
        }
        
        try
        {
            return await TestConnectionInternalAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed for {Provider}", ProviderName);
            return false;
        }
    }

    public virtual Task<BalanceInfo?> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        if (!SupportedFeatures.SupportsBalanceCheck)
        {
            _logger.LogDebug("Provider {Provider} does not support balance checking", ProviderName);
        }
        
        return Task.FromResult<BalanceInfo?>(null);
    }

    /// <summary>
    /// Provider-specific send implementation
    /// </summary>
    protected abstract Task<SmsSendResult> SendInternalAsync(SmsMessage message, CancellationToken cancellationToken);
    
    /// <summary>
    /// Provider-specific connection test
    /// </summary>
    protected abstract Task<bool> TestConnectionInternalAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Check if text contains only GSM 7-bit characters
    /// </summary>
    protected static bool IsGsm7Bit(string text)
    {
        const string gsm7BitChars = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
                                    "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
        
        return text.All(c => gsm7BitChars.Contains(c));
    }

    /// <summary>
    /// Enforce rate limiting
    /// </summary>
    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        var key = ProviderName;
        var now = DateTime.UtcNow;
        
        await _rateLimitLock.WaitAsync(cancellationToken);
        try
        {
            // Reset counter if a minute has passed
            if (_lastSendTimes.TryGetValue(key, out var lastSend))
            {
                if ((now - lastSend).TotalMinutes >= 1)
                {
                    _sendCounts[key] = 0;
                }
            }
            
            var rateLimit = _options.RateLimitPerMinute > 0 
                ? _options.RateLimitPerMinute 
                : _settings.RateLimitPerMinute;
            
            // Check rate limit
            var currentCount = _sendCounts.GetValueOrDefault(key, 0);
            if (currentCount >= rateLimit)
            {
                var delayMs = (int)((_lastSendTimes[key].AddMinutes(1) - now).TotalMilliseconds);
                if (delayMs > 0)
                {
                    _logger.LogWarning("Rate limit reached for {Provider}. Waiting {DelayMs}ms", ProviderName, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                    _sendCounts[key] = 0;
                }
            }
            
            // Update counters
            _lastSendTimes[key] = now;
            _sendCounts[key] = currentCount + 1;
        }
        finally
        {
            _rateLimitLock.Release();
        }
    }
}

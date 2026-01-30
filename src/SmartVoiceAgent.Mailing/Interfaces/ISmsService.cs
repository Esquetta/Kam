using SmartVoiceAgent.Mailing.Entities;

namespace SmartVoiceAgent.Mailing.Interfaces;

/// <summary>
/// Generic SMS service interface - works with any provider
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Send an SMS message
    /// </summary>
    /// <param name="message">SMS message to send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send result with status and message ID</returns>
    Task<SmsSendResult> SendAsync(SmsMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a simple SMS
    /// </summary>
    /// <param name="to">Recipient phone number (E.164 format: +1234567890)</param>
    /// <param name="body">Message body</param>
    /// <param name="from">Optional sender number/name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send result</returns>
    Task<SmsSendResult> SendAsync(string to, string body, string? from = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send SMS to multiple recipients
    /// </summary>
    /// <param name="recipients">List of recipient phone numbers</param>
    /// <param name="body">Message body</param>
    /// <param name="from">Optional sender number/name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Send results for each recipient</returns>
    Task<IEnumerable<SmsSendResult>> SendBulkAsync(List<string> recipients, string body, string? from = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get delivery status of a sent message
    /// </summary>
    /// <param name="messageId">Message ID</param>
    /// <param name="providerMessageId">Provider-specific message ID (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Delivery status</returns>
    Task<DeliveryStatus> GetDeliveryStatusAsync(Guid messageId, string? providerMessageId = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate a phone number
    /// </summary>
    /// <param name="phoneNumber">Phone number to validate</param>
    /// <param name="defaultCountryCode">Default country code for local numbers</param>
    /// <returns>Validation result</returns>
    SmsValidationResult ValidatePhoneNumber(string phoneNumber, string? defaultCountryCode = null);
    
    /// <summary>
    /// Check SMS service connection
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connected successfully</returns>
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get account balance/credits (if supported by provider)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Balance information or null if not supported</returns>
    Task<BalanceInfo?> GetBalanceAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get the provider name
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// Get supported features
    /// </summary>
    SmsFeatures SupportedFeatures { get; }
}

/// <summary>
/// SMS Service Factory - creates appropriate service for configured provider
/// </summary>
public interface ISmsServiceFactory
{
    /// <summary>
    /// Get the primary SMS service
    /// </summary>
    ISmsService GetService();
    
    /// <summary>
    /// Get SMS service for specific provider
    /// </summary>
    /// <param name="provider">Provider type</param>
    /// <returns>SMS service</returns>
    ISmsService GetService(SmsProvider provider);
    
    /// <summary>
    /// Get all available SMS services
    /// </summary>
    IEnumerable<ISmsService> GetAllServices();
}

/// <summary>
/// SMS send result
/// </summary>
public class SmsSendResult
{
    /// <summary>
    /// Whether the send was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Message ID
    /// </summary>
    public Guid MessageId { get; set; }
    
    /// <summary>
    /// Provider-specific message ID
    /// </summary>
    public string? ProviderMessageId { get; set; }
    
    /// <summary>
    /// Status message
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Error details if failed
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Error code from provider
    /// </summary>
    public string? ErrorCode { get; set; }
    
    /// <summary>
    /// Number of message segments
    /// </summary>
    public int Segments { get; set; } = 1;
    
    /// <summary>
    /// Cost of the message (if available)
    /// </summary>
    public decimal? Cost { get; set; }
    
    /// <summary>
    /// Currency of the cost
    /// </summary>
    public string? Currency { get; set; }
    
    /// <summary>
    /// When the message was sent
    /// </summary>
    public DateTime? SentAt { get; set; }
    
    /// <summary>
    /// Provider name used
    /// </summary>
    public string? ProviderName { get; set; }
    
    /// <summary>
    /// Create a success result
    /// </summary>
    public static SmsSendResult CreateSuccess(Guid messageId, string providerMessageId, string providerName, int segments = 1)
    {
        return new SmsSendResult
        {
            Success = true,
            MessageId = messageId,
            ProviderMessageId = providerMessageId,
            ProviderName = providerName,
            Segments = segments,
            Message = $"SMS sent successfully via {providerName}",
            SentAt = DateTime.UtcNow
        };
    }
    
    /// <summary>
    /// Create a failure result
    /// </summary>
    public static SmsSendResult CreateFailure(Guid messageId, string error, string? errorCode = null)
    {
        return new SmsSendResult
        {
            Success = false,
            MessageId = messageId,
            Message = "Failed to send SMS",
            Error = error,
            ErrorCode = errorCode
        };
    }
}

/// <summary>
/// Account balance information
/// </summary>

public class BalanceInfo
{
    /// <summary>
    /// Current balance amount
    /// </summary>
    public decimal Balance { get; set; }
    
    /// <summary>
    /// Currency code
    /// </summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Estimated number of messages remaining
    /// </summary>
    public int? EstimatedMessagesRemaining { get; set; }
}

/// <summary>
/// SMS service features
/// </summary>
public class SmsFeatures
{
    /// <summary>
    /// Supports delivery reports
    /// </summary>
    public bool SupportsDeliveryReports { get; set; }
    
    /// <summary>
    /// Supports scheduled messages
    /// </summary>
    public bool SupportsScheduledMessages { get; set; }
    
    /// <summary>
    /// Supports two-way messaging (receiving replies)
    /// </summary>
    public bool SupportsTwoWay { get; set; }
    
    /// <summary>
    /// Supports alphanumeric sender ID
    /// </summary>
    public bool SupportsAlphanumericSender { get; set; }
    
    /// <summary>
    /// Supports flash messages
    /// </summary>
    public bool SupportsFlashMessages { get; set; }
    
    /// <summary>
    /// Supports Unicode messages
    /// </summary>
    public bool SupportsUnicode { get; set; } = true;
    
    /// <summary>
    /// Supports bulk sending
    /// </summary>
    public bool SupportsBulkSending { get; set; } = true;
    
    /// <summary>
    /// Supports balance checking
    /// </summary>
    public bool SupportsBalanceCheck { get; set; }
    
    /// <summary>
    /// Maximum message length
    /// </summary>
    public int MaxMessageLength { get; set; }
    
    /// <summary>
    /// Supports message templates
    /// </summary>
    public bool SupportsTemplates { get; set; }
}

namespace SmartVoiceAgent.Mailing.Entities;

/// <summary>
/// Represents an SMS message to be sent
/// </summary>
public class SmsMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// Recipient phone number (E.164 format: +1234567890)
    /// </summary>
    public string To { get; set; } = string.Empty;
    
    /// <summary>
    /// Sender phone number or alphanumeric sender ID
    /// </summary>
    public string? From { get; set; }
    
    /// <summary>
    /// Message content
    /// </summary>
    public string Body { get; set; } = string.Empty;
    
    /// <summary>
    /// Message type
    /// </summary>
    public SmsType Type { get; set; } = SmsType.Text;
    
    /// <summary>
    /// Whether to use Unicode (for non-ASCII characters like Turkish, Arabic, etc.)
    /// </summary>
    public bool UseUnicode { get; set; }
    
    /// <summary>
    /// Scheduled delivery time (null for immediate)
    /// </summary>
    public DateTime? ScheduledTime { get; set; }
    
    /// <summary>
    /// Message status
    /// </summary>
    public SmsStatus Status { get; set; } = SmsStatus.Pending;
    
    /// <summary>
    /// Provider-specific message ID
    /// </summary>
    public string? ProviderMessageId { get; set; }
    
    /// <summary>
    /// Number of message parts (for long messages)
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
    /// Error message if sending failed
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryCount { get; set; }
    
    /// <summary>
    /// When the message was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// When the message was sent
    /// </summary>
    public DateTime? SentAt { get; set; }
    
    /// <summary>
    /// When the delivery status was last updated
    /// </summary>
    public DateTime? StatusUpdatedAt { get; set; }
    
    /// <summary>
    /// Delivery receipt information
    /// </summary>
    public DeliveryReceipt? DeliveryReceipt { get; set; }
    
    /// <summary>
    /// Tags for categorization
    /// </summary>
    public List<string> Tags { get; set; } = new();
    
    /// <summary>
    /// User ID who initiated the send
    /// </summary>
    public string? UserId { get; set; }
    
    /// <summary>
    /// Provider name used to send
    /// </summary>
    public string? ProviderName { get; set; }
    
    /// <summary>
    /// Calculate the number of segments for this message
    /// </summary>
    public int CalculateSegments()
    {
        const int gsm7BitLimit = 160;
        const int gsm7BitConcatLimit = 153;
        const int unicodeLimit = 70;
        const int unicodeConcatLimit = 67;
        
        if (string.IsNullOrEmpty(Body))
            return 1;
        
        // Check if message contains non-GSM characters
        var requiresUnicode = UseUnicode || !IsGsm7Bit(Body);
        
        if (!requiresUnicode)
        {
            // GSM-7 encoding
            if (Body.Length <= gsm7BitLimit)
                return 1;
            return (int)Math.Ceiling((double)Body.Length / gsm7BitConcatLimit);
        }
        else
        {
            // UCS-2 encoding (Unicode)
            if (Body.Length <= unicodeLimit)
                return 1;
            return (int)Math.Ceiling((double)Body.Length / unicodeConcatLimit);
        }
    }
    
    /// <summary>
    /// Check if string contains only GSM 7-bit characters
    /// </summary>
    private static bool IsGsm7Bit(string text)
    {
        // GSM 7-bit default alphabet
        const string gsm7BitChars = "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?" +
                                    "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
        
        return text.All(c => gsm7BitChars.Contains(c));
    }
    
    /// <summary>
    /// Validate the message
    /// </summary>
    public SmsValidationResult Validate()
    {
        var errors = new List<string>();
        
        // Validate phone number
        var phoneValidation = SmsValidationHelper.ValidatePhoneNumber(To);
        if (!phoneValidation.IsValid)
        {
            errors.AddRange(phoneValidation.Errors);
        }
        
        // Validate body
        if (string.IsNullOrWhiteSpace(Body))
        {
            errors.Add("Message body cannot be empty");
        }
        
        // Check message length
        var maxLength = UseUnicode ? 1000 : 2000; // Most providers limit
        if (Body?.Length > maxLength)
        {
            errors.Add($"Message body exceeds maximum length of {maxLength} characters");
        }
        
        // Check scheduled time
        if (ScheduledTime.HasValue && ScheduledTime.Value < DateTime.UtcNow)
        {
            errors.Add("Scheduled time cannot be in the past");
        }
        
        return errors.Count == 0
            ? SmsValidationResult.Valid()
            : SmsValidationResult.Invalid(errors.ToArray());
    }
}

/// <summary>
/// SMS message type
/// </summary>
public enum SmsType
{
    /// <summary>
    /// Standard text message
    /// </summary>
    Text,
    
    /// <summary>
    /// Flash message (displayed immediately on screen, not saved)
    /// </summary>
    Flash,
    
    /// <summary>
    /// Binary message (for ringtones, images, etc.)
    /// </summary>
    Binary,
    
    /// <summary>
    /// Unicode message (for non-GSM characters)
    /// </summary>
    Unicode,
    
    /// <summary>
    /// WAP Push message
    /// </summary>
    WapPush
}

/// <summary>
/// SMS message status
/// </summary>
public enum SmsStatus
{
    Pending,
    Queued,
    Sending,
    Sent,
    Delivered,
    Failed,
    Expired,
    Rejected,
    Unknown
}

/// <summary>
/// Delivery receipt information
/// </summary>
public class DeliveryReceipt
{
    /// <summary>
    /// Delivery status
    /// </summary>
    public DeliveryStatus Status { get; set; }
    
    /// <summary>
    /// When the message was delivered
    /// </summary>
    public DateTime? DeliveredAt { get; set; }
    
    /// <summary>
    /// Network code
    /// </summary>
    public string? NetworkCode { get; set; }
    
    /// <summary>
    /// Error code if failed
    /// </summary>
    public string? ErrorCode { get; set; }
    
    /// <summary>
    /// Raw receipt data from provider
    /// </summary>
    public string? RawData { get; set; }
}

/// <summary>
/// Delivery status
/// </summary>
public enum DeliveryStatus
{
    Delivered,
    Pending,
    Failed,
    Expired,
    Rejected,
    Unknown
}

/// <summary>
/// SMS validation helper
/// </summary>
public static class SmsValidationHelper
{
    /// <summary>
    /// Validate phone number in E.164 format
    /// </summary>
    public static SmsValidationResult ValidatePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return SmsValidationResult.Invalid("Phone number is required");
        }
        
        // Remove all non-digit characters except +
        var cleaned = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
        
        // Check format
        if (!cleaned.StartsWith("+"))
        {
            return SmsValidationResult.Invalid("Phone number must include country code (e.g., +90 for Turkey)");
        }
        
        // Remove + for length check
        var digitsOnly = cleaned.Substring(1);
        
        if (digitsOnly.Length < 7 || digitsOnly.Length > 15)
        {
            return SmsValidationResult.Invalid("Phone number must be between 7 and 15 digits (excluding country code)");
        }
        
        return SmsValidationResult.Valid(cleaned);
    }
    
    /// <summary>
    /// Format phone number to E.164
    /// </summary>
    public static string FormatToE164(string phoneNumber, string defaultCountryCode = "+1")
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return string.Empty;
        
        // Remove all non-digit characters
        var digitsOnly = new string(phoneNumber.Where(char.IsDigit).ToArray());
        
        // If doesn't start with country code, add default
        if (!phoneNumber.Trim().StartsWith("+"))
        {
            // Remove leading 0 if present (common in local numbers)
            if (digitsOnly.StartsWith("0"))
            {
                digitsOnly = digitsOnly.Substring(1);
            }
            digitsOnly = defaultCountryCode.TrimStart('+') + digitsOnly;
        }
        
        return "+" + digitsOnly;
    }
}

/// <summary>
/// SMS validation result
/// </summary>
public class SmsValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? NormalizedPhoneNumber { get; set; }
    
    public static SmsValidationResult Valid(string? normalizedNumber = null)
    {
        return new SmsValidationResult
        {
            IsValid = true,
            NormalizedPhoneNumber = normalizedNumber
        };
    }
    
    public static SmsValidationResult Invalid(params string[] errors)
    {
        return new SmsValidationResult
        {
            IsValid = false,
            Errors = errors.ToList()
        };
    }
}

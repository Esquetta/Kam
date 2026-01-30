namespace SmartVoiceAgent.Mailing.Entities;

/// <summary>
/// SMS Provider types
/// </summary>
public enum SmsProvider
{
    Custom,
    Twilio,
    Vonage,
    AwsSns,
    MessageBird,
    Plivo,
    Clickatell,
    Sinch,
    Infobip,
    TeleSign
}

/// <summary>
/// Authentication method for SMS providers
/// </summary>
public enum SmsAuthMethod
{
    ApiKey,
    ApiKeyAndSecret,
    UsernameAndPassword,
    Token,
    OAuth2,
    AwsCredentials
}

/// <summary>
/// SMS configuration settings
/// </summary>
public class SmsSettings
{
    /// <summary>
    /// Primary SMS provider
    /// </summary>
    public SmsProvider Provider { get; set; } = SmsProvider.Twilio;
    
    /// <summary>
    /// Authentication method
    /// </summary>
    public SmsAuthMethod AuthMethod { get; set; } = SmsAuthMethod.ApiKeyAndSecret;
    
    // Twilio-specific settings
    /// <summary>
    /// Twilio Account SID
    /// </summary>
    public string? TwilioAccountSid { get; set; }
    
    /// <summary>
    /// Twilio Auth Token
    /// </summary>
    public string? TwilioAuthToken { get; set; }
    
    /// <summary>
    /// Default Twilio phone number
    /// </summary>
    public string? TwilioPhoneNumber { get; set; }
    
    // Vonage/Nexmo-specific settings
    /// <summary>
    /// Vonage API Key
    /// </summary>
    public string? VonageApiKey { get; set; }
    
    /// <summary>
    /// Vonage API Secret
    /// </summary>
    public string? VonageApiSecret { get; set; }
    
    /// <summary>
    /// Default Vonage sender name/number
    /// </summary>
    public string? VonageFrom { get; set; }
    
    // AWS SNS-specific settings
    /// <summary>
    /// AWS Access Key ID
    /// </summary>
    public string? AwsAccessKeyId { get; set; }
    
    /// <summary>
    /// AWS Secret Access Key
    /// </summary>
    public string? AwsSecretAccessKey { get; set; }
    
    /// <summary>
    /// AWS Region
    /// </summary>
    public string AwsRegion { get; set; } = "us-east-1";
    
    /// <summary>
    /// AWS SNS Sender ID
    /// </summary>
    public string? AwsSnsSenderId { get; set; }
    
    // MessageBird-specific settings
    /// <summary>
    /// MessageBird API Key
    /// </summary>
    public string? MessageBirdApiKey { get; set; }
    
    /// <summary>
    /// Default MessageBird originator
    /// </summary>
    public string? MessageBirdOriginator { get; set; }
    
    // Plivo-specific settings
    /// <summary>
    /// Plivo Auth ID
    /// </summary>
    public string? PlivoAuthId { get; set; }
    
    /// <summary>
    /// Plivo Auth Token
    /// </summary>
    public string? PlivoAuthToken { get; set; }
    
    /// <summary>
    /// Default Plivo source number
    /// </summary>
    public string? PlivoSourceNumber { get; set; }
    
    // Generic/Custom provider settings
    /// <summary>
    /// API Endpoint URL (for custom providers)
    /// </summary>
    public string? ApiEndpoint { get; set; }
    
    /// <summary>
    /// API Key
    /// </summary>
    public string? ApiKey { get; set; }
    
    /// <summary>
    /// API Secret
    /// </summary>
    public string? ApiSecret { get; set; }
    
    /// <summary>
    /// Username
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Password
    /// </summary>
    public string? Password { get; set; }
    
    /// <summary>
    /// Default sender number/name
    /// </summary>
    public string? DefaultSender { get; set; }
    
    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int Timeout { get; set; } = 30;
    
    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// Enable delivery receipts
    /// </summary>
    public bool EnableDeliveryReports { get; set; } = true;
    
    /// <summary>
    /// Webhook URL for delivery reports
    /// </summary>
    public string? WebhookUrl { get; set; }
    
    /// <summary>
    /// Default country code for local numbers
    /// </summary>
    public string DefaultCountryCode { get; set; } = "+1";
    
    /// <summary>
    /// Whether to use Unicode for non-GSM characters
    /// </summary>
    public bool AutoDetectUnicode { get; set; } = true;
    
    /// <summary>
    /// Rate limit: messages per minute
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 100;
    
    /// <summary>
    /// Enable sandbox mode (for testing)
    /// </summary>
    public bool SandboxMode { get; set; } = false;

    #region Factory Methods

    /// <summary>
    /// Create Twilio settings
    /// </summary>
    public static SmsSettings Twilio(string accountSid, string authToken, string fromNumber)
    {
        return new SmsSettings
        {
            Provider = SmsProvider.Twilio,
            AuthMethod = SmsAuthMethod.ApiKeyAndSecret,
            TwilioAccountSid = accountSid,
            TwilioAuthToken = authToken,
            TwilioPhoneNumber = fromNumber,
            DefaultSender = fromNumber
        };
    }

    /// <summary>
    /// Create Vonage settings
    /// </summary>
    public static SmsSettings Vonage(string apiKey, string apiSecret, string from)
    {
        return new SmsSettings
        {
            Provider = SmsProvider.Vonage,
            AuthMethod = SmsAuthMethod.ApiKeyAndSecret,
            VonageApiKey = apiKey,
            VonageApiSecret = apiSecret,
            VonageFrom = from,
            DefaultSender = from
        };
    }

    /// <summary>
    /// Create AWS SNS settings
    /// </summary>
    public static SmsSettings AwsSns(string accessKeyId, string secretAccessKey, string region = "us-east-1", string? senderId = null)
    {
        return new SmsSettings
        {
            Provider = SmsProvider.AwsSns,
            AuthMethod = SmsAuthMethod.AwsCredentials,
            AwsAccessKeyId = accessKeyId,
            AwsSecretAccessKey = secretAccessKey,
            AwsRegion = region,
            AwsSnsSenderId = senderId
        };
    }

    /// <summary>
    /// Create MessageBird settings
    /// </summary>
    public static SmsSettings MessageBird(string apiKey, string originator)
    {
        return new SmsSettings
        {
            Provider = SmsProvider.MessageBird,
            AuthMethod = SmsAuthMethod.ApiKey,
            MessageBirdApiKey = apiKey,
            MessageBirdOriginator = originator,
            DefaultSender = originator
        };
    }

    /// <summary>
    /// Create custom provider settings
    /// </summary>
    public static SmsSettings Custom(string apiEndpoint, string apiKey, string? apiSecret = null, string? defaultSender = null)
    {
        return new SmsSettings
        {
            Provider = SmsProvider.Custom,
            AuthMethod = string.IsNullOrEmpty(apiSecret) ? SmsAuthMethod.ApiKey : SmsAuthMethod.ApiKeyAndSecret,
            ApiEndpoint = apiEndpoint,
            ApiKey = apiKey,
            ApiSecret = apiSecret,
            DefaultSender = defaultSender
        };
    }

    #endregion
}

/// <summary>
/// SMS sending options
/// </summary>
public class SmsSendingOptions
{
    /// <summary>
    /// Default country code for local numbers
    /// </summary>
    public string DefaultCountryCode { get; set; } = "+1";
    
    /// <summary>
    /// Whether to automatically detect and use Unicode for non-GSM characters
    /// </summary>
    public bool AutoDetectUnicode { get; set; } = true;
    
    /// <summary>
    /// Maximum message length (0 = unlimited)
    /// </summary>
    public int MaxMessageLength { get; set; } = 0;
    
    /// <summary>
    /// Whether to split long messages automatically
    /// </summary>
    public bool AutoSplitLongMessages { get; set; } = true;
    
    /// <summary>
    /// Rate limit: messages per minute
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 100;
    
    /// <summary>
    /// Enable sandbox mode (for testing)
    /// </summary>
    public bool SandboxMode { get; set; } = false;
    
    /// <summary>
    /// Callback URL for delivery reports
    /// </summary>
    public string? DeliveryReportCallbackUrl { get; set; }
    
    /// <summary>
    /// Enable message validation before sending
    /// </summary>
    public bool ValidateBeforeSend { get; set; } = true;
    
    /// <summary>
    /// Default validity period in hours (0 = provider default)
    /// </summary>
    public int DefaultValidityHours { get; set; } = 24;
}
